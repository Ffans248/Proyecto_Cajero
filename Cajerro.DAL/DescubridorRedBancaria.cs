using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cajerro.DAL.Interfaces;
using Cajerro.DAL.Modelos;

namespace Cajerro.DAL
{
    /// <summary>
    /// Servicio de autodescubrimiento de red e interconexión bancaria LAN (UDP Broadcast 5005 + Escaneo TCP 5000).
    /// Permite:
    /// 1. Identificar a qué red Wi-Fi / Ethernet / VPN está conectada esta computadora (SSID, IP local, Subred, Gateway).
    /// 2. Escuchar peticiones de otros bancos en la misma red (UDP 5005 y TCP 5000).
    /// 3. Detectar automáticamente otros bancos conectados a la misma red y sincronizar su IP y estado en línea.
    /// </summary>
    public static class DescubridorRedBancaria
    {
        public const int PuertoUdpDiscovery = 5005;
        public const int PuertoTcpInterbancario = 5000;

        private static bool _serviciosIniciados = false;
        private static readonly object _lockInicio = new();
        private static UdpClient? _udpListener;
        private static TcpListener? _tcpListener;
        private static CancellationTokenSource? _cts;

        /// <summary>
        /// Inicia en segundo plano el respondedor UDP (puerto 5005) y el nodo TCP interbancario (puerto 5000 si está libre)
        /// para que otros bancos en la misma red puedan detectar esta instancia automáticamente.
        /// </summary>
        public static void AsegurarNodoEnRedActivo(IGestorArchivos dal)
        {
            lock (_lockInicio)
            {
                if (_serviciosIniciados) return;
                _serviciosIniciados = true;
                _cts = new CancellationTokenSource();

                Task.Run(() => EscucharUdpDiscoveryLoopAsync(dal, _cts.Token));
                Task.Run(() => EscucharTcpInterbancarioLoopAsync(dal, _cts.Token));
            }
        }

        /// <summary>
        /// Obtiene la información de la red activa (SSID Wi-Fi, IPv4, Subred, Gateway, VPN) y escanea en paralelo
        /// la red local en busca de otros bancos conectados (por UDP Broadcast 5005 y TCP Handshake 5000/5001).
        /// </summary>
        public static async Task<ResultadoEscaneoRed> EscanearRedYDetectarBancosAsync(IGestorArchivos dal, int timeoutMs = 950)
        {
            AsegurarNodoEnRedActivo(dal);

            var resultado = ObtenerInformacionRedActual();
            var bancosRegistrados = dal.ObtenerBancosRed();
            var bancoPropio = bancosRegistrados.FirstOrDefault(b => b.EsPropio) ?? new BancoRedInfo
            {
                CodigoBanco = "BANC01",
                NombreBanco = "NEXUSBANK (Nuestro Banco)",
                EsPropio = true,
                Puerto = PuertoTcpInterbancario,
                LimiteDiario = 25000m
            };

            bancoPropio.IP = resultado.IpLocal;
            bancoPropio.EnLinea = true;

            var bancosDescubiertos = new ConcurrentDictionary<string, BancoRedInfo>(StringComparer.OrdinalIgnoreCase);

            // 1. Descubrimiento rápido por UDP Broadcast (Puerto 5005)
            await EnviarBroadcastUdpYRecibirAsync(bancoPropio, resultado.IpLocal, bancosDescubiertos, 450);

            // 2. Obtener hosts candidatos (tabla ARP + barrido rápido de la subred local /24)
            var ipsCandidatas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in resultado.HostsActivosEnLan)
            {
                if (!EsIpPropia(h, resultado.IpLocal))
                    ipsCandidatas.Add(h);
            }

            string? prefijo = ObtenerPrefijoTresOctetos(resultado.IpLocal);
            if (!string.IsNullOrEmpty(prefijo))
            {
                var ipsSubred = Enumerable.Range(1, 254)
                    .Select(i => $"{prefijo}.{i}")
                    .Where(ip => !EsIpPropia(ip, resultado.IpLocal))
                    .ToList();

                var tareasSondeo = ipsSubred.Select(async ip =>
                {
                    if (await ProbarPuertoTcpRapidoAsync(ip, PuertoTcpInterbancario, 160))
                    {
                        lock (ipsCandidatas)
                        {
                            ipsCandidatas.Add(ip);
                            if (!resultado.HostsActivosEnLan.Contains(ip))
                                resultado.HostsActivosEnLan.Add(ip);
                        }
                    }
                });
                await Task.WhenAll(tareasSondeo);
            }

            // 3. También incluir las IPs configuradas manualmente en BancosRed (si no son 127.0.0.1)
            foreach (var b in bancosRegistrados.Where(x => !x.EsPropio))
            {
                if (!string.IsNullOrWhiteSpace(b.IP) && b.IP != "127.0.0.1" && !EsIpPropia(b.IP, resultado.IpLocal))
                {
                    ipsCandidatas.Add(b.IP);
                }
            }

            // 4. Consultar IB_PING por TCP a cada IP candidata en puertos 5000 y 5001
            var tareasPingBancario = new List<Task>();
            foreach (var ip in ipsCandidatas)
            {
                foreach (int puerto in new[] { 5000, 5001 })
                {
                    tareasPingBancario.Add(Task.Run(async () =>
                    {
                        var infoRemota = await ConsultarPingTcpBancoAsync(ip, puerto, bancoPropio.CodigoBanco, bancoPropio.NombreBanco, timeoutMs);
                        if (infoRemota != null && !string.Equals(infoRemota.CodigoBanco, bancoPropio.CodigoBanco, StringComparison.OrdinalIgnoreCase))
                        {
                            infoRemota.EnLinea = true;
                            bancosDescubiertos[infoRemota.CodigoBanco] = infoRemota;
                        }
                    }));
                }
            }

            await Task.WhenAll(tareasPingBancario);

            // 5. Sincronizar resultados con la lista de BancosRed
            foreach (var b in bancosRegistrados)
            {
                if (b.EsPropio)
                {
                    b.IP = resultado.IpLocal;
                    b.EnLinea = true;
                }
                else if (bancosDescubiertos.TryGetValue(b.CodigoBanco, out var det))
                {
                    b.IP = det.IP;
                    b.Puerto = det.Puerto;
                    b.NombreBanco = string.IsNullOrWhiteSpace(det.NombreBanco) ? b.NombreBanco : det.NombreBanco;
                    b.EnLinea = true;
                }
                else
                {
                    b.EnLinea = false;
                }
            }

            foreach (var kv in bancosDescubiertos)
            {
                if (!bancosRegistrados.Any(x => string.Equals(x.CodigoBanco, kv.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    bancosRegistrados.Add(kv.Value);
                }
            }

            dal.GuardarBancosRed(bancosRegistrados);

            resultado.BancosDetectados = bancosRegistrados.Where(x => !x.EsPropio && x.EnLinea).ToList();
            resultado.FechaEscaneo = DateTime.Now;
            return resultado;
        }

        /// <summary>
        /// Obtiene los detalles reales de la interfaz de red activa (SSID Wi-Fi, IPv4, Subred, Gateway, VPN y tabla ARP).
        /// </summary>
        public static ResultadoEscaneoRed ObtenerInformacionRedActual()
        {
            var res = new ResultadoEscaneoRed();

            try
            {
                string ssid = ObtenerSsidWifiActual();

                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                NetworkInterface? principal = interfaces
                    .FirstOrDefault(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                          ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                    ?? interfaces.FirstOrDefault(ni => ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !ni.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase)))
                    ?? interfaces.FirstOrDefault();

                var secundarias = new List<string>();

                foreach (var ni in interfaces)
                {
                    var props = ni.GetIPProperties();
                    var ipv4 = props.UnicastAddresses
                        .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork &&
                                             !IPAddress.IsLoopback(u.Address) &&
                                             !u.Address.ToString().StartsWith("169.254."));

                    if (ipv4 == null) continue;

                    if (principal != null && ni.Id == principal.Id)
                    {
                        res.IpLocal = ipv4.Address.ToString();
                        res.DescripcionAdaptador = ni.Description;
                        res.TipoInterfaz = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : ni.Name;
                        res.NombreRedSsid = !string.IsNullOrWhiteSpace(ssid) ? ssid : ni.Name;

                        var gw = props.GatewayAddresses
                            .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                        res.PuertaEnlace = gw?.Address.ToString() ?? "Sin Gateway";

                        string? pref = ObtenerPrefijoTresOctetos(res.IpLocal);
                        res.SubredCidr = pref != null ? $"{pref}.0/24" : $"{res.IpLocal}/24";
                    }
                    else
                    {
                        secundarias.Add($"{ni.Name}: {ipv4.Address}");
                    }
                }

                res.InterfacesSecundarias = secundarias.Count > 0 ? string.Join(" | ", secundarias) : "Ninguna";

                res.HostsActivosEnLan = ObtenerHostsTablaArp(ObtenerPrefijoTresOctetos(res.IpLocal));
                if (!string.IsNullOrWhiteSpace(res.IpLocal) && !res.HostsActivosEnLan.Contains(res.IpLocal))
                {
                    res.HostsActivosEnLan.Insert(0, res.IpLocal);
                }
            }
            catch { }

            return res;
        }

        private static string ObtenerSsidWifiActual()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null) return string.Empty;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1500);

                foreach (var rawLine in output.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                        !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        int idx = line.IndexOf(':');
                        if (idx >= 0 && idx + 1 < line.Length)
                        {
                            return line[(idx + 1)..].Trim();
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private static List<string> ObtenerHostsTablaArp(string? prefijoSubred)
        {
            var lista = new List<string>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return lista;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1500);

                var matches = Regex.Matches(output, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
                foreach (Match m in matches)
                {
                    string ip = m.Groups[1].Value;
                    if (ip.EndsWith(".255") || ip.StartsWith("224.") || ip.StartsWith("239.") || ip == "255.255.255.255")
                        continue;

                    if (string.IsNullOrEmpty(prefijoSubred) || ip.StartsWith(prefijoSubred + "."))
                    {
                        if (!lista.Contains(ip))
                            lista.Add(ip);
                    }
                }
            }
            catch { }
            return lista;
        }

        private static async Task<bool> ProbarPuertoTcpRapidoAsync(string ip, int puerto, int timeoutMs)
        {
            try
            {
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(ip, puerto);
                var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                return completed == connectTask && tcp.Connected;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<BancoRedInfo?> ConsultarPingTcpBancoAsync(
            string ip,
            int puerto,
            string codigoBancoOrigen,
            string nombreBancoOrigen,
            int timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, puerto);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask || !client.Connected)
                    return null;

                using var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                stream.WriteTimeout = timeoutMs;

                // Formato estándar compatible con Cajero.Servidor y con agentes LLM externos
                var reqObj = new
                {
                    tipo = "IB_PING",
                    cajero = "SERVER",
                    idMensaje = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    bancoOrigen = codigoBancoOrigen,
                    nombreBancoOrigen = nombreBancoOrigen
                };

                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reqObj) + "\n");
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var readTask = reader.ReadLineAsync();
                if (await Task.WhenAny(readTask, Task.Delay(timeoutMs)) != readTask)
                    return null;

                string? linea = await readTask;
                if (string.IsNullOrWhiteSpace(linea))
                    return null;

                using var doc = JsonDocument.Parse(linea);
                var root = doc.RootElement;

                JsonElement datosElem = root;
                if (root.TryGetProperty("datos", out var d1) && d1.ValueKind == JsonValueKind.Object)
                {
                    datosElem = d1;
                }
                else if (root.TryGetProperty("Datos", out var d2))
                {
                    if (d2.ValueKind == JsonValueKind.Object)
                        datosElem = d2;
                    else if (d2.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d2.GetString()))
                        datosElem = JsonDocument.Parse(d2.GetString()!).RootElement;
                }

                string cod = ObtenerStringProp(datosElem, "codigoBanco", "CodigoBanco");
                string nom = ObtenerStringProp(datosElem, "nombreBanco", "NombreBanco");
                decimal disp = ObtenerDecimalProp(datosElem, 25000m, "cupoDisponibleHoy", "CupoDisponibleHoy", "cupoDisponibleBanco");

                if (string.IsNullOrWhiteSpace(cod))
                    return null;

                return new BancoRedInfo
                {
                    CodigoBanco = cod,
                    NombreBanco = string.IsNullOrWhiteSpace(nom) ? $"Banco {cod}" : nom,
                    IP = ip,
                    Puerto = puerto,
                    EsPropio = false,
                    EnLinea = true,
                    LimiteDiario = Math.Max(25000m, disp),
                    TransferidoHoy = Math.Max(0m, 25000m - disp)
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task EnviarBroadcastUdpYRecibirAsync(
            BancoRedInfo bancoPropio,
            string ipLocal,
            ConcurrentDictionary<string, BancoRedInfo> descubiertos,
            int waitMs)
        {
            try
            {
                using var udp = new UdpClient(0);
                udp.EnableBroadcast = true;

                string msg = $"DISCOVER_BANCO_ATM|{bancoPropio.CodigoBanco}|{bancoPropio.NombreBanco}|{ipLocal}|{bancoPropio.Puerto}";
                byte[] data = Encoding.UTF8.GetBytes(msg);

                await udp.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, PuertoUdpDiscovery));

                string? pref = ObtenerPrefijoTresOctetos(ipLocal);
                if (!string.IsNullOrEmpty(pref) && IPAddress.TryParse($"{pref}.255", out var bcastSubred))
                {
                    await udp.SendAsync(data, data.Length, new IPEndPoint(bcastSubred, PuertoUdpDiscovery));
                }

                var fin = DateTime.UtcNow.AddMilliseconds(waitMs);
                while (DateTime.UtcNow < fin)
                {
                    int restante = (int)(fin - DateTime.UtcNow).TotalMilliseconds;
                    if (restante <= 0) break;

                    var recvTask = udp.ReceiveAsync();
                    if (await Task.WhenAny(recvTask, Task.Delay(restante)) != recvTask)
                        break;

                    var res = await recvTask;
                    string texto = Encoding.UTF8.GetString(res.Buffer).Trim();
                    // Formato respuesta: HERE_BANCO_ATM|CodigoBanco|NombreBanco|IP|Puerto|LimiteDiario|TransferidoHoy
                    if (texto.StartsWith("HERE_BANCO_ATM|", StringComparison.OrdinalIgnoreCase))
                    {
                        var partes = texto.Split('|');
                        if (partes.Length >= 5)
                        {
                            string cod = partes[1].Trim();
                            string nom = partes[2].Trim();
                            string ip = res.RemoteEndPoint.Address.ToString();
                            int.TryParse(partes[4].Trim(), out int puerto);
                            decimal lim = partes.Length >= 6 && decimal.TryParse(partes[5], out var l) ? l : 25000m;
                            decimal trf = partes.Length >= 7 && decimal.TryParse(partes[6], out var t) ? t : 0m;

                            if (!string.Equals(cod, bancoPropio.CodigoBanco, StringComparison.OrdinalIgnoreCase))
                            {
                                descubiertos[cod] = new BancoRedInfo
                                {
                                    CodigoBanco = cod,
                                    NombreBanco = nom,
                                    IP = ip,
                                    Puerto = puerto > 0 ? puerto : PuertoTcpInterbancario,
                                    EsPropio = false,
                                    EnLinea = true,
                                    LimiteDiario = lim,
                                    TransferidoHoy = trf
                                };
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static async Task EscucharUdpDiscoveryLoopAsync(IGestorArchivos dal, CancellationToken token)
        {
            try
            {
                _udpListener = new UdpClient(PuertoUdpDiscovery);
                _udpListener.EnableBroadcast = true;

                while (!token.IsCancellationRequested)
                {
                    var recibido = await _udpListener.ReceiveAsync(token);
                    string msg = Encoding.UTF8.GetString(recibido.Buffer).Trim();

                    if (msg.StartsWith("DISCOVER_BANCO_ATM|", StringComparison.OrdinalIgnoreCase))
                    {
                        var infoRed = ObtenerInformacionRedActual();
                        var propio = dal.ObtenerBancosRed().FirstOrDefault(b => b.EsPropio);
                        if (propio != null)
                        {
                            string resp = $"HERE_BANCO_ATM|{propio.CodigoBanco}|{propio.NombreBanco}|{infoRed.IpLocal}|{propio.Puerto}|{propio.LimiteDiario}|{propio.TransferidoHoy}";
                            byte[] bytes = Encoding.UTF8.GetBytes(resp);
                            await _udpListener.SendAsync(bytes, bytes.Length, recibido.RemoteEndPoint);
                        }
                    }
                }
            }
            catch { }
        }

        private static async Task EscucharTcpInterbancarioLoopAsync(IGestorArchivos dal, CancellationToken token)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, PuertoTcpInterbancario);
                _tcpListener.Start();

                while (!token.IsCancellationRequested)
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => AtenderClienteTcpInterbancarioAsync(client, dal), token);
                }
            }
            catch { }
        }

        private static async Task AtenderClienteTcpInterbancarioAsync(TcpClient client, IGestorArchivos dal)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string? linea = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(linea)) return;

                    using var doc = JsonDocument.Parse(linea);
                    var root = doc.RootElement;
                    string idMsg = ObtenerStringProp(root, "idMensaje", "IdMensaje");
                    string tipo = ObtenerStringProp(root, "tipo", "Tipo");

                    var bancos = dal.ObtenerBancosRed();
                    var propio = bancos.FirstOrDefault(b => b.EsPropio) ?? new BancoRedInfo
                    {
                        CodigoBanco = "BANC01",
                        NombreBanco = "NEXUSBANK (Nuestro Banco)",
                        EsPropio = true,
                        LimiteDiario = 25000m
                    };

                    if (string.Equals(tipo, "IB_PING", StringComparison.OrdinalIgnoreCase))
                    {
                        var resp = new
                        {
                            tipo = "RESP_OK",
                            idMensaje = idMsg,
                            codigo = "00",
                            mensaje = $"Banco {propio.CodigoBanco} ({propio.NombreBanco}) en línea",
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            datos = new
                            {
                                codigoBanco = propio.CodigoBanco,
                                nombreBanco = propio.NombreBanco,
                                cupoDisponibleHoy = propio.DisponibleHoy,
                                estado = "EN_LINEA"
                            }
                        };
                        await EnviarJsonLineaAsync(stream, resp);
                    }
                    else if (string.Equals(tipo, "IB_VERIFICAR_CUENTA", StringComparison.OrdinalIgnoreCase))
                    {
                        string cuenta = ObtenerStringProp(root, "numeroCuenta", "cuentaDestino", "CuentaDestino");
                        var usuario = dal.ObtenerUsuarioPorTarjeta(cuenta);

                        var resp = new
                        {
                            tipo = "RESP_OK",
                            idMensaje = idMsg,
                            codigo = usuario != null ? "00" : "14",
                            mensaje = usuario != null ? "Cuenta verificada en el banco destino." : "La cuenta no existe en este banco.",
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            datos = new
                            {
                                existe = usuario != null,
                                codigoBanco = propio.CodigoBanco,
                                nombreBanco = propio.NombreBanco,
                                numeroCuenta = usuario?.NumeroTarjeta ?? cuenta,
                                nombreTitular = usuario?.Nombre ?? "",
                                cupoDisponibleBanco = propio.DisponibleHoy
                            }
                        };
                        await EnviarJsonLineaAsync(stream, resp);
                    }
                    else if (string.Equals(tipo, "IB_TRANSFERIR", StringComparison.OrdinalIgnoreCase))
                    {
                        string tokenTrf = ObtenerStringProp(root, "tokenTransferencia", "TokenTransferencia");
                        string bancoOrigen = ObtenerStringProp(root, "bancoOrigen", "BancoOrigen");
                        string cuentaOrigen = ObtenerStringProp(root, "cuentaOrigen", "CuentaOrigen");
                        string nombreOrigen = ObtenerStringProp(root, "nombreOrigen", "NombreOrigen");
                        string cuentaDestino = ObtenerStringProp(root, "cuentaDestino", "CuentaDestino");
                        string concepto = ObtenerStringProp(root, "concepto", "Concepto");
                        decimal monto = ObtenerDecimalProp(root, 0m, "monto", "Monto");

                        var usuarioDest = dal.ObtenerUsuarioPorTarjeta(cuentaDestino);
                        if (usuarioDest == null)
                        {
                            await EnviarJsonLineaAsync(stream, new
                            {
                                tipo = "RESP_OK",
                                idMensaje = idMsg,
                                codigo = "14",
                                mensaje = "La cuenta destino no existe en este banco.",
                                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                            return;
                        }

                        if (monto <= 0 || monto > propio.DisponibleHoy)
                        {
                            await EnviarJsonLineaAsync(stream, new
                            {
                                tipo = "RESP_OK",
                                idMensaje = idMsg,
                                codigo = "65",
                                mensaje = $"El monto excede el límite diario disponible del banco destino (Disp: Q {propio.DisponibleHoy:N2}).",
                                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                            return;
                        }

                        // Acreditar al usuario local, descontar cupo de ambos bancos y guardar el TokenTransferencia
                        usuarioDest.SaldoActual += monto;
                        dal.ActualizarUsuario(usuarioDest);

                        propio.TransferidoHoy += monto;
                        var bOrig = bancos.FirstOrDefault(b => string.Equals(b.CodigoBanco, bancoOrigen, StringComparison.OrdinalIgnoreCase));
                        if (bOrig != null)
                        {
                            bOrig.TransferidoHoy += monto;
                        }
                        dal.GuardarBancosRed(bancos);

                        if (string.IsNullOrWhiteSpace(tokenTrf))
                            tokenTrf = $"TRF-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";

                        dal.RegistrarTransaccion(new Transaccion(
                            usuarioDest.NumeroTarjeta,
                            DateTime.Now,
                            $"Transferencia Recibida ({bancoOrigen} Cta:{cuentaOrigen})",
                            monto,
                            tokenTrf));

                        dal.RegistrarTransferenciaCompartida(new RegistroTransferenciaBancaria
                        {
                            TokenTransferencia = tokenTrf,
                            FechaHora = DateTime.Now,
                            TipoTransferencia = "INTERBANCARIA",
                            BancoOrigen = bancoOrigen,
                            CuentaOrigen = cuentaOrigen,
                            NombreOrigen = nombreOrigen,
                            BancoDestino = propio.CodigoBanco,
                            CuentaDestino = usuarioDest.NumeroTarjeta,
                            NombreDestino = usuarioDest.Nombre,
                            Monto = monto,
                            Concepto = concepto,
                            CupoRestanteBancoOrigen = bOrig?.DisponibleHoy ?? propio.DisponibleHoy,
                            CupoRestanteBancoDestino = propio.DisponibleHoy,
                            Estado = "APROBADA"
                        });

                        await EnviarJsonLineaAsync(stream, new
                        {
                            tipo = "RESP_OK",
                            idMensaje = idMsg,
                            codigo = "00",
                            mensaje = "Transferencia interbancaria acreditada y token registrado.",
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            datos = new
                            {
                                tokenTransferencia = tokenTrf,
                                bancoDestino = propio.CodigoBanco,
                                cuentaDestino = usuarioDest.NumeroTarjeta,
                                nombreDestino = usuarioDest.Nombre,
                                monto = monto,
                                cupoRestanteBancoDestino = propio.DisponibleHoy
                            }
                        });
                    }
                }
                catch { }
            }
        }

        private static async Task EnviarJsonLineaAsync(NetworkStream stream, object obj)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj) + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        private static string ObtenerStringProp(JsonElement elem, params string[] nombres)
        {
            if (elem.ValueKind != JsonValueKind.Object) return string.Empty;
            foreach (var n in nombres)
            {
                if (elem.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
                    return p.GetString() ?? string.Empty;
            }
            return string.Empty;
        }

        private static decimal ObtenerDecimalProp(JsonElement elem, decimal porDefecto, params string[] nombres)
        {
            if (elem.ValueKind != JsonValueKind.Object) return porDefecto;
            foreach (var n in nombres)
            {
                if (elem.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d))
                    return d;
            }
            return porDefecto;
        }

        private static string? ObtenerPrefijoTresOctetos(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return null;
            var partes = ip.Split('.');
            if (partes.Length == 4)
                return $"{partes[0]}.{partes[1]}.{partes[2]}";
            return null;
        }

        private static bool EsIpPropia(string ip, string ipLocalActual)
        {
            if (string.IsNullOrWhiteSpace(ip)) return true;
            if (ip == "127.0.0.1" || ip == "0.0.0.0" || string.Equals(ip, ipLocalActual, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        /// <summary>
        /// Verifica en tiempo real por TCP (puerto 5000) si una cuenta existe en otro banco conectado a la misma red.
        /// </summary>
        public static (bool respondioRed, bool existe, string numeroCuenta, string nombreTitular, decimal? cupoRemoto, string mensaje) VerificarCuentaEnBancoRemotoTcp(
            BancoRedInfo bancoDestino,
            string codigoBancoOrigen,
            string cuentaDestino,
            int timeoutMs = 2200)
        {
            if (string.IsNullOrWhiteSpace(bancoDestino.IP) || bancoDestino.IP == "127.0.0.1")
                return (false, false, cuentaDestino, "", null, "Banco en modo local.");

            try
            {
                using var client = new TcpClient();
                var ct = client.ConnectAsync(bancoDestino.IP, bancoDestino.Puerto);
                if (!ct.Wait(timeoutMs) || !client.Connected)
                    return (false, false, cuentaDestino, "", null, "El banco remoto no respondió por TCP.");

                using var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                stream.WriteTimeout = timeoutMs;

                var req = new
                {
                    tipo = "IB_VERIFICAR_CUENTA",
                    cajero = "SERVER",
                    idMensaje = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    bancoOrigen = codigoBancoOrigen,
                    bancoDestino = bancoDestino.CodigoBanco,
                    numeroCuenta = cuentaDestino.Trim(),
                    cuentaDestino = cuentaDestino.Trim()
                };

                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(req) + "\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();

                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? linea = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(linea))
                    return (false, false, cuentaDestino, "", null, "Respuesta vacía del banco remoto.");

                using var doc = JsonDocument.Parse(linea);
                var root = doc.RootElement;
                string codResp = ObtenerStringProp(root, "codigo", "CodigoRespuesta", "Codigo");
                string msjResp = ObtenerStringProp(root, "mensaje", "Mensaje");

                JsonElement datosElem = root;
                if (root.TryGetProperty("datos", out var d1) && d1.ValueKind == JsonValueKind.Object)
                    datosElem = d1;
                else if (root.TryGetProperty("Datos", out var d2))
                {
                    if (d2.ValueKind == JsonValueKind.Object)
                        datosElem = d2;
                    else if (d2.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d2.GetString()))
                        datosElem = JsonDocument.Parse(d2.GetString()!).RootElement;
                }

                bool existe = codResp == "00" ||
                              (datosElem.TryGetProperty("existe", out var pe1) && pe1.ValueKind == JsonValueKind.True) ||
                              (datosElem.TryGetProperty("Existe", out var pe2) && pe2.ValueKind == JsonValueKind.True);

                string ctaNorm = ObtenerStringProp(datosElem, "numeroCuenta", "NumeroCuenta", "cuentaDestino", "CuentaDestino");
                if (string.IsNullOrWhiteSpace(ctaNorm)) ctaNorm = cuentaDestino.Trim();

                string titular = ObtenerStringProp(datosElem, "nombreTitular", "NombreTitular");
                decimal cupo = ObtenerDecimalProp(datosElem, bancoDestino.DisponibleHoy, "cupoDisponibleBanco", "CupoDisponibleBancoDestino", "cupoDisponibleHoy");

                return (true, existe, ctaNorm, titular, cupo, string.IsNullOrWhiteSpace(msjResp) ? "Verificado en red" : msjResp);
            }
            catch
            {
                return (false, false, cuentaDestino, "", null, "No se pudo conectar al banco remoto.");
            }
        }

        /// <summary>
        /// Envía en tiempo real por TCP (puerto 5000) la orden IB_TRANSFERIR con el TokenTransferencia al banco destino conectado en la red.
        /// </summary>
        public static (bool respondioRed, bool aprobada, decimal? cupoRestanteDestino, string mensaje) EnviarTransferenciaABancoRemotoTcp(
            BancoRedInfo bancoDestino,
            string tokenTransferencia,
            string bancoOrigen,
            string cuentaOrigen,
            string nombreOrigen,
            string cuentaDestino,
            string nombreDestino,
            decimal monto,
            string concepto,
            int timeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(bancoDestino.IP) || bancoDestino.IP == "127.0.0.1")
                return (false, false, null, "Banco en modo local.");

            try
            {
                using var client = new TcpClient();
                var ct = client.ConnectAsync(bancoDestino.IP, bancoDestino.Puerto);
                if (!ct.Wait(timeoutMs) || !client.Connected)
                    return (false, false, null, "El banco remoto no respondió por TCP.");

                using var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                stream.WriteTimeout = timeoutMs;

                var req = new
                {
                    tipo = "IB_TRANSFERIR",
                    cajero = "SERVER",
                    idMensaje = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    tokenTransferencia = tokenTransferencia,
                    bancoOrigen = bancoOrigen,
                    cuentaOrigen = cuentaOrigen,
                    nombreOrigen = nombreOrigen,
                    bancoDestino = bancoDestino.CodigoBanco,
                    cuentaDestino = cuentaDestino,
                    nombreDestino = nombreDestino,
                    monto = monto,
                    concepto = concepto
                };

                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(req) + "\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();

                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? linea = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(linea))
                    return (false, false, null, "Respuesta vacía del banco remoto.");

                using var doc = JsonDocument.Parse(linea);
                var root = doc.RootElement;
                string codResp = ObtenerStringProp(root, "codigo", "CodigoRespuesta", "Codigo");
                string msjResp = ObtenerStringProp(root, "mensaje", "Mensaje");

                JsonElement datosElem = root;
                if (root.TryGetProperty("datos", out var d1) && d1.ValueKind == JsonValueKind.Object)
                    datosElem = d1;
                else if (root.TryGetProperty("Datos", out var d2))
                {
                    if (d2.ValueKind == JsonValueKind.Object)
                        datosElem = d2;
                    else if (d2.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d2.GetString()))
                        datosElem = JsonDocument.Parse(d2.GetString()!).RootElement;
                }

                decimal cupoRest = ObtenerDecimalProp(datosElem, -1m, "cupoRestanteBancoDestino", "CupoRestanteBancoDestino");
                return (true, codResp == "00", cupoRest >= 0m ? cupoRest : null, msjResp);
            }
            catch
            {
                return (false, false, null, "Error de red al transferir al banco remoto.");
            }
        }
    }
}
