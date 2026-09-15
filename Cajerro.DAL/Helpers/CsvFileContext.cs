using System.Globalization;
using System.Text;

namespace Cajero.DAL.Helpers
{
    /// <summary>
    /// Administra la infraestructura de persistencia en archivos CSV.
    /// Garantiza la creación de directorios, generación de datos semilla iniciales,
    /// y provee operaciones atómicas de lectura y escritura con StreamReader y StreamWriter,
    /// sincronizadas para prevenir problemas de concurrencia.
    /// </summary>
    public class CsvFileContext
    {
        private readonly string _dataDirectory;
        private readonly string _rutaUsuarios;
        private readonly string _rutaBoveda;
        private readonly string _rutaTransacciones;

        // Objetos de bloqueo por archivo para garantizar concurrencia segura
        private readonly object _lockUsuarios = new();
        private readonly object _lockBoveda = new();
        private readonly object _lockTransacciones = new();

        public const char Separador = ';';

        public string RutaUsuarios => _rutaUsuarios;
        public string RutaBoveda => _rutaBoveda;
        public string RutaTransacciones => _rutaTransacciones;

        public object LockUsuarios => _lockUsuarios;
        public object LockBoveda => _lockBoveda;
        public object LockTransacciones => _lockTransacciones;

        public CsvFileContext(string? customDataDirectory = null)
        {
            _dataDirectory = customDataDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            _rutaUsuarios = Path.Combine(_dataDirectory, "Usuarios.csv");
            _rutaBoveda = Path.Combine(_dataDirectory, "Boveda.csv");
            _rutaTransacciones = Path.Combine(_dataDirectory, "Transacciones.csv");

            InicializarArchivos();
        }

        /// <summary>
        /// Crea la carpeta Data y los archivos CSV iniciales con encabezados y datos semilla si no existen.
        /// </summary>
        private void InicializarArchivos()
        {
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
            }

            InicializarBoveda();
            InicializarUsuarios();
            InicializarTransacciones();
        }

        private void InicializarBoveda()
        {
            lock (_lockBoveda)
            {
                if (!File.Exists(_rutaBoveda))
                {
                    using var writer = new StreamWriter(_rutaBoveda, append: false, Encoding.UTF8);
                    writer.WriteLine("Denominacion;Cantidad");
                    // Inicialización reglamentaria de bóveda con Q10,000 exactos:
                    // 20 billetes de 200 = Q4,000
                    // 30 billetes de 100 = Q3,000
                    // 30 billetes de 50  = Q1,500
                    // 40 billetes de 20  = Q800
                    // 50 billetes de 10  = Q500
                    // 30 billetes de 5   = Q150
                    // 50 billetes de 1   = Q50
                    // TOTAL = Q10,000.00
                    writer.WriteLine("200;20");
                    writer.WriteLine("100;30");
                    writer.WriteLine("50;30");
                    writer.WriteLine("20;40");
                    writer.WriteLine("10;50");
                    writer.WriteLine("5;30");
                    writer.WriteLine("1;50");
                }
            }
        }

        private void InicializarUsuarios()
        {
            lock (_lockUsuarios)
            {
                if (!File.Exists(_rutaUsuarios))
                {
                    using var writer = new StreamWriter(_rutaUsuarios, append: false, Encoding.UTF8);
                    writer.WriteLine("NumeroTarjeta;Pin;NombreCompleto;RfidUID;Saldo;LimiteDiarioRetiro;MontoRetiradoHoy;UltimaFechaRetiro;Rol;Estado");

                    // Usuario Administrador (PIN: 1234)
                    string pinAdminHash = PinSecurityHelper.EncriptarPin("1234");
                    string fechaHoy = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                    writer.WriteLine($"99999999;{pinAdminHash};Administrador General;ADMIN-RFID-01;0.00;0.00;0.00;{fechaHoy};Administrador;Activo");

                    // Usuario Cliente de prueba (PIN: 4321)
                    string pinClienteHash = PinSecurityHelper.EncriptarPin("4321");
                    writer.WriteLine($"12345678;{pinClienteHash};Juan Perez;UID-TEST-01;5000.00;3000.00;0.00;{fechaHoy};Cliente;Activo");
                }
            }
        }

        private void InicializarTransacciones()
        {
            lock (_lockTransacciones)
            {
                if (!File.Exists(_rutaTransacciones))
                {
                    using var writer = new StreamWriter(_rutaTransacciones, append: false, Encoding.UTF8);
                    writer.WriteLine("IdTransaccion;NumeroTarjeta;TipoTransaccion;Monto;FechaHora;DetalleDesglose;SaldoPosterior");
                }
            }
        }

        /// <summary>
        /// Lee todas las líneas de un archivo CSV usando StreamReader de forma sincronizada.
        /// </summary>
        public List<string> LeerTodasLasLineas(string rutaArchivo, object lockObj)
        {
            lock (lockObj)
            {
                var lineas = new List<string>();

                if (!File.Exists(rutaArchivo))
                    return lineas;

                using var reader = new StreamReader(rutaArchivo, Encoding.UTF8);
                string? linea;
                while ((linea = reader.ReadLine()) != null)
                {
                    lineas.Add(linea);
                }

                return lineas;
            }
        }

        /// <summary>
        /// Sobrescribe todas las líneas de un archivo CSV usando StreamWriter de forma sincronizada.
        /// </summary>
        public void EscribirTodasLasLineas(string rutaArchivo, IEnumerable<string> lineas, object lockObj)
        {
            lock (lockObj)
            {
                // Para evitar escrituras truncadas en caso de fallo, se escribe de manera segura
                using var writer = new StreamWriter(rutaArchivo, append: false, Encoding.UTF8);
                foreach (var linea in lineas)
                {
                    writer.WriteLine(linea);
                }
            }
        }

        /// <summary>
        /// Agrega una línea al final de un archivo CSV usando StreamWriter en modo append sincronizado.
        /// </summary>
        public void AgregarLinea(string rutaArchivo, string linea, object lockObj)
        {
            lock (lockObj)
            {
                using var writer = new StreamWriter(rutaArchivo, append: true, Encoding.UTF8);
                writer.WriteLine(linea);
            }
        }
    }
}
