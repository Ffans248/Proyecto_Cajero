using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace Cajero.Hardware
{
    public class LectorRFID : IDisposable
    {
        private SerialPort _puertoSerial;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _estaEscuchando;

        // Evento que se dispararÃ¡ cuando se lea un UID vÃ¡lido
        public event Action<string> TarjetaLeida;

                public static string ConvertirNfcA16Digitos(string uidCrudo)
        {
            if (string.IsNullOrEmpty(uidCrudo)) return "0000000000000000";
            
            string uidLimpio = uidCrudo.Trim().ToUpper();
            try
            {
                ulong numero = Convert.ToUInt64(uidLimpio, 16);
                return numero.ToString("D16");
            }
            catch
            {
                long hash = Math.Abs((long)uidLimpio.GetHashCode());
                return hash.ToString("D16");
            }
        }

        public LectorRFID(string puerto = "COM15", int baudRate = 9600)
        {
            _puertoSerial = new SerialPort(puerto, baudRate)
            {
                ReadTimeout = 500, // Timeout para no bloquear la lectura por siempre
                NewLine = "\n"     // El ESP32 envÃ­a un salto de lÃ­nea al final
            };
        }

        public void IniciarEscucha()
        {
            if (_estaEscuchando) return;

            try
            {
                if (!_puertoSerial.IsOpen)
                {
                    _puertoSerial.Open();
                }

                _estaEscuchando = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // Arrancamos la lectura en un hilo en segundo plano
                Task.Run(() => LeerPuerto(_cancellationTokenSource.Token));
            }
            catch (UnauthorizedAccessException)
            {
                // El puerto estÃ¡ ocupado o denegado
                throw new Exception("El puerto COM15 estÃ¡ ocupado por otra aplicaciÃ³n (Â¿Monitor Serie de Arduino abierto?).");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al abrir el puerto: {ex.Message}");
            }
        }

        private void LeerPuerto(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _puertoSerial.IsOpen)
            {
                try
                {
                    // Bloquea hasta que encuentra \n o salta el ReadTimeout
                    string uidCrudo = _puertoSerial.ReadLine();
                    string uidLimpio = uidCrudo.Trim(); // Limpiar espacios y saltos de lÃ­nea (\r, \n)

                    if (!string.IsNullOrEmpty(uidLimpio))
                    {
                        // Disparar el evento notificando que se leyÃ³ una tarjeta
                        TarjetaLeida?.Invoke(uidLimpio);
                    }
                }
                catch (TimeoutException)
                {
                    // Es normal, ocurre cuando no hay tarjetas pasando en 500ms. Sigue el ciclo.
                }
                catch (Exception)
                {
                    // Otras excepciones (ej. desconexiÃ³n abrupta del USB)
                    if (!token.IsCancellationRequested)
                    {
                        DetenerEscucha();
                    }
                }
            }
        }

        public void DetenerEscucha()
        {
            _estaEscuchando = false;
            _cancellationTokenSource?.Cancel();
            
            if (_puertoSerial != null && _puertoSerial.IsOpen)
            {
                _puertoSerial.Close();
            }
        }

        public void Dispose()
        {
            DetenerEscucha();
            _puertoSerial?.Dispose();
        }
    }
}

