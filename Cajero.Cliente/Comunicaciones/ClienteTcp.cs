using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cajero.Protocolo;

namespace Cajero.Cliente.Comunicaciones
{
    public class ClienteTcp
    {
        private readonly ConfiguracionCliente _config;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private System.Threading.Timer? _timerHeartbeat;
        private System.Threading.Timer? _timerReconexion;

        private readonly ConcurrentDictionary<string, TaskCompletionSource<MensajeRespuesta>> _solicitudesPendientes = new();

        public bool EstaConectado => _tcpClient != null && _tcpClient.Connected;
        public bool EstaHabilitado { get; set; } = true;

        public event Action<bool>? EstadoConexionCambiado;
        public event Action<string, Dictionary<string, string>>? ComandoRecibido;

        public ClienteTcp(ConfiguracionCliente config)
        {
            _config = config;
        }

        public void Iniciar()
        {
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => IntentarConexionAsync());

            // Timer de reintento de conexión cada 5 segundos si está desconectado
            _timerReconexion = new System.Threading.Timer(async _ =>
            {
                if (!EstaConectado)
                {
                    await IntentarConexionAsync();
                }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            // Timer de Heartbeat cada 10 segundos
            _timerHeartbeat = new System.Threading.Timer(async _ =>
            {
                if (EstaConectado)
                {
                    try
                    {
                        var req = new SolicitudHeartbeat { Codigo = _config.CodigoCajero };
                        await EnviarSinEsperarAsync(req);
                    }
                    catch { }
                }
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        public async Task<bool> IntentarConexionAsync()
        {
            try
            {
                if (_tcpClient != null)
                {
                    try { _tcpClient.Close(); } catch { }
                }

                _tcpClient = new TcpClient();
                var connectTask = _tcpClient.ConnectAsync(_config.ServidorIP, _config.ServidorPuerto);
                var delayTask = Task.Delay(4000);

                if (await Task.WhenAny(connectTask, delayTask) == connectTask && _tcpClient.Connected)
                {
                    _stream = _tcpClient.GetStream();
                    _reader = new StreamReader(_stream, Encoding.UTF8);
                    _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

                    // Iniciar bucle de escucha de respuestas y comandos
                    _ = Task.Run(() => EscucharServidorAsync(_cts!.Token));

                    // Enviar registro de cajero inmediatamente
                    var reg = new SolicitudRegistroCajero
                    {
                        Codigo = _config.CodigoCajero,
                        NombreEquipo = Environment.MachineName,
                        IP = _config.ServidorIP,
                        Version = "2.0"
                    };

                    var resp = await EnviarSolicitudAsync(reg, 5000);
                    bool ok = (resp != null && resp.Codigo == CodigosRespuesta.Aprobado);

                    EstadoConexionCambiado?.Invoke(true);
                    return true;
                }
            }
            catch { }

            EstadoConexionCambiado?.Invoke(false);
            return false;
        }

        private async Task EscucharServidorAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && EstaConectado)
                {
                    string? linea = await _reader!.ReadLineAsync(token);
                    if (linea == null) break;

                    if (string.IsNullOrWhiteSpace(linea)) continue;

                    var baseMsg = ProtocoloHelper.DeserializarBase(linea);
                    if (baseMsg == null) continue;

                    if (baseMsg.Tipo == TiposMensaje.Respuesta)
                    {
                        var resp = ProtocoloHelper.Deserializar<MensajeRespuesta>(linea);
                        if (resp != null && _solicitudesPendientes.TryRemove(resp.IdSolicitud, out var tcs))
                        {
                            tcs.TrySetResult(resp);
                        }
                    }
                    else if (baseMsg.Tipo == TiposMensaje.Comando)
                    {
                        var cmd = ProtocoloHelper.Deserializar<MensajeComando>(linea);
                        if (cmd != null)
                        {
                            ProcesarComandoServidor(cmd);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                EstadoConexionCambiado?.Invoke(false);
            }
        }

        private void ProcesarComandoServidor(MensajeComando cmd)
        {
            if (cmd.Comando == TiposComando.FueraDeServicio)
            {
                EstaHabilitado = false;
            }
            else if (cmd.Comando == TiposComando.Habilitar)
            {
                EstaHabilitado = true;
            }

            ComandoRecibido?.Invoke(cmd.Comando, cmd.Parametros);
        }

        public async Task<MensajeRespuesta?> EnviarSolicitudAsync(MensajeBase solicitud, int timeoutMs = 10000)
        {
            if (!EstaConectado) return null;

            var tcs = new TaskCompletionSource<MensajeRespuesta>(TaskCreationOptions.RunContinuationsAsynchronously);
            _solicitudesPendientes[solicitud.IdSolicitud] = tcs;

            try
            {
                string json = ProtocoloHelper.Serializar(solicitud);
                await _writer!.WriteAsync(json);

                using var ctsTimeout = new CancellationTokenSource(timeoutMs);
                using (ctsTimeout.Token.Register(() => tcs.TrySetCanceled()))
                {
                    return await tcs.Task;
                }
            }
            catch (TaskCanceledException)
            {
                _solicitudesPendientes.TryRemove(solicitud.IdSolicitud, out _);
                return new MensajeRespuesta
                {
                    IdSolicitud = solicitud.IdSolicitud,
                    Codigo = CodigosRespuesta.ErrorDelSistema,
                    Mensaje = "Tiempo de espera agotado (10s) sin respuesta del servidor."
                };
            }
            catch (Exception ex)
            {
                _solicitudesPendientes.TryRemove(solicitud.IdSolicitud, out _);
                return new MensajeRespuesta
                {
                    IdSolicitud = solicitud.IdSolicitud,
                    Codigo = CodigosRespuesta.ErrorDelSistema,
                    Mensaje = "Error de comunicación: " + ex.Message
                };
            }
        }

        public async Task EnviarSinEsperarAsync(MensajeBase mensaje)
        {
            if (!EstaConectado) return;
            string json = ProtocoloHelper.Serializar(mensaje);
            await _writer!.WriteAsync(json);
        }

        public void Detener()
        {
            _cts?.Cancel();
            _timerHeartbeat?.Dispose();
            _timerReconexion?.Dispose();
            try { _tcpClient?.Close(); } catch { }
        }
    }
}
