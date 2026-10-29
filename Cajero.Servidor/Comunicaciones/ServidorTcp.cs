using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cajero.Protocolo;
using Cajero.Servidor.AccesoDatos;
using Microsoft.Data.SqlClient;

namespace Cajero.Servidor.Comunicaciones
{
    public class ClienteConectado
    {
        public string CodigoCajero { get; set; } = string.Empty;
        public string NombreEquipo { get; set; } = string.Empty;
        public string IP { get; set; } = string.Empty;
        public TcpClient Client { get; set; } = null!;
        public NetworkStream Stream { get; set; } = null!;
        public StreamWriter Writer { get; set; } = null!;
        public DateTime UltimoHeartbeat { get; set; } = DateTime.Now;
        public DateTime UltimaActividadUsuario { get; set; } = DateTime.Now;
        public bool TieneSesionActiva { get; set; }
        public string? NumeroTarjetaSesion { get; set; }
    }

    public class ServidorTcp
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly RepositorioServidor _repo;
        private readonly ConcurrentDictionary<string, ClienteConectado> _clientesConectados = new();
        private System.Threading.Timer? _timerHeartbeat;

        public int Puerto { get; set; } = 5000;
        public bool EstaCorriendo { get; private set; }

        public event Action<string, string>? CajeroConectado;
        public event Action<string, string>? CajeroDesconectado;
        public event Action<string, string, decimal, string>? TransaccionProcesada;
        public event Action<string, string>? AlertaDisparada;
        public event Action<string, string, string, DateTime>? TokenGenerado; // token, usuario, cajero, exp

        public ServidorTcp(RepositorioServidor repo, int puerto = 5000)
        {
            _repo = repo;
            Puerto = puerto;
        }

        public void Iniciar()
        {
            if (EstaCorriendo) return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, Puerto);
            _listener.Start();
            EstaCorriendo = true;

            // Timer para chequear heartbeats cada 5 segundos
            _timerHeartbeat = new System.Threading.Timer(VerificarHeartbeats, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            Task.Run(() => AceptarClientesAsync(_cts.Token));
        }

        public void Detener()
        {
            if (!EstaCorriendo) return;

            EstaCorriendo = false;
            _cts?.Cancel();
            _timerHeartbeat?.Dispose();

            foreach (var kvp in _clientesConectados)
            {
                try
                {
                    kvp.Value.Client.Close();
                }
                catch { }
            }
            _clientesConectados.Clear();

            try
            {
                _listener?.Stop();
            }
            catch { }
        }

        private async Task AceptarClientesAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && EstaCorriendo)
            {
                try
                {
                    var tcpClient = await _listener!.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => AtenderClienteAsync(tcpClient, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    if (!EstaCorriendo) break;
                }
            }
        }

        private async Task AtenderClienteAsync(TcpClient tcpClient, CancellationToken token)
        {
            string ipCliente = ((IPEndPoint)tcpClient.Client.RemoteEndPoint!).Address.ToString();
            NetworkStream stream = tcpClient.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            ClienteConectado? infoCliente = null;

            try
            {
                while (!token.IsCancellationRequested && tcpClient.Connected)
                {
                    string? linea = await reader.ReadLineAsync(token);
                    if (linea == null) break; // Cliente desconectado

                    if (string.IsNullOrWhiteSpace(linea)) continue;

                    var baseMsg = ProtocoloHelper.DeserializarBase(linea);
                    if (baseMsg == null) continue;

                    // Si tiene sesión activa y pasaron más de 120 segundos sin actividad, expirar sesión
                    if (infoCliente != null && infoCliente.TieneSesionActiva && (DateTime.Now - infoCliente.UltimaActividadUsuario).TotalSeconds > 120)
                    {
                        infoCliente.TieneSesionActiva = false;
                        infoCliente.NumeroTarjetaSesion = null;
                        _repo.RegistrarEnBitacora("Servidor", "Sesión Expirada", $"Sesión de usuario expirada por inactividad (2 min) en {infoCliente.CodigoCajero}");
                    }

                    MensajeRespuesta? respuesta = await ProcesarMensajeAsync(linea, baseMsg, ipCliente, tcpClient, stream, writer, infoCliente);
                    if (baseMsg.Tipo == TiposMensaje.RegistrarCajero && respuesta?.Codigo == CodigosRespuesta.Aprobado)
                    {
                        var reqReg = ProtocoloHelper.Deserializar<SolicitudRegistroCajero>(linea);
                        if (reqReg != null && _clientesConectados.TryGetValue(reqReg.Codigo, out var registrado))
                        {
                            infoCliente = registrado;
                        }
                    }

                    if (respuesta != null)
                    {
                        string respJson = ProtocoloHelper.Serializar(respuesta);
                        await writer.WriteAsync(respJson);
                    }
                }
            }
            catch (Exception ex)
            {
                if (infoCliente != null)
                {
                    _repo.RegistrarEnBitacora("Servidor", "Error Socket", $"Conexión terminada para {infoCliente.CodigoCajero}: {ex.Message}");
                }
            }
            finally
            {
                if (infoCliente != null && !string.IsNullOrEmpty(infoCliente.CodigoCajero))
                {
                    _clientesConectados.TryRemove(infoCliente.CodigoCajero, out _);
                    _repo.ActualizarEstadoCajero(infoCliente.CodigoCajero, "Desconectado");
                    CajeroDesconectado?.Invoke(infoCliente.CodigoCajero, "Cierre de conexión TCP");
                }
                tcpClient.Close();
            }
        }

        private async Task<MensajeRespuesta> ProcesarMensajeAsync(
            string jsonCrudo,
            MensajeBase baseMsg,
            string ipCliente,
            TcpClient tcpClient,
            NetworkStream stream,
            StreamWriter writer,
            ClienteConectado? infoCliente)
        {
            var respuesta = new MensajeRespuesta
            {
                IdSolicitud = baseMsg.IdSolicitud
            };

            switch (baseMsg.Tipo)
            {
                case TiposMensaje.RegistrarCajero:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudRegistroCajero>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        var (cod, msj) = _repo.RegistrarCajero(req.Codigo, req.NombreEquipo, ipCliente, req.Version);
                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;

                        if (cod == CodigosRespuesta.Aprobado)
                        {
                            var nuevoCliente = new ClienteConectado
                            {
                                CodigoCajero = req.Codigo,
                                NombreEquipo = req.NombreEquipo,
                                IP = ipCliente,
                                Client = tcpClient,
                                Stream = stream,
                                Writer = writer,
                                UltimoHeartbeat = DateTime.Now
                            };
                            _clientesConectados[req.Codigo] = nuevoCliente;
                            CajeroConectado?.Invoke(req.Codigo, ipCliente);
                        }
                        break;
                    }

                case TiposMensaje.Heartbeat:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudHeartbeat>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        var (cod, msj) = _repo.Heartbeat(req.Codigo);
                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;

                        if (infoCliente != null)
                        {
                            infoCliente.UltimoHeartbeat = DateTime.Now;
                        }
                        else if (_clientesConectados.TryGetValue(req.Codigo, out var cli))
                        {
                            cli.UltimoHeartbeat = DateTime.Now;
                        }
                        break;
                    }

                case TiposMensaje.Login:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudLogin>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        string tarjeta = req.NumeroTarjeta.Trim();

                        // Si enviaron un UID de RFID, buscar en MapeoRfid
                        if (tarjeta.Length != 16)
                        {
                            string? mapeada = _repo.ObtenerTarjetaPorRfid(tarjeta);
                            if (!string.IsNullOrEmpty(mapeada)) tarjeta = mapeada;
                        }

                        var (cod, msj, idUsuario, nombreUsuario, reqCambioPin) = _repo.ValidarCredenciales(tarjeta, req.Pin, req.Cajero);
                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;

                        if (cod == CodigosRespuesta.Aprobado)
                        {
                            // Generar Token de 6 dígitos con vigencia de 2 minutos
                            var (tokenGen, exp) = _repo.GenerarToken(idUsuario, req.Cajero);

                            // Notificar a la UI del Servidor para mostrar la ventana "Token del usuario"
                            TokenGenerado?.Invoke(tokenGen, nombreUsuario, req.Cajero, exp);

                            respuesta.Datos = new DatosLogin
                            {
                                IdUsuario = idUsuario,
                                NombreUsuario = nombreUsuario,
                                RequiereCambioPin = reqCambioPin
                            };

                            if (infoCliente != null)
                            {
                                infoCliente.UltimaActividadUsuario = DateTime.Now;
                            }
                        }
                        else if (cod == CodigosRespuesta.TarjetaBloqueadaOInactiva)
                        {
                            AlertaDisparada?.Invoke("Tarjeta Bloqueada", $"Tarjeta **** {tarjeta[^4..]} bloqueada tras intentos fallidos.");
                        }
                        break;
                    }

                case TiposMensaje.ValidarToken:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudValidarToken>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        string tarjeta = req.NumeroTarjeta.Trim();
                        if (tarjeta.Length != 16)
                        {
                            string? mapeada = _repo.ObtenerTarjetaPorRfid(tarjeta);
                            if (!string.IsNullOrEmpty(mapeada)) tarjeta = mapeada;
                        }

                        var (cod, msj, idUsuario, nombreUsuario, saldo, limite, disponible) = _repo.ValidarToken(req.Cajero, tarjeta, req.Token);
                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;

                        if (cod == CodigosRespuesta.Aprobado)
                        {
                            var (_, numeroCuenta, _, _, _) = _repo.ConsultaSaldo(req.Cajero, tarjeta);
                            var (codBanco, nomBanco) = _repo.ObtenerInfoBancoPropio();
                            string tokenSesion = Guid.NewGuid().ToString("N");

                            respuesta.Datos = new DatosValidarToken
                            {
                                IdUsuario = idUsuario,
                                NombreUsuario = nombreUsuario,
                                Saldo = saldo,
                                LimiteDiario = limite,
                                DisponibleHoy = disponible,
                                NumeroCuenta = numeroCuenta,
                                CodigoBanco = codBanco,
                                NombreBanco = nomBanco,
                                TokenSesion = tokenSesion
                            };

                            if (infoCliente != null)
                            {
                                infoCliente.TieneSesionActiva = true;
                                infoCliente.NumeroTarjetaSesion = tarjeta;
                                infoCliente.UltimaActividadUsuario = DateTime.Now;
                            }
                        }
                        break;
                    }

                case TiposMensaje.Retiro:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudRetiro>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        string tarjeta = req.NumeroTarjeta.Trim();
                        if (tarjeta.Length != 16 && infoCliente?.NumeroTarjetaSesion != null)
                        {
                            tarjeta = infoCliente.NumeroTarjetaSesion;
                        }

                        var (cod, msj, idTx, nuevoSaldo, dispHoy, alt) = _repo.Retiro(req.Cajero, tarjeta, req.Monto, req.Desglose);
                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;

                        var datosRetiro = new DatosRetiro
                        {
                            IdTransaccion = idTx,
                            Saldo = nuevoSaldo,
                            DisponibleHoy = dispHoy,
                            DesgloseAlternativo = alt
                        };
                        respuesta.Datos = datosRetiro;

                        if (infoCliente != null) infoCliente.UltimaActividadUsuario = DateTime.Now;

                        TransaccionProcesada?.Invoke(req.Cajero, "RETIRO", req.Monto, cod);
                        break;
                    }

                case TiposMensaje.Deposito:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudDeposito>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        string tarjeta = req.NumeroTarjeta.Trim();
                        if (tarjeta.Length != 16 && infoCliente?.NumeroTarjetaSesion != null)
                        {
                            tarjeta = infoCliente.NumeroTarjetaSesion;
                        }

                        var (cod, msj, idTx, nuevoSaldo) = _repo.Deposito(req.Cajero, tarjeta, req.Monto, req.Desglose);
                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;

                        respuesta.Datos = new DatosDeposito
                        {
                            IdTransaccion = idTx,
                            Saldo = nuevoSaldo
                        };

                        if (infoCliente != null) infoCliente.UltimaActividadUsuario = DateTime.Now;

                        TransaccionProcesada?.Invoke(req.Cajero, "DEPOSITO", req.Monto, cod);
                        break;
                    }

                case TiposMensaje.CambioPin:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudCambioPin>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        string tarjeta = req.NumeroTarjeta.Trim();
                        if (tarjeta.Length != 16 && infoCliente?.NumeroTarjetaSesion != null)
                        {
                            tarjeta = infoCliente.NumeroTarjetaSesion;
                        }

                        var (cod, msj) = _repo.CambiarPin(tarjeta, req.PinActual, req.PinNuevo, "Usuario", req.Cajero);
                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;

                        if (infoCliente != null) infoCliente.UltimaActividadUsuario = DateTime.Now;

                        TransaccionProcesada?.Invoke(req.Cajero, "CAMBIO_PIN", 0, cod);
                        break;
                    }

                case TiposMensaje.ConsultaSaldo:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudConsultaSaldo>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        string tarjeta = req.NumeroTarjeta.Trim();
                        if (tarjeta.Length != 16 && infoCliente?.NumeroTarjetaSesion != null)
                        {
                            tarjeta = infoCliente.NumeroTarjetaSesion;
                        }

                        var (nom, cta, saldo, lim, disp) = _repo.ConsultaSaldo(req.Cajero, tarjeta);
                        respuesta.Codigo = CodigosRespuesta.Aprobado;
                        respuesta.Mensaje = "Consulta de saldo exitosa.";
                        respuesta.Datos = new DatosConsultaSaldo
                        {
                            NombreUsuario = nom,
                            NumeroCuenta = cta,
                            Saldo = saldo,
                            LimiteDiario = lim,
                            DisponibleHoy = disp
                        };

                        if (infoCliente != null) infoCliente.UltimaActividadUsuario = DateTime.Now;
                        break;
                    }

                case TiposMensaje.UltimasTransacciones:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudUltimasTransacciones>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        string tarjeta = req.NumeroTarjeta.Trim();
                        if (tarjeta.Length != 16 && infoCliente?.NumeroTarjetaSesion != null)
                        {
                            tarjeta = infoCliente.NumeroTarjetaSesion;
                        }

                        var lista = _repo.UltimasTransacciones(tarjeta, req.Cantidad);
                        respuesta.Codigo = CodigosRespuesta.Aprobado;
                        respuesta.Mensaje = "Últimas transacciones obtenidas.";
                        respuesta.Datos = new DatosUltimasTransacciones { Transacciones = lista };

                        if (infoCliente != null) infoCliente.UltimaActividadUsuario = DateTime.Now;
                        break;
                    }

                case TiposMensaje.ListarBancos:
                    {
                        var bancosDto = _repo.ObtenerBancosRed(true);
                        var (codPropio, nomPropio) = _repo.ObtenerInfoBancoPropio();

                        var items = bancosDto.Select(b => new ItemBancoProtocolo
                        {
                            CodigoBanco = b.CodigoBanco,
                            NombreBanco = b.NombreBanco,
                            EsPropio = b.EsPropio,
                            LimiteDiario = b.LimiteDiario,
                            DisponibleHoy = b.DisponibleHoy,
                            Activo = b.Activo
                        }).ToList();

                        respuesta.Codigo = CodigosRespuesta.Aprobado;
                        respuesta.Mensaje = "Catálogo de bancos obtenido.";
                        respuesta.Datos = new DatosListarBancos
                        {
                            Bancos = items,
                            CodigoBancoPropio = codPropio,
                            NombreBancoPropio = nomPropio
                        };
                        if (infoCliente != null) infoCliente.UltimaActividadUsuario = DateTime.Now;
                        break;
                    }

                case TiposMensaje.VerificarCuenta:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudVerificarCuenta>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        var (cod, msj, datos) = await _repo.VerificarCuentaDestinoAsync(req.BancoDestino, req.CuentaDestino);
                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;
                        respuesta.Datos = datos;

                        if (infoCliente != null) infoCliente.UltimaActividadUsuario = DateTime.Now;
                        break;
                    }

                case TiposMensaje.Transferencia:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudTransferencia>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        string tarjeta = req.NumeroTarjeta.Trim();
                        if (tarjeta.Length != 16)
                        {
                            if (infoCliente?.NumeroTarjetaSesion != null)
                            {
                                tarjeta = infoCliente.NumeroTarjetaSesion;
                            }
                            else
                            {
                                string? mapeada = _repo.ObtenerTarjetaPorRfid(tarjeta);
                                if (!string.IsNullOrEmpty(mapeada)) tarjeta = mapeada;
                            }
                        }

                        var (cod, msj, datos) = await _repo.EjecutarTransferenciaAsync(
                            req.Cajero,
                            tarjeta,
                            req.EsInterbancaria,
                            req.BancoDestino,
                            req.CuentaDestino,
                            req.Monto,
                            req.Concepto);

                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;
                        respuesta.Datos = datos;

                        if (infoCliente != null) infoCliente.UltimaActividadUsuario = DateTime.Now;

                        TransaccionProcesada?.Invoke(
                            req.Cajero,
                            req.EsInterbancaria ? "TRANSFERENCIA_INTERBANCARIA" : "TRANSFERENCIA_ENVIADA",
                            req.Monto,
                            cod);
                        break;
                    }

                // =========================================================================
                // PROTOCOLO INTERBANCARIO ESTÁNDAR (Mensajes recibidos desde OTROS BANCOS)
                // =========================================================================

                case TiposMensajeInterbancario.Ping:
                    {
                        var (codPropio, nomPropio) = _repo.ObtenerInfoBancoPropio();
                        respuesta.Codigo = CodigosRespuesta.Aprobado;
                        respuesta.Mensaje = $"PONG desde {codPropio} ({nomPropio})";
                        break;
                    }

                case TiposMensajeInterbancario.VerificarCuenta:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudIbVerificarCuenta>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        var (codPropio, nomPropio) = _repo.ObtenerInfoBancoPropio();
                        var bancos = _repo.ObtenerBancosRed(true);
                        var infoPropio = bancos.FirstOrDefault(b => b.EsPropio);
                        decimal cupoDisp = infoPropio?.DisponibleHoy ?? 50000m;

                        var (existe, _, numCuenta, _, nombreTitular) = _repo.VerificarCuentaLocal(req.NumeroCuenta);
                        if (!existe)
                        {
                            respuesta.Codigo = CodigosRespuesta.CuentaInexistente;
                            respuesta.Mensaje = $"La cuenta '{req.NumeroCuenta}' no existe o está inactiva en {nomPropio}.";
                            respuesta.Datos = new DatosIbVerificarCuenta
                            {
                                Existe = false,
                                NumeroCuenta = req.NumeroCuenta,
                                Banco = codPropio,
                                NombreBanco = nomPropio,
                                CupoDisponibleBanco = cupoDisp
                            };
                        }
                        else
                        {
                            respuesta.Codigo = CodigosRespuesta.Aprobado;
                            respuesta.Mensaje = "Cuenta activa verificada.";
                            respuesta.Datos = new DatosIbVerificarCuenta
                            {
                                Existe = true,
                                NumeroCuenta = numCuenta,
                                NombreTitular = nombreTitular,
                                Banco = codPropio,
                                NombreBanco = nomPropio,
                                Moneda = "GTQ",
                                CupoDisponibleBanco = cupoDisp
                            };
                        }
                        break;
                    }

                case TiposMensajeInterbancario.Transferir:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudIbTransferir>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        var (cod, msj, datos) = _repo.RecibirTransferenciaInterbancariaEntrante(req);
                        respuesta.Codigo = cod;
                        respuesta.Mensaje = msj;
                        respuesta.Datos = datos;

                        TransaccionProcesada?.Invoke("RED-IB", "TRANSFERENCIA_RECIBIDA", req.Monto, cod);
                        break;
                    }

                case TiposMensajeInterbancario.Consultar:
                    {
                        var req = ProtocoloHelper.Deserializar<SolicitudIbConsultar>(jsonCrudo);
                        if (req == null) return ErrorRespuesta(baseMsg.IdSolicitud, CodigosRespuesta.ErrorDelSistema, "Formato JSON inválido");

                        var trf = _repo.ConsultarTransferenciaPorToken(req.TokenTransferencia);
                        if (trf == null)
                        {
                            respuesta.Codigo = CodigosRespuesta.TokenNoEncontrado;
                            respuesta.Mensaje = $"No se encontró transferencia con token '{req.TokenTransferencia}'.";
                        }
                        else
                        {
                            respuesta.Codigo = CodigosRespuesta.Aprobado;
                            respuesta.Mensaje = $"Estado de transferencia {trf.TokenTransferencia}: {trf.Estado}";
                            respuesta.Datos = trf;
                        }
                        break;
                    }

                case TiposMensaje.Logout:
                    {
                        if (infoCliente != null)
                        {
                            infoCliente.TieneSesionActiva = false;
                            infoCliente.NumeroTarjetaSesion = null;
                        }
                        respuesta.Codigo = CodigosRespuesta.Aprobado;
                        respuesta.Mensaje = "Sesión cerrada correctamente.";
                        break;
                    }

                default:
                    respuesta.Codigo = CodigosRespuesta.ErrorDelSistema;
                    respuesta.Mensaje = $"Tipo de mensaje no reconocido: {baseMsg.Tipo}";
                    break;
            }

            return respuesta;
        }

        private static MensajeRespuesta ErrorRespuesta(string idSolicitud, string codigo, string mensaje)
        {
            return new MensajeRespuesta
            {
                IdSolicitud = idSolicitud,
                Codigo = codigo,
                Mensaje = mensaje
            };
        }

        public async Task<bool> EnviarComandoAsync(string codigoCajero, string comando, Dictionary<string, string>? parametros = null)
        {
            if (_clientesConectados.TryGetValue(codigoCajero, out var cli))
            {
                try
                {
                    var cmd = new MensajeComando
                    {
                        Comando = comando,
                        Parametros = parametros ?? new Dictionary<string, string>()
                    };

                    string json = ProtocoloHelper.Serializar(cmd);
                    await cli.Writer.WriteAsync(json);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        private void VerificarHeartbeats(object? state)
        {
            DateTime ahora = DateTime.Now;
            foreach (var kvp in _clientesConectados)
            {
                var cli = kvp.Value;
                // Si pasaron más de 30 segundos sin heartbeat
                if ((ahora - cli.UltimoHeartbeat).TotalSeconds > 30)
                {
                    _clientesConectados.TryRemove(kvp.Key, out _);
                    try
                    {
                        cli.Client.Close();
                    }
                    catch { }

                    _repo.ActualizarEstadoCajero(cli.CodigoCajero, "Desconectado");

                    // Registrar en bitácora y alerta
                    _repo.RegistrarEnBitacora("Servidor", "Pérdida de Heartbeat", $"Cajero {cli.CodigoCajero} no envió heartbeat en 30s. Marcado como Desconectado.");

                    try
                    {
                        using var conn = ConexionBD.ObtenerConexion();
                        conn.Open();
                        string sql = @"
                            INSERT INTO dbo.Alerta (Tipo, IdCajero, Descripcion, Fecha)
                            SELECT 'Cajero Desconectado', IdCajero, 
                                   CONCAT('El cajero ', Codigo, ' se ha desconectado por falta de heartbeat.'), GETDATE()
                            FROM dbo.Cajero WHERE Codigo = @Codigo";
                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@Codigo", cli.CodigoCajero);
                        cmd.ExecuteNonQuery();
                    }
                    catch { }

                    CajeroDesconectado?.Invoke(cli.CodigoCajero, "Falta de heartbeat por más de 30s");
                    AlertaDisparada?.Invoke("Cajero Desconectado", $"El cajero {cli.CodigoCajero} no envió heartbeat en 30s.");
                }
            }
        }
    }
}
