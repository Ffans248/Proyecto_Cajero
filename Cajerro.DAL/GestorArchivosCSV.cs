using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Cajerro.DAL.Interfaces;
using Cajerro.DAL.Modelos;

namespace Cajerro.DAL
{
    /// <summary>
    /// Capa de Acceso a Datos (DAL) encargada de la persistencia de datos del cajero automático
    /// utilizando archivos de texto plano con formato delimitado por comas (.csv).
    /// </summary>
    public class GestorArchivosCSV : IGestorArchivos
    {
        #region Constantes y Rutas de Archivos

        private readonly string _rutaDirectorio;
        private readonly string _rutaUsuarios;
        private readonly string _rutaTransacciones;
        private readonly string _rutaBoveda;
        private readonly string _rutaBitacora;
        private readonly string _rutaEstadoCajero;

        private const string EncabezadoUsuarios = "NumeroTarjeta,PIN,Nombre,SaldoActual,LimiteDiario";
        private const string EncabezadoTransacciones = "NumeroTarjeta,FechaHora,TipoMovimiento,Monto,Token";
        private const string EncabezadoBoveda = "Denominacion,Cantidad";
        private const string EncabezadoBitacora = "FechaHora,TipoEvento,UsuarioOTarjeta,Detalle";

        // Denominaciones de billetes oficiales del proyecto: 200, 100, 50, 20, 10, 5 y 1.
        private static readonly int[] DenominacionesOficiales = [200, 100, 50, 20, 10, 5, 1];

        #endregion

        #region Constructor e Inicialización

        /// <summary>
        /// Inicializa una nueva instancia de <see cref="GestorArchivosCSV"/>.
        /// Verifica la existencia de los archivos necesarios (Usuarios.csv, Transacciones.csv, Boveda.csv y Bitacora.csv)
        /// y los crea con sus encabezados correspondientes si no existen.
        /// </summary>
        /// <param name="rutaDirectorio">Ruta de la carpeta donde residirán los archivos .csv. Si es null o vacía, usa el directorio de ejecución.</param>
        public GestorArchivosCSV(string? rutaDirectorio = null)
        {
            _rutaDirectorio = string.IsNullOrWhiteSpace(rutaDirectorio)
                ? AppDomain.CurrentDomain.BaseDirectory
                : rutaDirectorio;

            _rutaUsuarios = Path.Combine(_rutaDirectorio, "Usuarios.csv");
            _rutaTransacciones = Path.Combine(_rutaDirectorio, "Transacciones.csv");
            _rutaBoveda = Path.Combine(_rutaDirectorio, "Boveda.csv");
            _rutaBitacora = Path.Combine(_rutaDirectorio, "Bitacora.csv");
            _rutaEstadoCajero = Path.Combine(_rutaDirectorio, "EstadoCajero.txt");

            InicializarArchivos();
        }

        /// <summary>
        /// Comprueba la existencia de los archivos de persistencia. Si no existen, los crea con su respectiva cabecera.
        /// </summary>
        private void InicializarArchivos()
        {
            try
            {
                if (!Directory.Exists(_rutaDirectorio))
                {
                    Directory.CreateDirectory(_rutaDirectorio);
                }

                // 1. Inicialización de Usuarios.csv
                if (!File.Exists(_rutaUsuarios))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaUsuarios, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoUsuarios);
                        sw.WriteLine("9999999999999999,1234,Administrador Gerencia,10000.00,5000.00");
                        sw.WriteLine("1234567890123456,1234,Juan Perez,5000.00,2000.00");
                    }
                    Debug.WriteLine($"[DAL INFO] Archivo creado: {_rutaUsuarios}");
                }

                // 2. Inicialización de Transacciones.csv
                if (!File.Exists(_rutaTransacciones))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaTransacciones, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoTransacciones);
                    }
                    Debug.WriteLine($"[DAL INFO] Archivo creado: {_rutaTransacciones}");
                }

                // 3. Inicialización de Boveda.csv
                if (!File.Exists(_rutaBoveda))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaBoveda, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoBoveda);
                        // Se inicializa la bóveda con cada denominación reglamentaria en cantidad 0
                        foreach (int denominacion in DenominacionesOficiales)
                        {
                            sw.WriteLine($"{denominacion},0");
                        }
                    }
                    Debug.WriteLine($"[DAL INFO] Archivo creado: {_rutaBoveda}");
                }

                // 4. Inicialización de Bitacora.csv
                if (!File.Exists(_rutaBitacora))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaBitacora, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoBitacora);
                    }
                    Debug.WriteLine($"[DAL INFO] Archivo creado: {_rutaBitacora}");
                }
            }
            catch (IOException ex)
            {
                RegistrarError("InicializarArchivos", "El archivo está bloqueado por otro proceso (posiblemente Excel o concurrencia).", ex);
            }
            catch (Exception ex)
            {
                RegistrarError("InicializarArchivos", "Ocurrió un error inesperado al inicializar los archivos de base de datos.", ex);
            }
        }

        #endregion

        #region Métodos de Persistencia - Usuarios

        /// <summary>
        /// Lee y retorna todos los registros de usuarios almacenados en Usuarios.csv.
        /// </summary>
        /// <returns>Lista de objetos <see cref="Usuario"/>. En caso de error o archivo vacío, retorna lista vacía.</returns>
        public List<Usuario> ObtenerTodosLosUsuarios()
        {
            var usuarios = new List<Usuario>();

            if (!File.Exists(_rutaUsuarios))
            {
                Debug.WriteLine($"[DAL WARN] El archivo {_rutaUsuarios} no existe.");
                return usuarios;
            }

            try
            {
                using (StreamReader sr = new StreamReader(_rutaUsuarios, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine(); // Descartar línea de encabezados
                    if (encabezado == null) return usuarios;

                    int lineaNumero = 1;
                    string? linea;

                    while ((linea = sr.ReadLine()) != null)
                    {
                        lineaNumero++;
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        string[] partes = linea.Split(',');
                        if (partes.Length < 5)
                        {
                            RegistrarAdvertencia("ObtenerTodosLosUsuarios", $"Línea {lineaNumero} corrupta o con columnas incompletas: {linea}");
                            continue;
                        }

                        string tarjeta = partes[0].Trim();
                        string pin = partes[1].Trim();
                        string nombre = partes[2].Trim();

                        // Casteo seguro de valores monetarios con InvariantCulture para evitar conflictos de comas/puntos decimales
                        if (!decimal.TryParse(partes[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal saldo))
                        {
                            RegistrarAdvertencia("ObtenerTodosLosUsuarios", $"Línea {lineaNumero}: Formato de Saldo inválido '{partes[3]}'. Registro omitido.");
                            continue;
                        }

                        if (!decimal.TryParse(partes[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal limite))
                        {
                            RegistrarAdvertencia("ObtenerTodosLosUsuarios", $"Línea {lineaNumero}: Formato de Límite Diario inválido '{partes[4]}'. Registro omitido.");
                            continue;
                        }

                        usuarios.Add(new Usuario
                        {
                            NumeroTarjeta = tarjeta,
                            PIN = pin,
                            Nombre = nombre,
                            SaldoActual = saldo,
                            LimiteDiario = limite
                        });
                    }
                }
            }
            catch (IOException ex)
            {
                RegistrarError("ObtenerTodosLosUsuarios", "No se pudo leer Usuarios.csv porque está siendo utilizado por otro programa (ej. Excel).", ex);
                return new List<Usuario>();
            }
            catch (FormatException ex)
            {
                RegistrarError("ObtenerTodosLosUsuarios", "Error de conversión de datos numéricos al procesar usuarios.", ex);
                return new List<Usuario>();
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerTodosLosUsuarios", "Error inesperado al leer los usuarios.", ex);
                return new List<Usuario>();
            }

            return usuarios;
        }

        /// <summary>
        /// Busca un usuario por su número de tarjeta de 16 posiciones.
        /// </summary>
        /// <param name="tarjeta">Número de tarjeta a consultar.</param>
        /// <returns>El objeto <see cref="Usuario"/> coincidente, o null si no se encuentra o ocurre un error.</returns>
        public Usuario? ObtenerUsuarioPorTarjeta(string tarjeta)
        {
            if (string.IsNullOrWhiteSpace(tarjeta)) return null;

            if (!File.Exists(_rutaUsuarios)) return null;

            try
            {
                using (StreamReader sr = new StreamReader(_rutaUsuarios, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return null;

                    string tarjetaBuscada = tarjeta.Trim();
                    string? linea;

                    while ((linea = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        string[] partes = linea.Split(',');
                        if (partes.Length >= 5 && partes[0].Trim().Equals(tarjetaBuscada, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!decimal.TryParse(partes[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal saldo))
                            {
                                throw new FormatException($"El saldo '{partes[3]}' de la tarjeta {tarjeta} tiene formato numérico inválido.");
                            }

                            if (!decimal.TryParse(partes[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal limite))
                            {
                                throw new FormatException($"El límite diario '{partes[4]}' de la tarjeta {tarjeta} tiene formato numérico inválido.");
                            }

                            return new Usuario
                            {
                                NumeroTarjeta = partes[0].Trim(),
                                PIN = partes[1].Trim(),
                                Nombre = partes[2].Trim(),
                                SaldoActual = saldo,
                                LimiteDiario = limite
                            };
                        }
                    }
                }

                return null; // Tarjeta no encontrada
            }
            catch (IOException ex)
            {
                RegistrarError("ObtenerUsuarioPorTarjeta", "El archivo Usuarios.csv está bloqueado por otra aplicación.", ex);
                return null;
            }
            catch (FormatException ex)
            {
                RegistrarError("ObtenerUsuarioPorTarjeta", "Error en el formato de datos del usuario buscado.", ex);
                return null;
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerUsuarioPorTarjeta", "Error inesperado al buscar el usuario por tarjeta.", ex);
                return null;
            }
        }

        /// <summary>
        /// Añade un nuevo usuario al final del archivo Usuarios.csv.
        /// </summary>
        /// <param name="nuevo">Instancia con los datos del usuario a insertar.</param>
        /// <returns>True si se registró satisfactoriamente; False en caso contrario.</returns>
        public bool CrearNuevoUsuario(Usuario nuevo)
        {
            if (nuevo == null || string.IsNullOrWhiteSpace(nuevo.NumeroTarjeta))
            {
                RegistrarAdvertencia("CrearNuevoUsuario", "Se intentó crear un usuario nulo o sin número de tarjeta.");
                return false;
            }

            try
            {
                // Formateo explícito con InvariantCulture para que los decimales utilicen '.' y no interfieran con las comas del CSV
                string linea = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3:F2},{4:F2}",
                    nuevo.NumeroTarjeta.Trim(),
                    nuevo.PIN.Trim(),
                    nuevo.Nombre.Trim(),
                    nuevo.SaldoActual,
                    nuevo.LimiteDiario
                );

                using (StreamWriter sw = new StreamWriter(_rutaUsuarios, append: true, Encoding.UTF8))
                {
                    sw.WriteLine(linea);
                }

                return true;
            }
            catch (IOException ex)
            {
                RegistrarError("CrearNuevoUsuario", "El archivo Usuarios.csv está abierto o bloqueado por otro proceso.", ex);
                return false;
            }
            catch (FormatException ex)
            {
                RegistrarError("CrearNuevoUsuario", "Error de formato al serializar los datos del nuevo usuario.", ex);
                return false;
            }
            catch (Exception ex)
            {
                RegistrarError("CrearNuevoUsuario", "Error inesperado al crear el usuario en el archivo.", ex);
                return false;
            }
        }

        /// <summary>
        /// Modifica el saldo actual de un usuario específico y reescribe el archivo Usuarios.csv manteniendo la integridad.
        /// </summary>
        /// <param name="tarjeta">Número de tarjeta del usuario a actualizar.</param>
        /// <param name="nuevoSaldo">Nuevo monto de saldo.</param>
        /// <returns>True si se actualizó el saldo y se reescribió el archivo; False si el usuario no existe o hubo error.</returns>
        public bool ActualizarSaldo(string tarjeta, decimal nuevoSaldo)
        {
            if (string.IsNullOrWhiteSpace(tarjeta)) return false;

            try
            {
                var usuarios = ObtenerTodosLosUsuarios();
                var usuarioEncontrado = usuarios.FirstOrDefault(u => u.NumeroTarjeta.Equals(tarjeta.Trim(), StringComparison.OrdinalIgnoreCase));

                if (usuarioEncontrado == null)
                {
                    RegistrarAdvertencia("ActualizarSaldo", $"No se localizó al usuario con tarjeta {tarjeta} para modificar su saldo.");
                    return false;
                }

                usuarioEncontrado.SaldoActual = nuevoSaldo;

                return GuardarTodosLosUsuarios(usuarios);
            }
            catch (IOException ex)
            {
                RegistrarError("ActualizarSaldo", "No se puede actualizar el saldo: archivo Usuarios.csv bloqueado.", ex);
                return false;
            }
            catch (Exception ex)
            {
                RegistrarError("ActualizarSaldo", "Error general al actualizar el saldo del usuario.", ex);
                return false;
            }
        }

        /// <summary>
        /// Sobrescribe la información de un usuario (útil para cambio de PIN, modificación de límite diario o cambio de tarjeta).
        /// </summary>
        /// <param name="usuarioActualizado">Objeto usuario con los datos actualizados.</param>
        /// <returns>True si se actualizó con éxito; de lo contrario False.</returns>
        public bool ActualizarUsuario(Usuario usuarioActualizado)
        {
            if (usuarioActualizado == null || string.IsNullOrWhiteSpace(usuarioActualizado.NumeroTarjeta))
            {
                return false;
            }

            try
            {
                var usuarios = ObtenerTodosLosUsuarios();
                int index = usuarios.FindIndex(u => u.NumeroTarjeta.Equals(usuarioActualizado.NumeroTarjeta.Trim(), StringComparison.OrdinalIgnoreCase));

                if (index < 0)
                {
                    RegistrarAdvertencia("ActualizarUsuario", $"Usuario {usuarioActualizado.NumeroTarjeta} no encontrado para actualización.");
                    return false;
                }

                usuarios[index] = usuarioActualizado;
                return GuardarTodosLosUsuarios(usuarios);
            }
            catch (IOException ex)
            {
                RegistrarError("ActualizarUsuario", "Archivo Usuarios.csv bloqueado.", ex);
                return false;
            }
            catch (Exception ex)
            {
                RegistrarError("ActualizarUsuario", "Error al actualizar el registro del usuario.", ex);
                return false;
            }
        }

        /// <summary>
        /// Método interno auxiliar para reescribir de forma atómica la lista completa de usuarios.
        /// </summary>
        private bool GuardarTodosLosUsuarios(List<Usuario> usuarios)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(_rutaUsuarios, append: false, Encoding.UTF8))
                {
                    sw.WriteLine(EncabezadoUsuarios);
                    foreach (var u in usuarios)
                    {
                        sw.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3:F2},{4:F2}",
                            u.NumeroTarjeta.Trim(),
                            u.PIN.Trim(),
                            u.Nombre.Trim(),
                            u.SaldoActual,
                            u.LimiteDiario
                        ));
                    }
                }
                return true;
            }
            catch (IOException ex)
            {
                RegistrarError("GuardarTodosLosUsuarios", "El archivo Usuarios.csv está bloqueado por otra aplicación.", ex);
                return false;
            }
            catch (Exception ex)
            {
                RegistrarError("GuardarTodosLosUsuarios", "Error inesperado al guardar la lista de usuarios.", ex);
                return false;
            }
        }

        #endregion

        #region Métodos de Persistencia - Transacciones

        /// <summary>
        /// Añade una nueva transacción al final de Transacciones.csv en modo Append.
        /// </summary>
        /// <param name="t">Objeto de la transacción efectuada.</param>
        /// <returns>True si la transacción se persistió exitosamente; False en caso contrario.</returns>
        public bool RegistrarTransaccion(Transaccion t)
        {
            if (t == null)
            {
                RegistrarAdvertencia("RegistrarTransaccion", "Se intentó registrar una transacción nula.");
                return false;
            }

            try
            {
                // Formato ISO estándar para fecha y hora, punto decimal invariable y token de autorización
                string linea = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1:yyyy-MM-dd HH:mm:ss},{2},{3:F2},{4}",
                    t.NumeroTarjeta.Trim(),
                    t.FechaHora,
                    t.TipoMovimiento.Trim(),
                    t.Monto,
                    (t.Token ?? "").Trim()
                );

                using (StreamWriter sw = new StreamWriter(_rutaTransacciones, append: true, Encoding.UTF8))
                {
                    sw.WriteLine(linea);
                }

                return true;
            }
            catch (IOException ex)
            {
                RegistrarError("RegistrarTransaccion", "No se pudo registrar la transacción: Transacciones.csv está bloqueado.", ex);
                return false;
            }
            catch (FormatException ex)
            {
                RegistrarError("RegistrarTransaccion", "Error al formatear los datos de la transacción.", ex);
                return false;
            }
            catch (Exception ex)
            {
                RegistrarError("RegistrarTransaccion", "Error inesperado al registrar la transacción.", ex);
                return false;
            }
        }

        /// <summary>
        /// Retorna la lista de transacciones asociadas exclusivamente al número de tarjeta suministrado.
        /// </summary>
        /// <param name="tarjeta">Número de tarjeta a filtrar.</param>
        /// <returns>Lista de transacciones del usuario. Si no hay transacciones o ocurre error, retorna lista vacía.</returns>
        public List<Transaccion> ObtenerHistorial(string tarjeta)
        {
            var historial = new List<Transaccion>();

            if (string.IsNullOrWhiteSpace(tarjeta) || !File.Exists(_rutaTransacciones))
            {
                return historial;
            }

            try
            {
                using (StreamReader sr = new StreamReader(_rutaTransacciones, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return historial;

                    string tarjetaFiltro = tarjeta.Trim();
                    int lineaNumero = 1;
                    string? linea;

                    while ((linea = sr.ReadLine()) != null)
                    {
                        lineaNumero++;
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        string[] partes = linea.Split(',');
                        if (partes.Length < 4)
                        {
                            RegistrarAdvertencia("ObtenerHistorial", $"Línea {lineaNumero} corrupta en Transacciones.csv: {linea}");
                            continue;
                        }

                        string tarjetaReg = partes[0].Trim();
                        if (!tarjetaReg.Equals(tarjetaFiltro, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // No pertenece a la tarjeta consultada
                        }

                        // Parseo tolerante de fecha y hora
                        if (!DateTime.TryParse(partes[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fechaHora))
                        {
                            if (!DateTime.TryParse(partes[1].Trim(), out fechaHora))
                            {
                                RegistrarAdvertencia("ObtenerHistorial", $"Línea {lineaNumero}: Fecha u hora no válida '{partes[1]}'. Registro omitido.");
                                continue;
                            }
                        }

                        // Parseo de monto
                        if (!decimal.TryParse(partes[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto))
                        {
                            RegistrarAdvertencia("ObtenerHistorial", $"Línea {lineaNumero}: Monto inválido '{partes[3]}'. Registro omitido.");
                            continue;
                        }

                        string token = partes.Length >= 5 ? partes[4].Trim() : "";

                        historial.Add(new Transaccion
                        {
                            NumeroTarjeta = tarjetaReg,
                            FechaHora = fechaHora,
                            TipoMovimiento = partes[2].Trim(),
                            Monto = monto,
                            Token = token
                        });
                    }
                }
            }
            catch (IOException ex)
            {
                RegistrarError("ObtenerHistorial", "Transacciones.csv está bloqueado por otro proceso.", ex);
                return new List<Transaccion>();
            }
            catch (FormatException ex)
            {
                RegistrarError("ObtenerHistorial", "Error de conversión de formato al leer historial de transacciones.", ex);
                return new List<Transaccion>();
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerHistorial", "Error general al consultar el historial de transacciones.", ex);
                return new List<Transaccion>();
            }

            return historial;
        }

        /// <summary>
        /// Obtiene todas las transacciones registradas en el sistema (requerido por BLL para reportes administrativos y de control).
        /// </summary>
        /// <returns>Lista completa de transacciones del cajero.</returns>
        public List<Transaccion> ObtenerTodasLasTransacciones()
        {
            var transacciones = new List<Transaccion>();

            if (!File.Exists(_rutaTransacciones)) return transacciones;

            try
            {
                using (StreamReader sr = new StreamReader(_rutaTransacciones, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return transacciones;

                    int lineaNumero = 1;
                    string? linea;

                    while ((linea = sr.ReadLine()) != null)
                    {
                        lineaNumero++;
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        string[] partes = linea.Split(',');
                        if (partes.Length < 4) continue;

                        if (!DateTime.TryParse(partes[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fechaHora))
                        {
                            if (!DateTime.TryParse(partes[1].Trim(), out fechaHora)) continue;
                        }

                        if (!decimal.TryParse(partes[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto)) continue;

                        string token = partes.Length >= 5 ? partes[4].Trim() : "";

                        transacciones.Add(new Transaccion
                        {
                            NumeroTarjeta = partes[0].Trim(),
                            FechaHora = fechaHora,
                            TipoMovimiento = partes[2].Trim(),
                            Monto = monto,
                            Token = token
                        });
                    }
                }
            }
            catch (IOException ex)
            {
                RegistrarError("ObtenerTodasLasTransacciones", "Transacciones.csv está bloqueado.", ex);
                return new List<Transaccion>();
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerTodasLasTransacciones", "Error inesperado al leer todas las transacciones.", ex);
                return new List<Transaccion>();
            }

            return transacciones;
        }

        #endregion

        #region Métodos de Persistencia - Bóveda (Inventario de Efectivo)

        /// <summary>
        /// Carga el inventario actual de billetes de la bóveda (denominaciones de 200, 100, 50, 20, 10, 5 y 1).
        /// </summary>
        /// <returns>Lista con los registros de cada billete y su cantidad. En caso de error, retorna lista vacía.</returns>
        public List<Billete> ObtenerInventarioBilletes()
        {
            var inventario = new List<Billete>();

            if (!File.Exists(_rutaBoveda))
            {
                Debug.WriteLine($"[DAL WARN] El archivo {_rutaBoveda} no existe.");
                return inventario;
            }

            try
            {
                using (StreamReader sr = new StreamReader(_rutaBoveda, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return inventario;

                    int lineaNumero = 1;
                    string? linea;

                    while ((linea = sr.ReadLine()) != null)
                    {
                        lineaNumero++;
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        string[] partes = linea.Split(',');
                        if (partes.Length < 2)
                        {
                            RegistrarAdvertencia("ObtenerInventarioBilletes", $"Línea {lineaNumero} en Boveda.csv corrupta: {linea}");
                            continue;
                        }

                        if (!int.TryParse(partes[0].Trim(), out int denominacion))
                        {
                            RegistrarAdvertencia("ObtenerInventarioBilletes", $"Denominación inválida en línea {lineaNumero}: '{partes[0]}'.");
                            continue;
                        }

                        if (!int.TryParse(partes[1].Trim(), out int cantidad))
                        {
                            RegistrarAdvertencia("ObtenerInventarioBilletes", $"Cantidad inválida en línea {lineaNumero}: '{partes[1]}'.");
                            continue;
                        }

                        inventario.Add(new Billete
                        {
                            Denominacion = denominacion,
                            Cantidad = cantidad
                        });
                    }
                }
            }
            catch (IOException ex)
            {
                RegistrarError("ObtenerInventarioBilletes", "Boveda.csv está siendo utilizado por otro programa.", ex);
                return new List<Billete>();
            }
            catch (FormatException ex)
            {
                RegistrarError("ObtenerInventarioBilletes", "Error de conversión de formato en los datos de la bóveda.", ex);
                return new List<Billete>();
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerInventarioBilletes", "Error general al consultar inventario de billetes.", ex);
                return new List<Billete>();
            }

            return inventario;
        }

        /// <summary>
        /// Sobrescribe el archivo completo de la bóveda con los nuevos valores de billetes.
        /// </summary>
        /// <param name="inventarioActualizado">Lista con las denominaciones y sus cantidades actualizadas.</param>
        /// <returns>True si la persistencia fue exitosa; False en caso de error.</returns>
        public bool GuardarInventarioBilletes(List<Billete> inventarioActualizado)
        {
            if (inventarioActualizado == null)
            {
                RegistrarAdvertencia("GuardarInventarioBilletes", "Se intentó guardar un inventario nulo.");
                return false;
            }

            try
            {
                using (StreamWriter sw = new StreamWriter(_rutaBoveda, append: false, Encoding.UTF8))
                {
                    sw.WriteLine(EncabezadoBoveda);
                    foreach (var billete in inventarioActualizado)
                    {
                        sw.WriteLine($"{billete.Denominacion},{billete.Cantidad}");
                    }
                }

                return true;
            }
            catch (IOException ex)
            {
                RegistrarError("GuardarInventarioBilletes", "No se pudo guardar el inventario: Boveda.csv está bloqueado.", ex);
                return false;
            }
            catch (FormatException ex)
            {
                RegistrarError("GuardarInventarioBilletes", "Error de formato al serializar el inventario de billetes.", ex);
                return false;
            }
            catch (Exception ex)
            {
                RegistrarError("GuardarInventarioBilletes", "Error inesperado al guardar el inventario de la bóveda.", ex);
                return false;
            }
        }

        /// <summary>
        /// Verifica si el cajero ha sido inicializado en el día actual con su carga inicial de efectivo.
        /// </summary>
        public bool EstaCajeroInicializado()
        {
            try
            {
                if (File.Exists(_rutaEstadoCajero))
                {
                    string contenido = File.ReadAllText(_rutaEstadoCajero).Trim();
                    string[] partes = contenido.Split(';');
                    if (partes.Length >= 2 && partes[0].Equals(DateTime.Today.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase))
                    {
                        return partes[1].Equals("INICIALIZADO", StringComparison.OrdinalIgnoreCase);
                    }
                }

                // Verificación secundaria en la bitácora para el día de hoy
                var registros = ObtenerBitacora();
                var ultimoEventoApertura = registros
                    .Where(r => r.FechaHora.Date == DateTime.Today && 
                                (r.TipoEvento.Equals("Inicialización Cajero", StringComparison.OrdinalIgnoreCase) ||
                                 r.TipoEvento.Equals("Reinicio de Apertura", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(r => r.FechaHora)
                    .FirstOrDefault();

                return ultimoEventoApertura != null && 
                       ultimoEventoApertura.TipoEvento.Equals("Inicialización Cajero", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Realiza la inicialización reglamentaria del cajero (monto máximo Q10,000.00 con todas las denominaciones).
        /// Solo puede realizarse una única vez al día según la normativa del proyecto.
        /// </summary>
        public bool InicializarCajero(List<Billete> billetesIniciales)
        {
            // Restricción: Solo una vez al día
            if (EstaCajeroInicializado())
            {
                throw new InvalidOperationException("El cajero ya ha sido inicializado para el día de hoy. Por normativa, la inicialización de apertura solo puede realizarse una única vez al día. Para ingresar más efectivo utilice 'Agregar Efectivo'.");
            }

            if (billetesIniciales == null || billetesIniciales.Count == 0)
            {
                throw new ArgumentException("Debe proporcionar la cantidad de billetes para cada denominación.");
            }

            // Asegurar que todas las 7 denominaciones oficiales estén representadas
            var billetesCompletos = new List<Billete>();
            foreach (int denom in DenominacionesOficiales.OrderByDescending(d => d))
            {
                var existente = billetesIniciales.FirstOrDefault(b => b.Denominacion == denom);
                int cantidad = existente != null ? Math.Max(0, existente.Cantidad) : 0;
                billetesCompletos.Add(new Billete(denom, cantidad));
            }

            decimal total = billetesCompletos.Sum(b => b.Subtotal);

            // Restricción: No puede ser más de Q. 10,000.00
            if (total > 10000m)
            {
                throw new InvalidOperationException($"El monto total de inicialización no puede ser mayor a Q10,000.00 según la normativa del proyecto (Monto ingresado: Q{total:N2}).");
            }

            if (total <= 0m)
            {
                throw new InvalidOperationException("El monto de inicialización debe ser mayor a Q0.00.");
            }

            bool guardado = GuardarInventarioBilletes(billetesCompletos);
            if (!guardado)
            {
                throw new IOException("Error al persistir la carga inicial en Boveda.csv.");
            }

            // Guardar estado de inicialización con la fecha de hoy
            try
            {
                File.WriteAllText(_rutaEstadoCajero, $"{DateTime.Today:yyyy-MM-dd};INICIALIZADO;{total:F2}");
            }
            catch (Exception ex)
            {
                RegistrarAdvertencia("InicializarCajero", $"No se pudo escribir EstadoCajero.txt: {ex.Message}");
            }

            // Registrar en bitácora administrativa
            string detalle = $"Carga inicial de apertura exitosa por Q{total:N2}. Desglose: " +
                             string.Join(", ", billetesCompletos.Where(b => b.Cantidad > 0).Select(b => $"{b.Cantidad} de Q{b.Denominacion}"));

            RegistrarEnBitacora("Inicialización Cajero", "ADMIN", detalle);
            return true;
        }

        /// <summary>
        /// Reinicia el estado de inicialización del día (útil para pruebas y calibración administrativa).
        /// </summary>
        public bool ReiniciarInicializacionDia()
        {
            try
            {
                File.WriteAllText(_rutaEstadoCajero, $"{DateTime.Today:yyyy-MM-dd};PENDIENTE;0.00");
                RegistrarEnBitacora("Reinicio de Apertura", "ADMIN", "Se reinició el estado de inicialización diaria del cajero.");
                return true;
            }
            catch (Exception ex)
            {
                RegistrarError("ReiniciarInicializacionDia", "Error al reiniciar estado del cajero.", ex);
                return false;
            }
        }

        /// <summary>
        /// Agrega un nuevo lote de efectivo a la bóveda (límite máximo acumulado Q30,000.00). Solo permitido tras ser inicializado.
        /// </summary>
        public bool AgregarEfectivoBoveda(List<Billete> billetesRecarga)
        {
            if (!EstaCajeroInicializado())
            {
                throw new InvalidOperationException("El cajero debe ser inicializado con su carga del día antes de poder agregar lotes de efectivo.");
            }

            if (billetesRecarga == null || billetesRecarga.Count == 0)
            {
                throw new ArgumentException("Debe ingresar los billetes a recargar.");
            }

            decimal totalRecarga = billetesRecarga.Sum(b => b.Subtotal);
            if (totalRecarga <= 0m)
            {
                throw new InvalidOperationException("Debe ingresar una cantidad mayor a cero billetes para recargar.");
            }

            if (totalRecarga > 30000m)
            {
                throw new InvalidOperationException($"El nuevo lote a recargar no podrá pasar los Q30,000.00 (Monto ingresado: Q{totalRecarga:N2}).");
            }

            var inventarioActual = ObtenerInventarioBilletes();
            decimal totalActual = inventarioActual.Sum(b => b.Subtotal);

            if (totalActual + totalRecarga > 30000m)
            {
                throw new InvalidOperationException($"La recarga de Q{totalRecarga:N2} excede el límite máximo permitido de Q30,000.00 en la bóveda (Actual: Q{totalActual:N2}, Resultante: Q{(totalActual + totalRecarga):N2}).");
            }

            // Sumar cantidades de billetes por cada denominación
            foreach (var recarga in billetesRecarga)
            {
                var boveda = inventarioActual.FirstOrDefault(b => b.Denominacion == recarga.Denominacion);
                if (boveda != null)
                {
                    boveda.Cantidad += Math.Max(0, recarga.Cantidad);
                }
                else if (DenominacionesOficiales.Contains(recarga.Denominacion))
                {
                    inventarioActual.Add(new Billete(recarga.Denominacion, Math.Max(0, recarga.Cantidad)));
                }
            }

            bool guardado = GuardarInventarioBilletes(inventarioActual);
            if (!guardado)
            {
                throw new IOException("Error al actualizar Boveda.csv.");
            }

            decimal nuevoTotal = inventarioActual.Sum(b => b.Subtotal);
            string detalle = $"Recarga de efectivo aprobada por Q{totalRecarga:N2}. Total disponible en bóveda: Q{nuevoTotal:N2}. Lote: " +
                             string.Join(", ", billetesRecarga.Where(b => b.Cantidad > 0).Select(b => $"{b.Cantidad} de Q{b.Denominacion}"));

            RegistrarEnBitacora("Recarga de Efectivo", "ADMIN", detalle);
            return true;
        }

        #endregion

        #region Bitácora Administrativa y Control

        /// <summary>
        /// Registra un evento de auditoría en Bitacora.csv.
        /// </summary>
        public bool RegistrarEnBitacora(string tipoEvento, string usuarioOTarjeta, string detalle)
        {
            try
            {
                string linea = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3}",
                    DateTime.Now,
                    (tipoEvento ?? string.Empty).Trim().Replace(',', ';'),
                    (usuarioOTarjeta ?? string.Empty).Trim().Replace(',', ';'),
                    (detalle ?? string.Empty).Trim().Replace(',', ';')
                );

                using (StreamWriter sw = new StreamWriter(_rutaBitacora, append: true, Encoding.UTF8))
                {
                    sw.WriteLine(linea);
                }

                return true;
            }
            catch (Exception ex)
            {
                RegistrarError("RegistrarEnBitacora", "Error al escribir en Bitacora.csv.", ex);
                return false;
            }
        }

        /// <summary>
        /// Obtiene todos los registros almacenados en Bitacora.csv ordenados de forma descendente.
        /// </summary>
        public List<RegistroBitacora> ObtenerBitacora()
        {
            var registros = new List<RegistroBitacora>();
            if (!File.Exists(_rutaBitacora)) return registros;

            try
            {
                using (StreamReader sr = new StreamReader(_rutaBitacora, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return registros;

                    string? linea;
                    while ((linea = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        string[] partes = linea.Split(',');
                        if (partes.Length < 4) continue;

                        if (!DateTime.TryParse(partes[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fechaHora))
                        {
                            if (!DateTime.TryParse(partes[0].Trim(), out fechaHora)) continue;
                        }

                        registros.Add(new RegistroBitacora
                        {
                            FechaHora = fechaHora,
                            TipoEvento = partes[1].Trim(),
                            UsuarioOTarjeta = partes[2].Trim(),
                            Detalle = partes[3].Trim()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerBitacora", "Error al leer Bitacora.csv.", ex);
            }

            return registros.OrderByDescending(r => r.FechaHora).ToList();
        }

        /// <summary>
        /// Modifica el número de tarjeta para un usuario registrando el cambio en la bitácora
        /// para mantener el control de límite diario acumulado.
        /// </summary>
        public bool ModificarNumeroTarjeta(string tarjetaActual, string nuevaTarjeta)
        {
            if (string.IsNullOrWhiteSpace(tarjetaActual) || string.IsNullOrWhiteSpace(nuevaTarjeta))
            {
                throw new ArgumentException("Los números de tarjeta no pueden estar vacíos.");
            }

            tarjetaActual = tarjetaActual.Trim();
            nuevaTarjeta = nuevaTarjeta.Trim();

            if (nuevaTarjeta.Length != 16 || !nuevaTarjeta.All(char.IsDigit))
            {
                throw new ArgumentException("El nuevo número de tarjeta debe tener exactamente 16 dígitos numéricos.");
            }

            if (tarjetaActual.Equals(nuevaTarjeta, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("El nuevo número de tarjeta no puede ser igual al actual.");
            }

            var usuarios = ObtenerTodosLosUsuarios();
            var usuario = usuarios.FirstOrDefault(u => u.NumeroTarjeta.Equals(tarjetaActual, StringComparison.OrdinalIgnoreCase));
            if (usuario == null)
            {
                throw new InvalidOperationException($"No se encontró ningún usuario con la tarjeta {tarjetaActual}.");
            }

            if (usuarios.Any(u => u.NumeroTarjeta.Equals(nuevaTarjeta, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"La tarjeta {nuevaTarjeta} ya está registrada en el sistema.");
            }

            usuario.NumeroTarjeta = nuevaTarjeta;
            bool exito = GuardarTodosLosUsuarios(usuarios);

            if (exito)
            {
                RegistrarEnBitacora("Cambio de Tarjeta", nuevaTarjeta, $"Tarjeta actualizada. Tarjeta previa: {tarjetaActual}. Titular: {usuario.Nombre}.");
            }

            return exito;
        }

        /// <summary>
        /// Retorna la lista de todas las tarjetas (actual y anteriores) asociadas a un usuario según los registros de la bitácora.
        /// </summary>
        public List<string> ObtenerTarjetasVinculadas(string tarjeta)
        {
            var vinculadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(tarjeta)) return vinculadas.ToList();

            vinculadas.Add(tarjeta.Trim());

            try
            {
                var bitacora = ObtenerBitacora();
                bool cambio;

                // Propagación transitiva por si hubo múltiples cambios de tarjeta en cadena
                do
                {
                    cambio = false;
                    foreach (var reg in bitacora.Where(r => r.TipoEvento.Equals("Cambio de Tarjeta", StringComparison.OrdinalIgnoreCase)))
                    {
                        string tarjReg = reg.UsuarioOTarjeta.Trim();
                        // El detalle contiene: "Tarjeta previa: {tarjetaVieja}"
                        int idx = reg.Detalle.IndexOf("Tarjeta previa: ", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            string resto = reg.Detalle.Substring(idx + 16);
                            int puntoIdx = resto.IndexOf('.');
                            string tarjPrevia = (puntoIdx >= 0 ? resto.Substring(0, puntoIdx) : resto).Trim();

                            if (vinculadas.Contains(tarjReg) && !vinculadas.Contains(tarjPrevia))
                            {
                                vinculadas.Add(tarjPrevia);
                                cambio = true;
                            }
                            else if (vinculadas.Contains(tarjPrevia) && !vinculadas.Contains(tarjReg))
                            {
                                vinculadas.Add(tarjReg);
                                cambio = true;
                            }
                        }
                    }
                } while (cambio);
            }
            catch (Exception ex)
            {
                RegistrarAdvertencia("ObtenerTarjetasVinculadas", $"Error al rastrear tarjetas vinculadas: {ex.Message}");
            }

            return vinculadas.ToList();
        }

        /// <summary>
        /// Retorna la fecha y hora del último acceso/login exitoso para una tarjeta o sus tarjetas vinculadas.
        /// </summary>
        public DateTime? ObtenerUltimoAcceso(string tarjeta)
        {
            var tarjetas = ObtenerTarjetasVinculadas(tarjeta);
            var bitacora = ObtenerBitacora();

            var acceso = bitacora.FirstOrDefault(r => (r.TipoEvento.Equals("Inicio de Sesión", StringComparison.OrdinalIgnoreCase) ||
                                                      r.TipoEvento.Equals("Acceso Cajero", StringComparison.OrdinalIgnoreCase)) &&
                                                     tarjetas.Contains(r.UsuarioOTarjeta.Trim()));

            return acceso?.FechaHora;
        }

        /// <summary>
        /// Retorna el último registro de acceso de cualquier usuario al cajero.
        /// </summary>
        public RegistroBitacora? ObtenerUltimoAccesoGeneral()
        {
            var bitacora = ObtenerBitacora();
            return bitacora.FirstOrDefault(r => r.TipoEvento.Equals("Inicio de Sesión", StringComparison.OrdinalIgnoreCase) ||
                                                r.TipoEvento.Equals("Acceso Cajero", StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Registro de Errores y Advertencias

        /// <summary>
        /// Registra un error capturado en los flujos de lectura y escritura.
        /// Escribe tanto en la ventana de depuración (Debug) como en la consola estándar.
        /// </summary>
        private static void RegistrarError(string metodo, string contexto, Exception ex)
        {
            string mensaje = $"[DAL ERROR] {metodo}: {contexto} | Detalle: {ex.Message}";
            Debug.WriteLine(mensaje);
            Console.WriteLine(mensaje);
        }

        /// <summary>
        /// Registra una advertencia ante datos inconsistentes que no detienen la ejecución.
        /// </summary>
        private static void RegistrarAdvertencia(string metodo, string mensaje)
        {
            string log = $"[DAL WARN] {metodo}: {mensaje}";
            Debug.WriteLine(log);
            Console.WriteLine(log);
        }

        #endregion
    }
}
