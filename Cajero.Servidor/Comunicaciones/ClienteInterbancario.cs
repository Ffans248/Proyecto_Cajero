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
using System.Threading;
using System.Threading.Tasks;
using Cajero.Protocolo;
using Cajero.Servidor.Modelos;
using Microsoft.Data.SqlClient;

namespace Cajero.Servidor.Comunicaciones
{
    public class DiagnosticoRedServidor
    {
        public string NombreRedSsid { get; set; } = "Red Local";
        public string TipoInterfaz { get; set; } = "Wi-Fi / Ethernet";
        public string IpLocal { get; set; } = "127.0.0.1";
        public string SubredCidr { get; set; } = "127.0.0.0/24";
        public string Gateway { get; set; } = "N/A";
        public string OtrasInterfaces { get; set; } = "Ninguna";
        public List<(string codigoBanco, string nombreBanco, string ip, int puerto, decimal cupoDisp)> BancosDetectados { get; set; } = new();
    }

    /// <summary>
    /// Cliente de comunicación interbancaria (Banco <-> Banco).
    /// Permite verificar cuentas y ejecutar transferencias con otros bancos conectados por router:
    /// 1) Mediante TCP JSON en línea (IP:Puerto del otro servidor bancario).
    /// 2) Opcionalmente mediante conexión SQL directa a la tabla común si se configuró CadenaConexionBD.
    /// </summary>
    public static class ClienteInterbancario
    {
        public static DiagnosticoRedServidor ObtenerInfoRedLocal()
        {
            var diag = new DiagnosticoRedServidor();
            try
            {
                string ssid = "";
                try
                {
                    var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        string outStr = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(1200);
                        foreach (var l in outStr.Split('\n'))
                        {
                            string line = l.Trim();
                            if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                            {
                                int idx = line.IndexOf(':');
                                if (idx >= 0) { ssid = line[(idx + 1)..].Trim(); break; }
                            }
                        }
                    }
                }
                catch { }

                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                var principal = interfaces
                    .FirstOrDefault(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                    ?? interfaces.FirstOrDefault(ni => ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !ni.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase)))
                    ?? interfaces.FirstOrDefault();

                var otras = new List<string>();
                foreach (var ni in interfaces)
                {
                    var ipv4 = ni.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork && !u.Address.ToString().StartsWith("169.254."));
                    if (ipv4 == null) continue;

                    if (principal != null && ni.Id == principal.Id)
                    {
                        diag.IpLocal = ipv4.Address.ToString();
                        diag.TipoInterfaz = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : ni.Name;
                        diag.NombreRedSsid = !string.IsNullOrEmpty(ssid) ? ssid : ni.Name;
                        var gw = ni.GetIPProperties().GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                        diag.Gateway = gw?.Address.ToString() ?? "N/A";
                        var parts = diag.IpLocal.Split('.');
                        diag.SubredCidr = parts.Length == 4 ? $"{parts[0]}.{parts[1]}.{parts[2]}.0/24" : $"{diag.IpLocal}/24";
                    }
                    else
                    {
                        otras.Add($"{ni.Name}: {ipv4.Address}");
                    }
                }
                diag.OtrasInterfaces = otras.Count > 0 ? string.Join(" | ", otras) : "Ninguna";
            }
            catch { }
            return diag;
        }

        public static async Task<DiagnosticoRedServidor> EscanearSubredBancosAsync(string codigoBancoPropio, int puertoEscaneo = 5000)
        {
            var diag = ObtenerInfoRedLocal();
            var parts = diag.IpLocal.Split('.');
            if (parts.Length != 4) return diag;

            string prefijo = $"{parts[0]}.{parts[1]}.{parts[2]}";
            var detectados = new ConcurrentBag<(string codigoBanco, string nombreBanco, string ip, int puerto, decimal cupoDisp)>();

            var tareas = Enumerable.Range(1, 254).Select(async i =>
            {
                string ip = $"{prefijo}.{i}";
                if (ip == diag.IpLocal) return;

                try
                {
                    using var tcp = new TcpClient();
                    var ct = tcp.ConnectAsync(ip, puertoEscaneo);
                    if (await Task.WhenAny(ct, Task.Delay(170)) != ct || !tcp.Connected)
                        return;

                    var req = new SolicitudIbPing { BancoOrigen = codigoBancoPropio };
                    var resp = await EnviarMensajeTcpBancoAsync(ip, puertoEscaneo, req, 750);
                    if (resp != null && resp.Codigo == CodigosRespuesta.Aprobado && resp.Datos is JsonElement je)
                    {
                        string cod = je.TryGetProperty("codigoBanco", out var p1) ? p1.GetString() ?? "" :
                                     (je.TryGetProperty("CodigoBanco", out var p2) ? p2.GetString() ?? "" : "");
                        string nom = je.TryGetProperty("nombreBanco", out var n1) ? n1.GetString() ?? "" :
                                     (je.TryGetProperty("NombreBanco", out var n2) ? n2.GetString() ?? "" : "");
                        decimal cupo = je.TryGetProperty("cupoDisponibleHoy", out var c1) ? c1.GetDecimal() :
                                       (je.TryGetProperty("CupoDisponibleHoy", out var c2) ? c2.GetDecimal() : 25000m);

                        if (!string.IsNullOrWhiteSpace(cod) && !string.Equals(cod, codigoBancoPropio, StringComparison.OrdinalIgnoreCase))
                        {
                            detectados.Add((cod, string.IsNullOrWhiteSpace(nom) ? $"Banco {cod}" : nom, ip, puertoEscaneo, cupo));
                        }
                    }
                }
                catch { }
            });

            await Task.WhenAll(tareas);
            diag.BancosDetectados = detectados.ToList();
            return diag;
        }
        public static async Task<MensajeRespuesta?> EnviarMensajeTcpBancoAsync(string ip, int puerto, MensajeBase solicitud, int timeoutMs = 4500)
        {
            try
            {
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(ip, puerto);
                var delayTask = Task.Delay(timeoutMs);

                if (await Task.WhenAny(connectTask, delayTask) != connectTask || !tcp.Connected)
                {
                    return null;
                }

                using NetworkStream stream = tcp.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                string json = ProtocoloHelper.Serializar(solicitud);
                await writer.WriteAsync(json);

                using var cts = new CancellationTokenSource(timeoutMs);
                string? linea = await reader.ReadLineAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(linea)) return null;

                return ProtocoloHelper.Deserializar<MensajeRespuesta>(linea);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Verifica si existe una cuenta en un banco externo (primero intenta TCP al servidor del otro banco,
        /// luego SQL directo si tiene CadenaConexionBD). Devuelve null si el banco remoto no está alcanzable en red.
        /// </summary>
        public static async Task<(bool alcanzable, bool existe, string numeroCuenta, string nombreTitular, decimal? cupoRemoto, string mensaje)> VerificarCuentaRemotaAsync(
            BancoRedDto bancoDestino, string codigoBancoOrigen, string cuentaDestino)
        {
            // 1. Intentar por TCP (Router -> IP:Puerto del servidor del otro estudiante)
            var req = new SolicitudIbVerificarCuenta
            {
                BancoOrigen = codigoBancoOrigen,
                BancoDestino = bancoDestino.CodigoBanco,
                NumeroCuenta = cuentaDestino.Trim()
            };

            var resp = await EnviarMensajeTcpBancoAsync(bancoDestino.IP, bancoDestino.Puerto, req, 3500);
            if (resp != null)
            {
                if (resp.Codigo == CodigosRespuesta.Aprobado && resp.Datos is JsonElement je)
                {
                    bool existe = je.TryGetProperty("existe", out var pEx) && pEx.GetBoolean();
                    string numCta = je.TryGetProperty("numeroCuenta", out var pCta) ? (pCta.GetString() ?? cuentaDestino) : cuentaDestino;
                    string titular = je.TryGetProperty("nombreTitular", out var pNom) ? (pNom.GetString() ?? "") : "";
                    decimal? cupo = je.TryGetProperty("cupoDisponibleBanco", out var pCupo) ? pCupo.GetDecimal() : null;

                    return (true, existe, numCta, titular, cupo, resp.Mensaje);
                }

                return (true, false, cuentaDestino, "", null, resp.Mensaje);
            }

            // 2. Intentar por SQL directo si el banco tiene CadenaConexionBD configurada
            if (!string.IsNullOrWhiteSpace(bancoDestino.CadenaConexionBD))
            {
                try
                {
                    using var conn = new SqlConnection(bancoDestino.CadenaConexionBD);
                    await conn.OpenAsync();

                    string sql = @"
                        SELECT TOP 1 
                            u.Nombre,
                            ISNULL(u.NumeroCuenta, ISNULL(t.NumeroTarjeta, @Cta)) AS Cuenta
                        FROM dbo.Usuario u
                        LEFT JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario AND t.Activa = 1
                        WHERE u.Activo = 1 AND u.Estado = 'Activo'
                          AND (u.NumeroCuenta = @Cta OR t.NumeroTarjeta = @Cta)";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Cta", cuentaDestino.Trim());
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        string nom = reader["Nombre"].ToString()!;
                        string cta = reader["Cuenta"].ToString()!;
                        return (true, true, cta, nom, null, "Cuenta verificada en BD remota.");
                    }

                    return (true, false, cuentaDestino, "", null, "La cuenta no existe en la BD del banco seleccionado.");
                }
                catch
                {
                    // Si falla SQL remoto, continuamos al fallback local en RepositorioServidor
                }
            }

            return (false, false, cuentaDestino, "", null, "Servidor remoto no alcanzable por red.");
        }

        /// <summary>
        /// Envía la orden de transferencia interbancaria al otro banco con el TokenTransferencia generado
        /// para que el banco destino acredite al usuario receptor, descuente su límite diario de banco y guarde el token.
        /// </summary>
        public static async Task<(bool alcanzable, bool aprobada, string codigo, string mensaje, string nombreDestino, decimal? cupoRestanteDestino)> EnviarTransferenciaRemotaAsync(
            BancoRedDto bancoDestino, SolicitudIbTransferir solicitud)
        {
            // 1. Intentar por protocolo TCP Banco <-> Banco
            var resp = await EnviarMensajeTcpBancoAsync(bancoDestino.IP, bancoDestino.Puerto, solicitud, 5000);
            if (resp != null)
            {
                bool ok = resp.Codigo == CodigosRespuesta.Aprobado;
                string nomDest = solicitud.NombreDestino;
                decimal? cupoDest = null;

                if (resp.Datos is JsonElement je)
                {
                    if (je.TryGetProperty("nombreDestino", out var pNom) && !string.IsNullOrWhiteSpace(pNom.GetString()))
                        nomDest = pNom.GetString()!;
                    if (je.TryGetProperty("cupoRestanteBancoDestino", out var pCupo))
                        cupoDest = pCupo.GetDecimal();
                }

                return (true, ok, resp.Codigo, resp.Mensaje, nomDest, cupoDest);
            }

            // 2. Intentar por SQL directo a la tabla común TransferenciaBancaria del otro banco si se configuró CadenaConexionBD
            if (!string.IsNullOrWhiteSpace(bancoDestino.CadenaConexionBD))
            {
                try
                {
                    using var conn = new SqlConnection(bancoDestino.CadenaConexionBD);
                    await conn.OpenAsync();
                    using var tx = conn.BeginTransaction();

                    // Verificar y restar del cupo en el banco destino
                    string sqlCupo = @"
                        UPDATE dbo.BancoRed
                        SET TransferidoHoy = CASE WHEN FechaUltimoReset < CAST(GETDATE() AS DATE) THEN 0 ELSE TransferidoHoy END,
                            FechaUltimoReset = CAST(GETDATE() AS DATE)
                        WHERE EsPropio = 1 OR CodigoBanco = @BancoDestino;

                        SELECT TOP 1 LimiteDiario, TransferidoHoy 
                        FROM dbo.BancoRed WITH (UPDLOCK)
                        WHERE EsPropio = 1 OR CodigoBanco = @BancoDestino;";

                    decimal limRemoto = 50000m, transfRemoto = 0m;
                    using (var cmdC = new SqlCommand(sqlCupo, conn, tx))
                    {
                        cmdC.Parameters.AddWithValue("@BancoDestino", solicitud.BancoDestino);
                        using var r = await cmdC.ExecuteReaderAsync();
                        if (await r.ReadAsync())
                        {
                            limRemoto = Convert.ToDecimal(r["LimiteDiario"]);
                            transfRemoto = Convert.ToDecimal(r["TransferidoHoy"]);
                        }
                    }

                    if (transfRemoto + solicitud.Monto > limRemoto)
                    {
                        tx.Rollback();
                        return (true, false, CodigosRespuesta.ExcedeCupoInterbancario,
                            $"El banco destino ({solicitud.BancoDestino}) no tiene cupo diario suficiente.", solicitud.NombreDestino, limRemoto - transfRemoto);
                    }

                    // Acreditar al usuario en el banco destino
                    string sqlAcred = @"
                        DECLARE @IdUsr INT, @IdTar INT, @Nom VARCHAR(100);
                        SELECT TOP 1 @IdUsr = u.IdUsuario, @IdTar = t.IdTarjeta, @Nom = u.Nombre
                        FROM dbo.Usuario u WITH (UPDLOCK)
                        LEFT JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario AND t.Activa = 1
                        WHERE u.Activo = 1 AND u.Estado = 'Activo'
                          AND (u.NumeroCuenta = @Cta OR t.NumeroTarjeta = @Cta);

                        IF @IdUsr IS NOT NULL
                        BEGIN
                            UPDATE dbo.Usuario SET Saldo = Saldo + @Monto WHERE IdUsuario = @IdUsr;

                            UPDATE dbo.BancoRed 
                            SET TransferidoHoy = TransferidoHoy + @Monto 
                            WHERE EsPropio = 1 OR CodigoBanco = @BancoDestino;

                            INSERT INTO dbo.TransferenciaBancaria
                                (TokenTransferencia, TipoTransferencia, Direccion, BancoOrigen, CuentaOrigen, NombreOrigen,
                                 BancoDestino, CuentaDestino, NombreDestino, Monto, Moneda, Concepto, Estado, CodigoRespuesta,
                                 MensajeRespuesta, CupoRestanteBancoDestino, IdUsuarioLocal, FechaCreacion, FechaConfirmacion)
                            VALUES
                                (@Token, 'INTERBANCARIA', 'ENTRANTE', @BancoOrigen, @CuentaOrigen, @NombreOrigen,
                                 @BancoDestino, @Cta, @Nom, @Monto, 'GTQ', @Concepto, 'COMPLETADA', '00',
                                 'Transferencia interbancaria recibida', (@CupoRest), @IdUsr, GETDATE(), GETDATE());

                            INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, TokenTransferencia, Fecha)
                            VALUES (@IdUsr, @IdTar, NULL, 'TRANSFERENCIA_RECIBIDA', @Monto, '00', CONCAT('De ', @BancoOrigen, ' Token: ', @Token), @Token, GETDATE());
                        END
                        SELECT ISNULL(@Nom, '') AS NombreEncontrado;";

                    decimal cupoRest = limRemoto - (transfRemoto + solicitud.Monto);
                    using var cmdA = new SqlCommand(sqlAcred, conn, tx);
                    cmdA.Parameters.AddWithValue("@Cta", solicitud.CuentaDestino);
                    cmdA.Parameters.AddWithValue("@Monto", solicitud.Monto);
                    cmdA.Parameters.AddWithValue("@BancoDestino", solicitud.BancoDestino);
                    cmdA.Parameters.AddWithValue("@BancoOrigen", solicitud.BancoOrigen);
                    cmdA.Parameters.AddWithValue("@CuentaOrigen", solicitud.CuentaOrigen);
                    cmdA.Parameters.AddWithValue("@NombreOrigen", solicitud.NombreOrigen);
                    cmdA.Parameters.AddWithValue("@Token", solicitud.TokenTransferencia);
                    cmdA.Parameters.AddWithValue("@Concepto", solicitud.Concepto ?? "");
                    cmdA.Parameters.AddWithValue("@CupoRest", cupoRest);

                    string nomEncontrado = (await cmdA.ExecuteScalarAsync())?.ToString() ?? "";
                    if (string.IsNullOrEmpty(nomEncontrado))
                    {
                        tx.Rollback();
                        return (true, false, CodigosRespuesta.CuentaInexistente, "La cuenta destino no existe en la base de datos del otro banco.", "", cupoRest);
                    }

                    tx.Commit();
                    return (true, true, CodigosRespuesta.Aprobado, "Transferencia acreditada y token guardado en la BD del otro banco.", nomEncontrado, cupoRest);
                }
                catch
                {
                    // Fallback a coordinación local si no se pudo conectar
                }
            }

            return (false, false, CodigosRespuesta.BancoDestinoSinRespuesta, "Servidor del otro banco no alcanzable por red.", solicitud.NombreDestino, null);
        }
    }
}

