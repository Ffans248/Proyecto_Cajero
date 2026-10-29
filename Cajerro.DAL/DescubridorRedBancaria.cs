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
    /// Servicio Maestro de Red, Autodescubrimiento de Cajeros/Bancos (UDP 5005) y Gateway de Base de Datos Maestra (TCP 5000).
    /// Este equipo aloja la Base de Datos Maestra para todos los cajeros y bancos conectados a la red:
    /// 1. Detecta a qué red Wi-Fi / LAN / VPN está conectado este equipo.
    /// 2. Detecta automáticamente todos los demás Cajeros (ATMs) y Bancos conectados a la misma red.
    /// 3. Atiende las consultas a la Base de Datos Maestra desde cualquier cajero o banco remoto en el puerto TCP 5000.
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

        // Registro en memoria de cajeros remotos que han hecho heartbeat o consulta contra esta BD Maestra
        private static readonly ConcurrentDictionary<string, CajeroRedInfo> _cajerosConectadosBdMaestra = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Inicia en segundo plano el respondedor UDP (puerto 5005) y el Servidor Maestro de BD TCP (puerto 5000)
        /// para que todos los demás cajeros y bancos de la red detecten este equipo y consulten la Base de Datos Maestra.
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
        /// Escanea la red activa (SSID Wi-Fi, IPv4, Subred, Gateway, VPN) en busca de todos los Cajeros (ATMs)
        /// y Bancos conectados a la misma red o registrados contra la Base de Datos Maestra alojada en este equipo.
        /// </summary>
        public static async Task<ResultadoEscaneoRed> EscanearRedYDetectarBancosAsync(IGestorArchivos dal, int timeoutMs = 950)
        {
            AsegurarNodoEnRedActivo(dal);

            var resultado = ObtenerInformacionRedActual();
            var bancosRegistrados = dal.ObtenerBancosRed();
            var cajerosRegistrados = dal.ObtenerCajerosRed();

            var bancoPropio = bancosRegistrados.FirstOrDefault(b => b.EsPropio) ?? new BancoRedInfo
            {
                CodigoBanco = "BANC01",
                NombreBanco = "NEXUSBANK (Nuestro Banco - BD Maestra)",
                EsPropio = true,
                Puerto = PuertoTcpInterbancario,
                LimiteDiario = 25000m
            };

            bancoPropio.IP = resultado.IpLocal;
            bancoPropio.EnLinea = true;

            // Calcular efectivo local en bóveda para el cajero local ATM-01
            decimal efectivoLocal = dal.ObtenerInventarioBilletes().Sum(x => (decimal)x.Denominacion * x.Cantidad);
            if (efectivoLocal <= 0) efectivoLocal = 10000m;

            var cajeroPropio = cajerosRegistrados.FirstOrDefault(c => c.EsLocal || c.CodigoCajero == "ATM-01") ?? new CajeroRedInfo
            {
                CodigoCajero = "ATM-01",
                NombreCajero = "Cajero Central Zona 1 (Maestro Local)",
                CodigoBanco = bancoPropio.CodigoBanco,
                EsLocal = true
            };
            cajeroPropio.IP = resultado.IpLocal;
            cajeroPropio.Puerto = PuertoTcpInterbancario;
            cajeroPropio.EsLocal = true;
            cajeroPropio.EnLinea = true;
            cajeroPropio.EfectivoActual = efectivoLocal;
            cajeroPropio.UltimaConexionBdMaestra = DateTime.Now;

            var bancosDescubiertos = new ConcurrentDictionary<string, BancoRedInfo>(StringComparer.OrdinalIgnoreCase);
            var cajerosDescubiertos = new ConcurrentDictionary<string, CajeroRedInfo>(StringComparer.OrdinalIgnoreCase);

            // Incluir cajeros que ya se hayan conectado por TCP a nuestra BD Maestra recientemente (últimos 5 minutos)
            foreach (var kv in _cajerosConectadosBdMaestra)
            {
                if ((DateTime.Now - kv.Value.UltimaConexionBdMaestra).TotalMinutes <= 5)
                {
                    kv.Value.EnLinea = true;
                    cajerosDescubiertos[kv.Key] = kv.Value;
                }
            }

            // 1. Descubrimiento rápido por UDP Broadcast (Puerto 5005) tanto de Bancos como de Cajeros
            await EnviarBroadcastUdpYRecibirAsync(bancoPropio, cajeroPropio, resultado.IpLocal, bancosDescubiertos, cajerosDescubiertos, 450);

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

            // 3. Incluir IPs configuradas manualmente en BancosRed y CajerosRed
            foreach (var b in bancosRegistrados.Where(x => !x.EsPropio))
            {
                if (!string.IsNullOrWhiteSpace(b.IP) && b.IP != "127.0.0.1" && !EsIpPropia(b.IP, resultado.IpLocal))
                    ipsCandidatas.Add(b.IP);
            }
            foreach (var c in cajerosRegistrados.Where(x => !x.EsLocal))
            {
                if (!string.IsNullOrWhiteSpace(c.IP) && c.IP != "127.0.0.1" && !EsIpPropia(c.IP, resultado.IpLocal))
                    ipsCandidatas.Add(c.IP);
            }

            // 4. Consultar IB_PING por TCP a cada IP candidata en puertos 5000 y 5001 (detecta bancos y cajeros remotos)
            var tareasPing = new List<Task>();
            foreach (var ip in ipsCandidatas)
            {
                foreach (int puerto in new[] { 5000, 5001 })
                {
                    tareasPing.Add(Task.Run(async () =>
                    {
                        var (bancoRemoto, cajeroRemoto) = await ConsultarNodoRemotoTcpAsync(ip, puerto, bancoPropio.CodigoBanco, bancoPropio.NombreBanco, timeoutMs);
                        if (bancoRemoto != null && !string.Equals(bancoRemoto.CodigoBanco, bancoPropio.CodigoBanco, StringComparison.OrdinalIgnoreCase))
                        {
                            bancoRemoto.EnLinea = true;
                            bancosDescubiertos[bancoRemoto.CodigoBanco] = bancoRemoto;
                        }
                        if (cajeroRemoto != null && !string.Equals(cajeroRemoto.CodigoCajero, cajeroPropio.CodigoCajero, StringComparison.OrdinalIgnoreCase))
                        {
                            cajeroRemoto.EnLinea = true;
                            cajerosDescubiertos[cajeroRemoto.CodigoCajero] = cajeroRemoto;
                        }
                    }));
                }
            }

            await Task.WhenAll(tareasPing);

            // 5. Sincronizar resultados con BancosRed
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

            // 6. Sincronizar resultados con CajerosRed
            if (!cajerosRegistrados.Any(x => x.EsLocal || x.CodigoCajero == "ATM-01"))
            {
                cajerosRegistrados.Insert(0, cajeroPropio);
            }

            foreach (var c in cajerosRegistrados)
            {
                if (c.EsLocal || c.CodigoCajero == "ATM-01")
                {
                    c.IP = resultado.IpLocal;
                    c.Puerto = PuertoTcpInterbancario;
                    c.EsLocal = true;
                    c.EnLinea = true;
                    c.EfectivoActual = efectivoLocal;
                    c.UltimaConexionBdMaestra = DateTime.Now;
                }
                else if (cajerosDescubiertos.TryGetValue(c.CodigoCajero, out var detCaj))
                {
                    c.IP = detCaj.IP;
                    c.Puerto = detCaj.Puerto;
                    c.CodigoBanco = string.IsNullOrWhiteSpace(detCaj.CodigoBanco) ? c.CodigoBanco : detCaj.CodigoBanco;
                    c.NombreCajero = string.IsNullOrWhiteSpace(detCaj.NombreCajero) ? c.NombreCajero : detCaj.NombreCajero;
                    c.EfectivoActual = detCaj.EfectivoActual > 0 ? detCaj.EfectivoActual : c.EfectivoActual;
                    c.EnLinea = true;
                    c.UltimaConexionBdMaestra = DateTime.Now;
                }
                else
                {
                    c.EnLinea = false;
                }
            }

            foreach (var kv in cajerosDescubiertos)
            {
                if (!cajerosRegistrados.Any(x => string.Equals(x.CodigoCajero, kv.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    cajerosRegistrados.Add(kv.Value);
                }
            }
            dal.GuardarCajerosRed(cajerosRegistrados);

            resultado.BancosDetectados = bancosRegistrados.Where(x => !x.EsPropio && x.EnLinea).ToList();
            resultado.CajerosDetectados = cajerosRegistrados.Where(x => x.EnLinea).ToList();
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

        private static async Task<(BancoRedInfo? banco, CajeroRedInfo? cajero)> ConsultarNodoRemotoTcpAsync(
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
                    return (null, null);

                using var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                stream.WriteTimeout = timeoutMs;

                var reqObj = new
                {
                    tipo = "IB_PING",
                    cajero = "ATM-01",
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
                    return (null, null);

                string? linea = await readTask;
                if (string.IsNullOrWhiteSpace(linea))
                    return (null, null);

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

                string codBanco = ObtenerStringProp(datosElem, "codigoBanco", "CodigoBanco");
                string nomBanco = ObtenerStringProp(datosElem, "nombreBanco", "NombreBanco");
                decimal disp = ObtenerDecimalProp(datosElem, 25000m, "cupoDisponibleHoy", "CupoDisponibleHoy", "cupoDisponibleBanco");

                string codCajero = ObtenerStringProp(datosElem, "codigoCajero", "CodigoCajero", "cajero");
                string nomCajero = ObtenerStringProp(datosElem, "nombreCajero", "NombreCajero");
                decimal efecCajero = ObtenerDecimalProp(datosElem, 10000m, "efectivoDisponible", "EfectivoActual");

                BancoRedInfo? banco = null;
                if (!string.IsNullOrWhiteSpace(codBanco))
                {
                    banco = new BancoRedInfo
                    {
                        CodigoBanco = codBanco,
                        NombreBanco = string.IsNullOrWhiteSpace(nomBanco) ? $"Banco {codBanco}" : nomBanco,
                        IP = ip,
                        Puerto = puerto,
                        EsPropio = false,
                        EnLinea = true,
                        LimiteDiario = Math.Max(25000m, disp),
                        TransferidoHoy = Math.Max(0m, 25000m - disp)
                    };
                }

                CajeroRedInfo? cajero = new CajeroRedInfo
                {
                    CodigoCajero = !string.IsNullOrWhiteSpace(codCajero) ? codCajero : $"ATM-{ip.Split('.').Last()}",
                    NombreCajero = !string.IsNullOrWhiteSpace(nomCajero) ? nomCajero : $"Cajero en Red ({ip})",
                    CodigoBanco = !string.IsNullOrWhiteSpace(codBanco) ? codBanco : "BANC01",
                    IP = ip,
                    Puerto = puerto,
                    EsLocal = false,
                    EnLinea = true,
                    EfectivoActual = efecCajero,
                    UltimaConexionBdMaestra = DateTime.Now
                };

                return (banco, cajero);
            }
            catch
            {
                return (null, null);
            }
        }

        private static async Task EnviarBroadcastUdpYRecibirAsync(
            BancoRedInfo bancoPropio,
            CajeroRedInfo cajeroPropio,
            string ipLocal,
            ConcurrentDictionary<string, BancoRedInfo> bancosDescubiertos,
            ConcurrentDictionary<string, CajeroRedInfo> cajerosDescubiertos,
            int waitMs)
        {
            try
            {
                using var udp = new UdpClient(0);
                udp.EnableBroadcast = true;

                string msg = $"DISCOVER_BANCO_ATM|{bancoPropio.CodigoBanco}|{bancoPropio.NombreBanco}|{ipLocal}|{bancoPropio.Puerto}|{cajeroPropio.CodigoCajero}";
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
                    string ipRemota = res.RemoteEndPoint.Address.ToString();

                    if (EsIpPropia(ipRemota, ipLocal)) continue;

                    // Respuesta de Banco: HERE_BANCO_ATM|CodigoBanco|NombreBanco|IP|Puerto|LimiteDiario|TransferidoHoy|CodigoCajero
                    if (texto.StartsWith("HERE_BANCO_ATM|", StringComparison.OrdinalIgnoreCase))
                    {
                        var partes = texto.Split('|');
                        if (partes.Length >= 5)
                        {
                            string cod = partes[1].Trim();
                            string nom = partes[2].Trim();
                            int.TryParse(partes[4].Trim(), out int puerto);
                            decimal lim = partes.Length >= 6 && decimal.TryParse(partes[5], out var l) ? l : 25000m;
                            decimal trf = partes.Length >= 7 && decimal.TryParse(partes[6], out var t) ? t : 0m;
                            string codCaj = partes.Length >= 8 ? partes[7].Trim() : $"ATM-{ipRemota.Split('.').Last()}";

                            if (!string.Equals(cod, bancoPropio.CodigoBanco, StringComparison.OrdinalIgnoreCase))
                            {
                                bancosDescubiertos[cod] = new BancoRedInfo
                                {
                                    CodigoBanco = cod,
                                    NombreBanco = nom,
                                    IP = ipRemota,
                                    Puerto = puerto > 0 ? puerto : PuertoTcpInterbancario,
                                    EsPropio = false,
                                    EnLinea = true,
                                    LimiteDiario = lim,
                                    TransferidoHoy = trf
                                };
                            }

                            cajerosDescubiertos[codCaj] = new CajeroRedInfo
                            {
                                CodigoCajero = codCaj,
                                NombreCajero = $"Cajero Remoto ({nom})",
                                CodigoBanco = cod,
                                IP = ipRemota,
                                Puerto = puerto > 0 ? puerto : PuertoTcpInterbancario,
                                EsLocal = false,
                                EnLinea = true,
                                EfectivoActual = 10000m,
                                UltimaConexionBdMaestra = DateTime.Now
                            };
                        }
                    }
                    // Respuesta de Cajero: HERE_CAJERO_ATM|CodigoCajero|NombreCajero|CodigoBanco|IP|Puerto|Efectivo
                    else if (texto.StartsWith("HERE_CAJERO_ATM|", StringComparison.OrdinalIgnoreCase))
                    {
                        var partes = texto.Split('|');
                        if (partes.Length >= 4)
                        {
                            string codCaj = partes[1].Trim();
                            string nomCaj = partes[2].Trim();
                            string codBan = partes[3].Trim();
                            int puerto = partes.Length >= 6 && int.TryParse(partes[5], out var p) ? p : PuertoTcpInterbancario;
                            decimal efec = partes.Length >= 7 && decimal.TryParse(partes[6], out var ef) ? ef : 10000m;

                            cajerosDescubiertos[codCaj] = new CajeroRedInfo
                            {
                                CodigoCajero = codCaj,
                                NombreCajero = nomCaj,
                                CodigoBanco = codBan,
                                IP = ipRemota,
                                Puerto = puerto,
                                EsLocal = false,
                                EnLinea = true,
                                EfectivoActual = efec,
                                UltimaConexionBdMaestra = DateTime.Now
                            };
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
                    string ipRemota = recibido.RemoteEndPoint.Address.ToString();

                    var infoRed = ObtenerInformacionRedActual();
                    var propio = dal.ObtenerBancosRed().FirstOrDefault(b => b.EsPropio);

                    if (msg.StartsWith("DISCOVER_BANCO_ATM|", StringComparison.OrdinalIgnoreCase) ||
                        msg.StartsWith("DISCOVER_CAJERO_ATM|", StringComparison.OrdinalIgnoreCase))
                    {
                        // Si el mensaje trae datos de un cajero remoto, registrarlo automáticamente en la BD Maestra
                        var p = msg.Split('|');
                        if (p.Length >= 3 && !EsIpPropia(ipRemota, infoRed.IpLocal))
                        {
                            string codCajRem = p.Length >= 6 ? p[5].Trim() : p[1].Trim();
                            string nomRem = p[2].Trim();
                            RegistrarLatidoCajeroRemoto(dal, codCajRem, nomRem, p[1].Trim(), ipRemota, PuertoTcpInterbancario, 10000m);
                        }

                        if (propio != null)
                        {
                            string respBanco = $"HERE_BANCO_ATM|{propio.CodigoBanco}|{propio.NombreBanco}|{infoRed.IpLocal}|{propio.Puerto}|{propio.LimiteDiario}|{propio.TransferidoHoy}|ATM-01";
                            byte[] bytes1 = Encoding.UTF8.GetBytes(respBanco);
                            await _udpListener.SendAsync(bytes1, bytes1.Length, recibido.RemoteEndPoint);

                            decimal efec = dal.ObtenerInventarioBilletes().Sum(x => (decimal)x.Denominacion * x.Cantidad);
                            string respCajero = $"HERE_CAJERO_ATM|ATM-01|Cajero Central BD Maestra ({infoRed.IpLocal})|{propio.CodigoBanco}|{infoRed.IpLocal}|{propio.Puerto}|{efec:F2}";
                            byte[] bytes2 = Encoding.UTF8.GetBytes(respCajero);
                            await _udpListener.SendAsync(bytes2, bytes2.Length, recibido.RemoteEndPoint);
                        }
                    }
                }
            }
            catch { }
        }

        private static void RegistrarLatidoCajeroRemoto(
            IGestorArchivos dal,
            string codigoCajero,
            string nombreCajero,
            string codigoBanco,
            string ip,
            int puerto,
            decimal efectivo)
        {
            if (string.IsNullOrWhiteSpace(codigoCajero) || codigoCajero == "SERVER")
                codigoCajero = $"ATM-{ip.Split('.').Last()}";

            var info = new CajeroRedInfo
            {
                CodigoCajero = codigoCajero,
                NombreCajero = string.IsNullOrWhiteSpace(nombreCajero) ? $"Cajero en Red ({ip})" : nombreCajero,
                CodigoBanco = string.IsNullOrWhiteSpace(codigoBanco) ? "BANC01" : codigoBanco,
                IP = ip,
                Puerto = puerto > 0 ? puerto : PuertoTcpInterbancario,
                EsLocal = false,
                EnLinea = true,
                EfectivoActual = efectivo > 0 ? efectivo : 10000m,
                UltimaConexionBdMaestra = DateTime.Now
            };

            _cajerosConectadosBdMaestra[codigoCajero] = info;

            var cajeros = dal.ObtenerCajerosRed();
            var existente = cajeros.FirstOrDefault(c => string.Equals(c.CodigoCajero, codigoCajero, StringComparison.OrdinalIgnoreCase));
            if (existente != null)
            {
                existente.IP = ip;
                existente.EnLinea = true;
                existente.UltimaConexionBdMaestra = DateTime.Now;
                if (efectivo > 0) existente.EfectivoActual = efectivo;
            }
            else
            {
                cajeros.Add(info);
            }
            dal.GuardarCajerosRed(cajeros);
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
                    _ = Task.Run(() => AtenderClienteTcpBdMaestraAsync(client, dal), token);
                }
            }
            catch { }
        }

        /// <summary>
        /// Atiende las conexiones entrantes al Puerto TCP 5000 de este equipo (Servidor de Base de Datos Maestra).
        /// Soporta tanto Cajeros Remotos (CONECTAR, HEARTBEAT, CONSULTAR_SALDO, RETIRAR, DEPOSITAR, VERIFICAR_CUENTA, TRANSFERIR)
        /// como Bancos Remotos (IB_PING, IB_VERIFICAR_CUENTA, IB_TRANSFERIR, DB_QUERY_*).
        /// </summary>
        private static async Task AtenderClienteTcpBdMaestraAsync(TcpClient client, IGestorArchivos dal)
        {
            using (client)
            {
                try
                {
                    string ipCliente = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    // Soportar múltiples peticiones en la misma conexión TCP (para Cajeros persistentes) o una sola petición por conexión
                    string? linea;
                    while ((linea = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        using var doc = JsonDocument.Parse(linea);
                        var root = doc.RootElement;
                        string idMsg = ObtenerStringProp(root, "idMensaje", "IdMensaje");
                        string tipo = ObtenerStringProp(root, "tipo", "Tipo");
                        string cajeroId = ObtenerStringProp(root, "cajero", "CajeroId", "codigoCajero");
                        string bancoOrig = ObtenerStringProp(root, "bancoOrigen", "BancoOrigen", "codigoBanco");
                        string nombreCaj = ObtenerStringProp(root, "nombreCajero", "Nombre", "ubicacion");
                        decimal efecCaj = ObtenerDecimalProp(root, 10000m, "efectivoTotal", "efectivoDisponible");

                        // Registrar el cajero remoto como activo en la BD Maestra alojada en este equipo
                        var infoRed = ObtenerInformacionRedActual();
                        if (!EsIpPropia(ipCliente, infoRed.IpLocal))
                        {
                            RegistrarLatidoCajeroRemoto(dal, cajeroId, nombreCaj, bancoOrig, ipCliente, PuertoTcpInterbancario, efecCaj);
                        }

                        var bancos = dal.ObtenerBancosRed();
                        var propio = bancos.FirstOrDefault(b => b.EsPropio) ?? new BancoRedInfo
                        {
                            CodigoBanco = "BANC01",
                            NombreBanco = "NEXUSBANK (BD Maestra)",
                            EsPropio = true,
                            LimiteDiario = 25000m
                        };

                        if (string.Equals(tipo, "IB_PING", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(tipo, "CONECTAR", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(tipo, "HEARTBEAT", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(tipo, "ATM_PING", StringComparison.OrdinalIgnoreCase))
                        {
                            decimal efecLocal = dal.ObtenerInventarioBilletes().Sum(x => (decimal)x.Denominacion * x.Cantidad);
                            var resp = new
                            {
                                tipo = "RESP_OK",
                                idMensaje = idMsg,
                                codigo = "00",
                                mensaje = $"Conectado a BD Maestra en {infoRed.IpLocal}:5000 ({propio.CodigoBanco} - {propio.NombreBanco})",
                                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                datos = new
                                {
                                    codigoBanco = propio.CodigoBanco,
                                    nombreBanco = propio.NombreBanco,
                                    codigoCajero = "ATM-01",
                                    nombreCajero = "Cajero Central BD Maestra",
                                    bdMaestraIp = infoRed.IpLocal,
                                    bdMaestraSql = @"localhost\SQLEXPRESS (BD_CajeroRed)",
                                    efectivoDisponible = efecLocal,
                                    cupoDisponibleHoy = propio.DisponibleHoy,
                                    estado = "EN_LINEA"
                                }
                            };
                            await writer.WriteLineAsync(JsonSerializer.Serialize(resp));
                        }
                        else if (string.Equals(tipo, "CONSULTAR_SALDO", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(tipo, "VALIDAR_TARJETA", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(tipo, "AUTENTICAR_PIN", StringComparison.OrdinalIgnoreCase))
                        {
                            string tarj = ObtenerStringProp(root, "numeroTarjeta", "NumeroTarjeta", "tarjeta", "cuenta");
                            string pin = ObtenerStringProp(root, "pin", "PIN", "pinHash");
                            var u = dal.ObtenerUsuarioPorTarjeta(tarj);

                            if (u == null)
                            {
                                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                                {
                                    tipo = "RESP_OK",
                                    idMensaje = idMsg,
                                    codigo = "14",
                                    mensaje = "Tarjeta o cuenta no encontrada en la BD Maestra."
                                }));
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(pin) && u.PIN != pin)
                            {
                                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                                {
                                    tipo = "RESP_OK",
                                    idMensaje = idMsg,
                                    codigo = "55",
                                    mensaje = "PIN incorrecto en la BD Maestra."
                                }));
                                continue;
                            }

                            await writer.WriteLineAsync(JsonSerializer.Serialize(new
                            {
                                tipo = "RESP_OK",
                                idMensaje = idMsg,
                                codigo = "00",
                                mensaje = "Consulta exitosa en BD Maestra.",
                                datos = new
                                {
                                    numeroTarjeta = u.NumeroTarjeta,
                                    nombre = u.Nombre,
                                    saldoActual = u.SaldoActual,
                                    limiteDiario = u.LimiteDiario,
                                    estado = "Activo"
                                }
                            }));
                        }
                        else if (string.Equals(tipo, "IB_VERIFICAR_CUENTA", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(tipo, "VERIFICAR_CUENTA", StringComparison.OrdinalIgnoreCase))
                        {
                            string cuenta = ObtenerStringProp(root, "numeroCuenta", "cuentaDestino", "CuentaDestino");
                            string codBancoDest = ObtenerStringProp(root, "bancoDestino", "BancoDestino", "codigoBancoDestino");

                            // Buscar primero en usuarios locales de la BD Maestra y luego en CuentasExternas de la BD Maestra
                            var usuario = dal.ObtenerUsuarioPorTarjeta(cuenta);
                            if (usuario != null && (string.IsNullOrWhiteSpace(codBancoDest) || string.Equals(codBancoDest, propio.CodigoBanco, StringComparison.OrdinalIgnoreCase)))
                            {
                                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                                {
                                    tipo = "RESP_OK",
                                    idMensaje = idMsg,
                                    codigo = "00",
                                    mensaje = "Cuenta verificada en la BD Maestra.",
                                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                    datos = new
                                    {
                                        existe = true,
                                        codigoBanco = propio.CodigoBanco,
                                        nombreBanco = propio.NombreBanco,
                                        numeroCuenta = usuario.NumeroTarjeta,
                                        nombreTitular = usuario.Nombre,
                                        cupoDisponibleBanco = propio.DisponibleHoy,
                                        cupoDisponibleBancoOrigen = propio.DisponibleHoy
                                    }
                                }));
                            }
                            else
                            {
                                var ctaExt = dal.ObtenerCuentasExternas(string.IsNullOrWhiteSpace(codBancoDest) ? null : codBancoDest)
                                    .FirstOrDefault(c => c.Activa && string.Equals(c.NumeroCuenta, cuenta, StringComparison.OrdinalIgnoreCase));

                                var bDest = bancos.FirstOrDefault(b => string.Equals(b.CodigoBanco, ctaExt?.CodigoBanco ?? codBancoDest, StringComparison.OrdinalIgnoreCase));

                                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                                {
                                    tipo = "RESP_OK",
                                    idMensaje = idMsg,
                                    codigo = ctaExt != null ? "00" : "14",
                                    mensaje = ctaExt != null ? "Cuenta verificada en BD Maestra." : "La cuenta no existe en el banco seleccionado.",
                                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                    datos = new
                                    {
                                        existe = ctaExt != null,
                                        codigoBanco = ctaExt?.CodigoBanco ?? codBancoDest,
                                        nombreBanco = bDest?.NombreBanco ?? $"Banco {codBancoDest}",
                                        numeroCuenta = ctaExt?.NumeroCuenta ?? cuenta,
                                        nombreTitular = ctaExt?.NombreTitular ?? "",
                                        cupoDisponibleBanco = bDest?.DisponibleHoy ?? 25000m,
                                        cupoDisponibleBancoOrigen = propio.DisponibleHoy
                                    }
                                }));
                            }
                        }
                        else if (string.Equals(tipo, "IB_TRANSFERIR", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(tipo, "TRANSFERIR", StringComparison.OrdinalIgnoreCase))
                        {
                            string tokenTrf = ObtenerStringProp(root, "tokenTransferencia", "TokenTransferencia");
                            string bancoOrigen = ObtenerStringProp(root, "bancoOrigen", "BancoOrigen");
                            if (string.IsNullOrWhiteSpace(bancoOrigen)) bancoOrigen = propio.CodigoBanco;

                            string bancoDestino = ObtenerStringProp(root, "bancoDestino", "BancoDestino", "codigoBancoDestino");
                            if (string.IsNullOrWhiteSpace(bancoDestino)) bancoDestino = propio.CodigoBanco;

                            string cuentaOrigen = ObtenerStringProp(root, "cuentaOrigen", "CuentaOrigen", "numeroTarjetaOrigen");
                            string nombreOrigen = ObtenerStringProp(root, "nombreOrigen", "NombreOrigen");
                            string cuentaDestino = ObtenerStringProp(root, "cuentaDestino", "CuentaDestino");
                            string concepto = ObtenerStringProp(root, "concepto", "Concepto");
                            decimal monto = ObtenerDecimalProp(root, 0m, "monto", "Monto");

                            if (string.IsNullOrWhiteSpace(tokenTrf))
                                tokenTrf = $"TRF-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";

                            var bOrig = bancos.FirstOrDefault(b => string.Equals(b.CodigoBanco, bancoOrigen, StringComparison.OrdinalIgnoreCase)) ?? propio;
                            var bDest = bancos.FirstOrDefault(b => string.Equals(b.CodigoBanco, bancoDestino, StringComparison.OrdinalIgnoreCase)) ?? propio;

                            if (monto <= 0 || monto > bOrig.DisponibleHoy || monto > bDest.DisponibleHoy)
                            {
                                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                                {
                                    tipo = "RESP_OK",
                                    idMensaje = idMsg,
                                    codigo = "65",
                                    mensaje = $"El monto excede el límite diario disponible de los bancos en la BD Maestra.",
                                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                                }));
                                continue;
                            }

                            var usrOrig = dal.ObtenerUsuarioPorTarjeta(cuentaOrigen);
                            if (usrOrig != null)
                            {
                                if (usrOrig.SaldoActual < monto)
                                {
                                    await writer.WriteLineAsync(JsonSerializer.Serialize(new
                                    {
                                        tipo = "RESP_OK",
                                        idMensaje = idMsg,
                                        codigo = "51",
                                        mensaje = "Fondos insuficientes en la cuenta origen en BD Maestra."
                                    }));
                                    continue;
                                }
                                usrOrig.SaldoActual -= monto;
                                dal.ActualizarUsuario(usrOrig);
                                nombreOrigen = usrOrig.Nombre;
                                dal.RegistrarTransaccion(new Transaccion(usrOrig.NumeroTarjeta, DateTime.Now, $"Transf. a {bancoDestino} ({cuentaDestino})", monto, tokenTrf));
                            }

                            string nombreDestinoFinal = ObtenerStringProp(root, "nombreDestino", "NombreDestino");
                            var usrDest = dal.ObtenerUsuarioPorTarjeta(cuentaDestino);
                            if (usrDest != null)
                            {
                                usrDest.SaldoActual += monto;
                                dal.ActualizarUsuario(usrDest);
                                nombreDestinoFinal = usrDest.Nombre;
                                dal.RegistrarTransaccion(new Transaccion(usrDest.NumeroTarjeta, DateTime.Now, $"Transf. de {bancoOrigen} ({cuentaOrigen})", monto, tokenTrf));
                            }
                            else
                            {
                                var ctaExt = dal.ObtenerCuentasExternas(bancoDestino)
                                    .FirstOrDefault(c => string.Equals(c.NumeroCuenta, cuentaDestino, StringComparison.OrdinalIgnoreCase));
                                if (ctaExt != null)
                                {
                                    ctaExt.SaldoActual += monto;
                                    dal.GuardarCuentaExterna(ctaExt);
                                    nombreDestinoFinal = ctaExt.NombreTitular;
                                }
                            }

                            // Restar monto al límite de los dos bancos coordinadamente en la BD Maestra
                            if (!string.Equals(bOrig.CodigoBanco, bDest.CodigoBanco, StringComparison.OrdinalIgnoreCase))
                            {
                                bOrig.TransferidoHoy += monto;
                                bDest.TransferidoHoy += monto;
                                dal.GuardarBancosRed(bancos);
                            }

                            dal.RegistrarTransferenciaCompartida(new RegistroTransferenciaBancaria
                            {
                                TokenTransferencia = tokenTrf,
                                FechaHora = DateTime.Now,
                                TipoTransferencia = string.Equals(bOrig.CodigoBanco, bDest.CodigoBanco, StringComparison.OrdinalIgnoreCase) ? "INTERNA" : "INTERBANCARIA",
                                BancoOrigen = bOrig.CodigoBanco,
                                CuentaOrigen = cuentaOrigen,
                                NombreOrigen = string.IsNullOrWhiteSpace(nombreOrigen) ? "Cliente Remito" : nombreOrigen,
                                BancoDestino = bDest.CodigoBanco,
                                CuentaDestino = cuentaDestino,
                                NombreDestino = string.IsNullOrWhiteSpace(nombreDestinoFinal) ? "Beneficiario" : nombreDestinoFinal,
                                Monto = monto,
                                Concepto = concepto,
                                CupoRestanteBancoOrigen = bOrig.DisponibleHoy,
                                CupoRestanteBancoDestino = bDest.DisponibleHoy,
                                Estado = "APROBADA"
                            });

                            await writer.WriteLineAsync(JsonSerializer.Serialize(new
                            {
                                tipo = "RESP_OK",
                                idMensaje = idMsg,
                                codigo = "00",
                                mensaje = "Transferencia procesada en BD Maestra y Token registrado en ambos bancos y usuario.",
                                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                datos = new
                                {
                                    tokenTransferencia = tokenTrf,
                                    bancoOrigen = bOrig.CodigoBanco,
                                    bancoDestino = bDest.CodigoBanco,
                                    cuentaDestino = cuentaDestino,
                                    nombreDestino = nombreDestinoFinal,
                                    monto = monto,
                                    nuevoSaldo = usrOrig?.SaldoActual ?? 0m,
                                    cupoRestanteBancoOrigen = bOrig.DisponibleHoy,
                                    cupoRestanteBancoDestino = bDest.DisponibleHoy
                                }
                            }));
                        }
                        else if (string.Equals(tipo, "DB_QUERY_ESTADO", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync(JsonSerializer.Serialize(new
                            {
                                tipo = "RESP_OK",
                                idMensaje = idMsg,
                                codigo = "00",
                                mensaje = "Catálogo completo desde la BD Maestra.",
                                datos = new
                                {
                                    bancos = dal.ObtenerBancosRed(),
                                    cajeros = dal.ObtenerCajerosRed(),
                                    transferencias = dal.ObtenerTransferenciasCompartidas().Take(25)
                                }
                            }));
                        }
                    }
                }
                catch { }
            }
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
