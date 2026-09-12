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

        private const string EncabezadoUsuarios = "NumeroTarjeta,PIN,Nombre,SaldoActual,LimiteDiario";
        private const string EncabezadoTransacciones = "NumeroTarjeta,FechaHora,TipoMovimiento,Monto";
        private const string EncabezadoBoveda = "Denominacion,Cantidad";

        // Denominaciones de billetes oficiales del proyecto: 200, 100, 50, 20, 10, 5 y 1.
        private static readonly int[] DenominacionesOficiales = [200, 100, 50, 20, 10, 5, 1];

        #endregion

        #region Constructor e Inicialización

        /// <summary>
        /// Inicializa una nueva instancia de <see cref="GestorArchivosCSV"/>.
        /// Verifica la existencia de los archivos necesarios (Usuarios.csv, Transacciones.csv y Boveda.csv)
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
                // Formato ISO estándar para fecha y hora, y punto decimal invariable para el monto
                string linea = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1:yyyy-MM-dd HH:mm:ss},{2},{3:F2}",
                    t.NumeroTarjeta.Trim(),
                    t.FechaHora,
                    t.TipoMovimiento.Trim(),
                    t.Monto
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

                        historial.Add(new Transaccion
                        {
                            NumeroTarjeta = tarjetaReg,
                            FechaHora = fechaHora,
                            TipoMovimiento = partes[2].Trim(),
                            Monto = monto
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

                        transacciones.Add(new Transaccion
                        {
                            NumeroTarjeta = partes[0].Trim(),
                            FechaHora = fechaHora,
                            TipoMovimiento = partes[2].Trim(),
                            Monto = monto
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
