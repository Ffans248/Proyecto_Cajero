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
        private readonly string _rutaCajerosRed;
        private readonly string _rutaBancosRed;
        private readonly string _rutaCuentasExternas;
        private readonly string _rutaTransferenciasNuestroBanco;
        private readonly string _rutaTransferenciasOtroBanco;

        private const string EncabezadoUsuarios = "NumeroTarjeta,PIN,Nombre,SaldoActual,LimiteDiario";
        private const string EncabezadoTransacciones = "NumeroTarjeta,FechaHora,TipoMovimiento,Monto,Token";
        private const string EncabezadoBoveda = "Denominacion,Cantidad";
        private const string EncabezadoBitacora = "FechaHora,TipoEvento,UsuarioOTarjeta,Detalle";
        private const string EncabezadoCajerosRed = "CodigoCajero,NombreCajero,CodigoBanco,IP,Puerto,EsLocal,EnLinea,EfectivoActual,UltimaConexion";
        private const string EncabezadoBancosRed = "CodigoBanco,NombreBanco,IP,Puerto,EsPropio,LimiteDiario,TransferidoHoy,FechaUltimoReinicio";
        private const string EncabezadoCuentasExternas = "CodigoBanco,NumeroCuenta,NombreTitular,SaldoActual,Activa";
        private const string EncabezadoTransferencias = "TokenTransferencia,FechaHora,TipoTransferencia,BancoOrigen,CuentaOrigen,NombreOrigen,BancoDestino,CuentaDestino,NombreDestino,Monto,Concepto,CupoRestanteOrigen,CupoRestanteDestino,Estado";

        // Denominaciones de billetes oficiales del proyecto: 200, 100, 50, 20, 10, 5 y 1.
        private static readonly int[] DenominacionesOficiales = [200, 100, 50, 20, 10, 5, 1];

        #endregion

        #region Constructor e Inicialización

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
            _rutaCajerosRed = Path.Combine(_rutaDirectorio, "CajerosRed.csv");
            _rutaBancosRed = Path.Combine(_rutaDirectorio, "BancosRed.csv");
            _rutaCuentasExternas = Path.Combine(_rutaDirectorio, "CuentasExternas.csv");
            _rutaTransferenciasNuestroBanco = Path.Combine(_rutaDirectorio, "TransferenciasNuestroBanco.csv");
            _rutaTransferenciasOtroBanco = Path.Combine(_rutaDirectorio, "TransferenciasOtroBanco.csv");

            InicializarArchivos();
        }

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
                        sw.WriteLine("1111222233334444,1234,Carlos Mendez,8000.00,3000.00");
                        sw.WriteLine("2222333344445555,1234,Ana Lopez,6500.00,2500.00");
                    }
                    Debug.WriteLine($"[DAL INFO] Archivo creado: {_rutaUsuarios}");
                }
                else
                {
                    // Asegurar que haya al menos un segundo usuario cliente para probar transferencias entre usuarios
                    var actuales = ObtenerTodosLosUsuarios();
                    bool mod = false;
                    if (!actuales.Any(u => u.NumeroTarjeta == "1111222233334444"))
                    {
                        actuales.Add(new Usuario("1111222233334444", "1234", "Carlos Mendez", 8000.00m, 3000.00m));
                        mod = true;
                    }
                    if (!actuales.Any(u => u.NumeroTarjeta == "2222333344445555"))
                    {
                        actuales.Add(new Usuario("2222333344445555", "1234", "Ana Lopez", 6500.00m, 2500.00m));
                        mod = true;
                    }
                    if (mod) GuardarTodosLosUsuarios(actuales);
                }

                // 2. Inicialización de Transacciones.csv
                if (!File.Exists(_rutaTransacciones))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaTransacciones, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoTransacciones);
                    }
                }

                // 3. Inicialización de Boveda.csv
                if (!File.Exists(_rutaBoveda))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaBoveda, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoBoveda);
                        foreach (int denominacion in DenominacionesOficiales)
                        {
                            sw.WriteLine($"{denominacion},0");
                        }
                    }
                }

                // 4. Inicialización de Bitacora.csv
                if (!File.Exists(_rutaBitacora))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaBitacora, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoBitacora);
                    }
                }

                // 5. Inicialización de BancosRed.csv (Límites coordinados entre bancos)
                if (!File.Exists(_rutaBancosRed))
                {
                    string hoy = DateTime.Today.ToString("yyyy-MM-dd");
                    using (StreamWriter sw = new StreamWriter(_rutaBancosRed, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoBancosRed);
                        sw.WriteLine($"BANC01,NEXUSBANK (Nuestro Banco),127.0.0.1,5000,true,25000.00,0.00,{hoy}");
                        sw.WriteLine($"BANC02,Banco Industrial Estudiantil,192.168.1.102,5000,false,25000.00,0.00,{hoy}");
                        sw.WriteLine($"BANC03,Banrural Red Universitaria,192.168.1.103,5000,false,20000.00,0.00,{hoy}");
                        sw.WriteLine($"BANC04,Banco G&T Interbancario,192.168.1.104,5000,false,20000.00,0.00,{hoy}");
                    }
                }

                // 6. Inicialización de CuentasExternas.csv (Cuentas verificables de otros bancos)
                if (!File.Exists(_rutaCuentasExternas))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaCuentasExternas, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoCuentasExternas);
                        sw.WriteLine("BANC02,2002000001,Roberto Gomez (Banco 2),12500.00,true");
                        sw.WriteLine("BANC02,2002000002,Andrea Morales (Banco 2),8400.00,true");
                        sw.WriteLine("BANC03,3003000001,Fernando Castillo (Banco 3),15200.00,true");
                        sw.WriteLine("BANC03,3003000002,Sofia Herrera (Banco 3),9100.00,true");
                        sw.WriteLine("BANC04,4004000001,Diego Ramirez (Banco 4),18000.00,true");
                    }
                }

                // 7. Inicialización de Tablas Comunes de Transferencias (Nuestro Banco + Otro Banco)
                if (!File.Exists(_rutaTransferenciasNuestroBanco))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaTransferenciasNuestroBanco, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoTransferencias);
                    }
                }

                if (!File.Exists(_rutaTransferenciasOtroBanco))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaTransferenciasOtroBanco, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoTransferencias);
                    }
                }

                // 8. Inicialización de Cajeros de la Red que consultan la BD Maestra
                if (!File.Exists(_rutaCajerosRed))
                {
                    using (StreamWriter sw = new StreamWriter(_rutaCajerosRed, append: false, Encoding.UTF8))
                    {
                        sw.WriteLine(EncabezadoCajerosRed);
                        sw.WriteLine($"ATM-01,Cajero Central Zona 1 (Maestro Local),BANC01,127.0.0.1,5000,true,true,10000.00,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        sw.WriteLine($"ATM-02,Cajero Sucursal Norte,BANC01,10.219.19.12,5000,false,false,10000.00,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        sw.WriteLine($"ATM-03,Cajero Campus Central,BANC02,10.219.19.13,5000,false,false,10000.00,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        sw.WriteLine($"ATM-04,Cajero Plaza Sur,BANC03,10.219.19.14,5000,false,false,10000.00,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        sw.WriteLine($"ATM-05,Cajero Centro Comercial,BANC04,10.219.19.15,5000,false,false,10000.00,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    }
                }
            }
            catch (IOException ex)
            {
                RegistrarError("InicializarArchivos", "El archivo está bloqueado por otro proceso.", ex);
            }
            catch (Exception ex)
            {
                RegistrarError("InicializarArchivos", "Ocurrió un error inesperado al inicializar los archivos.", ex);
            }
        }

        #endregion

        #region Métodos de Persistencia - Usuarios

        public List<Usuario> ObtenerTodosLosUsuarios()
        {
            var usuarios = new List<Usuario>();
            if (!File.Exists(_rutaUsuarios)) return usuarios;

            try
            {
                using (StreamReader sr = new StreamReader(_rutaUsuarios, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return usuarios;

                    int lineaNumero = 1;
                    string? linea;

                    while ((linea = sr.ReadLine()) != null)
                    {
                        lineaNumero++;
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        string[] partes = linea.Split(',');
                        if (partes.Length < 5) continue;

                        string tarjeta = partes[0].Trim();
                        string pin = partes[1].Trim();
                        string nombre = partes[2].Trim();

                        if (!decimal.TryParse(partes[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal saldo)) continue;
                        if (!decimal.TryParse(partes[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal limite)) continue;

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
            catch (Exception ex)
            {
                RegistrarError("ObtenerTodosLosUsuarios", "Error al leer los usuarios.", ex);
            }

            return usuarios;
        }

        public Usuario? ObtenerUsuarioPorTarjeta(string tarjeta)
        {
            if (string.IsNullOrWhiteSpace(tarjeta)) return null;
            if (!File.Exists(_rutaUsuarios)) return null;

            try
            {
                var todos = ObtenerTodosLosUsuarios();
                string buscada = tarjeta.Trim();

                // Coincidencia exacta por Número de Tarjeta (16 dígitos)
                var exacto = todos.FirstOrDefault(u => u.NumeroTarjeta.Equals(buscada, StringComparison.OrdinalIgnoreCase));
                if (exacto != null) return exacto;

                // También permitir búsqueda por número de cuenta corto (ej. 1001000001..1001000009) para transferencias internas
                for (int i = 0; i < todos.Count; i++)
                {
                    string cuentaGenerada = $"1001{(i + 1):D6}";
                    if (cuentaGenerada.Equals(buscada, StringComparison.OrdinalIgnoreCase))
                    {
                        return todos[i];
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerUsuarioPorTarjeta", "Error al buscar el usuario por tarjeta.", ex);
                return null;
            }
        }

        public bool CrearNuevoUsuario(Usuario nuevo)
        {
            if (nuevo == null || string.IsNullOrWhiteSpace(nuevo.NumeroTarjeta)) return false;

            try
            {
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
            catch (Exception ex)
            {
                RegistrarError("CrearNuevoUsuario", "Error al crear el usuario en el archivo.", ex);
                return false;
            }
        }

        public bool ActualizarSaldo(string tarjeta, decimal nuevoSaldo)
        {
            if (string.IsNullOrWhiteSpace(tarjeta)) return false;

            try
            {
                var usuarios = ObtenerTodosLosUsuarios();
                var usuarioEncontrado = usuarios.FirstOrDefault(u => u.NumeroTarjeta.Equals(tarjeta.Trim(), StringComparison.OrdinalIgnoreCase));

                if (usuarioEncontrado == null) return false;

                usuarioEncontrado.SaldoActual = nuevoSaldo;
                return GuardarTodosLosUsuarios(usuarios);
            }
            catch (Exception ex)
            {
                RegistrarError("ActualizarSaldo", "Error al actualizar el saldo del usuario.", ex);
                return false;
            }
        }

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

                if (index < 0) return false;

                usuarios[index] = usuarioActualizado;
                return GuardarTodosLosUsuarios(usuarios);
            }
            catch (Exception ex)
            {
                RegistrarError("ActualizarUsuario", "Error al actualizar el registro del usuario.", ex);
                return false;
            }
        }

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
            catch (Exception ex)
            {
                RegistrarError("GuardarTodosLosUsuarios", "Error al guardar la lista de usuarios.", ex);
                return false;
            }
        }

        #endregion

        #region Métodos de Persistencia - Transacciones

        public bool RegistrarTransaccion(Transaccion t)
        {
            if (t == null) return false;

            try
            {
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
            catch (Exception ex)
            {
                RegistrarError("RegistrarTransaccion", "Error al registrar la transacción.", ex);
                return false;
            }
        }

        public List<Transaccion> ObtenerHistorial(string tarjeta)
        {
            var historial = new List<Transaccion>();
            if (string.IsNullOrWhiteSpace(tarjeta) || !File.Exists(_rutaTransacciones)) return historial;

            try
            {
                using (StreamReader sr = new StreamReader(_rutaTransacciones, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return historial;

                    string tarjetaFiltro = tarjeta.Trim();
                    string? linea;

                    while ((linea = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        string[] partes = linea.Split(',');
                        if (partes.Length < 4) continue;

                        string tarjetaReg = partes[0].Trim();
                        if (!tarjetaReg.Equals(tarjetaFiltro, StringComparison.OrdinalIgnoreCase)) continue;

                        if (!DateTime.TryParse(partes[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fechaHora))
                        {
                            if (!DateTime.TryParse(partes[1].Trim(), out fechaHora)) continue;
                        }

                        if (!decimal.TryParse(partes[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto)) continue;

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
            catch (Exception ex)
            {
                RegistrarError("ObtenerHistorial", "Error al consultar el historial de transacciones.", ex);
            }

            return historial;
        }

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

                    string? linea;
                    while ((linea = sr.ReadLine()) != null)
                    {
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
            catch (Exception ex)
            {
                RegistrarError("ObtenerTodasLasTransacciones", "Error al leer todas las transacciones.", ex);
            }

            return transacciones;
        }

        #endregion

        #region Métodos de Persistencia - Bóveda (Inventario de Efectivo)

        public List<Billete> ObtenerInventarioBilletes()
        {
            var inventario = new List<Billete>();
            if (!File.Exists(_rutaBoveda)) return inventario;

            try
            {
                using (StreamReader sr = new StreamReader(_rutaBoveda, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return inventario;

                    string? linea;
                    while ((linea = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(linea)) continue;

                        string[] partes = linea.Split(',');
                        if (partes.Length < 2) continue;

                        if (!int.TryParse(partes[0].Trim(), out int denominacion)) continue;
                        if (!int.TryParse(partes[1].Trim(), out int cantidad)) continue;

                        inventario.Add(new Billete
                        {
                            Denominacion = denominacion,
                            Cantidad = cantidad
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerInventarioBilletes", "Error al consultar inventario de billetes.", ex);
            }

            return inventario;
        }

        public bool GuardarInventarioBilletes(List<Billete> inventarioActualizado)
        {
            if (inventarioActualizado == null) return false;

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
            catch (Exception ex)
            {
                RegistrarError("GuardarInventarioBilletes", "Error al guardar el inventario de la bóveda.", ex);
                return false;
            }
        }

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

        public bool InicializarCajero(List<Billete> billetesIniciales)
        {
            if (EstaCajeroInicializado())
            {
                throw new InvalidOperationException("El cajero ya ha sido inicializado para el día de hoy. Por normativa, la inicialización de apertura solo puede realizarse una única vez al día. Para ingresar más efectivo utilice 'Agregar Efectivo'.");
            }

            if (billetesIniciales == null || billetesIniciales.Count == 0)
            {
                throw new ArgumentException("Debe proporcionar la cantidad de billetes para cada denominación.");
            }

            var billetesCompletos = new List<Billete>();
            foreach (int denom in DenominacionesOficiales.OrderByDescending(d => d))
            {
                var existente = billetesIniciales.FirstOrDefault(b => b.Denominacion == denom);
                int cantidad = existente != null ? Math.Max(0, existente.Cantidad) : 0;
                billetesCompletos.Add(new Billete(denom, cantidad));
            }

            decimal total = billetesCompletos.Sum(b => b.Subtotal);

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

            try
            {
                File.WriteAllText(_rutaEstadoCajero, $"{DateTime.Today:yyyy-MM-dd};INICIALIZADO;{total:F2}");
            }
            catch { }

            string detalle = $"Carga inicial de apertura exitosa por Q{total:N2}. Desglose: " +
                             string.Join(", ", billetesCompletos.Where(b => b.Cantidad > 0).Select(b => $"{b.Cantidad} de Q{b.Denominacion}"));

            RegistrarEnBitacora("Inicialización Cajero", "ADMIN", detalle);
            return true;
        }

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

        public List<string> ObtenerTarjetasVinculadas(string tarjeta)
        {
            var vinculadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(tarjeta)) return vinculadas.ToList();

            vinculadas.Add(tarjeta.Trim());

            try
            {
                var bitacora = ObtenerBitacora();
                bool cambio;

                do
                {
                    cambio = false;
                    foreach (var reg in bitacora.Where(r => r.TipoEvento.Equals("Cambio de Tarjeta", StringComparison.OrdinalIgnoreCase)))
                    {
                        string tarjReg = reg.UsuarioOTarjeta.Trim();
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
            catch { }

            return vinculadas.ToList();
        }

        public DateTime? ObtenerUltimoAcceso(string tarjeta)
        {
            var tarjetas = ObtenerTarjetasVinculadas(tarjeta);
            var bitacora = ObtenerBitacora();

            var acceso = bitacora.FirstOrDefault(r => (r.TipoEvento.Equals("Inicio de Sesión", StringComparison.OrdinalIgnoreCase) ||
                                                      r.TipoEvento.Equals("Acceso Cajero", StringComparison.OrdinalIgnoreCase)) &&
                                                     tarjetas.Contains(r.UsuarioOTarjeta.Trim()));

            return acceso?.FechaHora;
        }

        public RegistroBitacora? ObtenerUltimoAccesoGeneral()
        {
            var bitacora = ObtenerBitacora();
            return bitacora.FirstOrDefault(r => r.TipoEvento.Equals("Inicio de Sesión", StringComparison.OrdinalIgnoreCase) ||
                                                r.TipoEvento.Equals("Acceso Cajero", StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Red Interbancaria, Verificación de Cuentas y Registro de Tokens

        public List<CajeroRedInfo> ObtenerCajerosRed()
        {
            var lista = new List<CajeroRedInfo>();
            if (!File.Exists(_rutaCajerosRed)) return lista;

            try
            {
                using (StreamReader sr = new StreamReader(_rutaCajerosRed, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return lista;

                    string? linea;
                    while ((linea = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(linea)) continue;
                        string[] p = linea.Split(',');
                        if (p.Length < 9) continue;

                        int.TryParse(p[4].Trim(), out int pto);
                        bool.TryParse(p[5].Trim(), out bool esLoc);
                        bool.TryParse(p[6].Trim(), out bool enLin);
                        decimal.TryParse(p[7].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal efec);
                        DateTime.TryParse(p[8].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime ultCon);

                        lista.Add(new CajeroRedInfo
                        {
                            CodigoCajero = p[0].Trim(),
                            NombreCajero = p[1].Trim(),
                            CodigoBanco = p[2].Trim(),
                            IP = p[3].Trim(),
                            Puerto = pto > 0 ? pto : 5000,
                            EsLocal = esLoc,
                            EnLinea = esLoc || enLin,
                            EfectivoActual = efec,
                            UltimaConexionBdMaestra = ultCon == default ? DateTime.Now : ultCon
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerCajerosRed", "Error al leer CajerosRed.csv.", ex);
            }

            return lista;
        }

        public bool GuardarCajerosRed(List<CajeroRedInfo> cajeros)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(_rutaCajerosRed, append: false, Encoding.UTF8))
                {
                    sw.WriteLine(EncabezadoCajerosRed);
                    foreach (var c in cajeros)
                    {
                        sw.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3},{4},{5},{6},{7:F2},{8:yyyy-MM-dd HH:mm:ss}",
                            c.CodigoCajero.Trim(),
                            c.NombreCajero.Trim().Replace(',', ' '),
                            c.CodigoBanco.Trim(),
                            c.IP.Trim(),
                            c.Puerto,
                            c.EsLocal ? "true" : "false",
                            (c.EsLocal || c.EnLinea) ? "true" : "false",
                            c.EfectivoActual,
                            c.UltimaConexionBdMaestra
                        ));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                RegistrarError("GuardarCajerosRed", "Error al guardar CajerosRed.csv.", ex);
                return false;
            }
        }

        public List<BancoRedInfo> ObtenerBancosRed()
        {
            var lista = new List<BancoRedInfo>();
            if (!File.Exists(_rutaBancosRed)) return lista;

            try
            {
                bool requiereGuardadoPorCambioDia = false;
                using (StreamReader sr = new StreamReader(_rutaBancosRed, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return lista;

                    string? linea;
                    while ((linea = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(linea)) continue;
                        string[] p = linea.Split(',');
                        if (p.Length < 8) continue;

                        decimal.TryParse(p[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal lim);
                        decimal.TryParse(p[6].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal transf);
                        int.TryParse(p[3].Trim(), out int pto);
                        bool.TryParse(p[4].Trim(), out bool propio);
                        DateTime.TryParse(p[7].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fechaReset);

                        if (fechaReset.Date < DateTime.Today)
                        {
                            transf = 0m;
                            fechaReset = DateTime.Today;
                            requiereGuardadoPorCambioDia = true;
                        }

                        bool enLinea = propio || (p.Length >= 9 && bool.TryParse(p[8].Trim(), out bool el) && el);

                        lista.Add(new BancoRedInfo
                        {
                            CodigoBanco = p[0].Trim(),
                            NombreBanco = p[1].Trim(),
                            IP = p[2].Trim(),
                            Puerto = pto > 0 ? pto : 5000,
                            EsPropio = propio,
                            EnLinea = enLinea,
                            LimiteDiario = lim > 0 ? lim : 25000m,
                            TransferidoHoy = transf,
                            FechaUltimoReinicio = fechaReset
                        });
                    }
                }

                if (requiereGuardadoPorCambioDia)
                {
                    GuardarBancosRed(lista);
                }
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerBancosRed", "Error al leer BancosRed.csv.", ex);
            }

            return lista;
        }

        public bool GuardarBancosRed(List<BancoRedInfo> bancos)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(_rutaBancosRed, append: false, Encoding.UTF8))
                {
                    sw.WriteLine(EncabezadoBancosRed);
                    foreach (var b in bancos)
                    {
                        sw.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3},{4},{5:F2},{6:F2},{7:yyyy-MM-dd},{8}",
                            b.CodigoBanco.Trim(),
                            b.NombreBanco.Trim().Replace(',', ' '),
                            b.IP.Trim(),
                            b.Puerto,
                            b.EsPropio ? "true" : "false",
                            b.LimiteDiario,
                            b.TransferidoHoy,
                            b.FechaUltimoReinicio,
                            (b.EsPropio || b.EnLinea) ? "true" : "false"
                        ));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                RegistrarError("GuardarBancosRed", "Error al guardar BancosRed.csv.", ex);
                return false;
            }
        }

        public bool ReiniciarCuposDiariosBancos()
        {
            var bancos = ObtenerBancosRed();
            foreach (var b in bancos)
            {
                b.TransferidoHoy = 0m;
                b.FechaUltimoReinicio = DateTime.Today;
            }
            bool ok = GuardarBancosRed(bancos);
            if (ok)
            {
                RegistrarEnBitacora("Reinicio Cupos Bancos", "ADMIN", "Se reiniciaron los límites diarios coordinados de todos los bancos a Q 0.00 transferidos hoy.");
            }
            return ok;
        }

        public List<CuentaExternaInfo> ObtenerCuentasExternas(string? codigoBanco = null)
        {
            var lista = new List<CuentaExternaInfo>();
            if (!File.Exists(_rutaCuentasExternas)) return lista;

            try
            {
                using (StreamReader sr = new StreamReader(_rutaCuentasExternas, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return lista;

                    string? linea;
                    while ((linea = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(linea)) continue;
                        string[] p = linea.Split(',');
                        if (p.Length < 4) continue;

                        string codB = p[0].Trim();
                        if (!string.IsNullOrWhiteSpace(codigoBanco) && !codB.Equals(codigoBanco.Trim(), StringComparison.OrdinalIgnoreCase))
                            continue;

                        decimal.TryParse(p[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal saldo);
                        bool activa = p.Length < 5 || !bool.TryParse(p[4].Trim(), out bool act) || act;

                        lista.Add(new CuentaExternaInfo
                        {
                            CodigoBanco = codB,
                            NumeroCuenta = p[1].Trim(),
                            NombreTitular = p[2].Trim(),
                            SaldoActual = saldo,
                            Activa = activa
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerCuentasExternas", "Error al leer CuentasExternas.csv.", ex);
            }

            return lista;
        }

        public bool GuardarCuentaExterna(CuentaExternaInfo cuenta)
        {
            try
            {
                var todas = ObtenerCuentasExternas();
                int idx = todas.FindIndex(c =>
                    c.CodigoBanco.Equals(cuenta.CodigoBanco.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    c.NumeroCuenta.Equals(cuenta.NumeroCuenta.Trim(), StringComparison.OrdinalIgnoreCase));

                if (idx >= 0)
                    todas[idx] = cuenta;
                else
                    todas.Add(cuenta);

                using (StreamWriter sw = new StreamWriter(_rutaCuentasExternas, append: false, Encoding.UTF8))
                {
                    sw.WriteLine(EncabezadoCuentasExternas);
                    foreach (var c in todas)
                    {
                        sw.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3:F2},{4}",
                            c.CodigoBanco.Trim().ToUpperInvariant(),
                            c.NumeroCuenta.Trim(),
                            c.NombreTitular.Trim().Replace(',', ' '),
                            c.SaldoActual,
                            c.Activa ? "true" : "false"
                        ));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                RegistrarError("GuardarCuentaExterna", "Error al guardar CuentasExternas.csv.", ex);
                return false;
            }
        }

        /// <summary>
        /// Guarda el TokenTransferencia tanto en el registro de Nuestro Banco (TransferenciasNuestroBanco.csv)
        /// como en el registro sincronizado del Otro Banco (TransferenciasOtroBanco.csv).
        /// </summary>
        public bool RegistrarTransferenciaCompartida(RegistroTransferenciaBancaria t)
        {
            try
            {
                string linea = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1:yyyy-MM-dd HH:mm:ss},{2},{3},{4},{5},{6},{7},{8},{9:F2},{10},{11:F2},{12:F2},{13}",
                    t.TokenTransferencia.Trim(),
                    t.FechaHora,
                    t.TipoTransferencia.Trim(),
                    t.BancoOrigen.Trim(),
                    t.CuentaOrigen.Trim(),
                    t.NombreOrigen.Trim().Replace(',', ' '),
                    t.BancoDestino.Trim(),
                    t.CuentaDestino.Trim(),
                    t.NombreDestino.Trim().Replace(',', ' '),
                    t.Monto,
                    (t.Concepto ?? "Transferencia").Trim().Replace(',', ' '),
                    t.CupoRestanteBancoOrigen,
                    t.CupoRestanteBancoDestino,
                    t.Estado.Trim()
                );

                // 1. Guardar en tabla de nuestro banco
                using (StreamWriter sw1 = new StreamWriter(_rutaTransferenciasNuestroBanco, append: true, Encoding.UTF8))
                {
                    sw1.WriteLine(linea);
                }

                // 2. Guardar también el mismo token en la tabla espejo del otro banco (coordinación interbancaria)
                using (StreamWriter sw2 = new StreamWriter(_rutaTransferenciasOtroBanco, append: true, Encoding.UTF8))
                {
                    sw2.WriteLine(linea);
                }

                return true;
            }
            catch (Exception ex)
            {
                RegistrarError("RegistrarTransferenciaCompartida", "Error al guardar el registro de transferencia.", ex);
                return false;
            }
        }

        public List<RegistroTransferenciaBancaria> ObtenerTransferenciasCompartidas()
        {
            var lista = new List<RegistroTransferenciaBancaria>();
            if (!File.Exists(_rutaTransferenciasNuestroBanco)) return lista;

            try
            {
                using (StreamReader sr = new StreamReader(_rutaTransferenciasNuestroBanco, Encoding.UTF8))
                {
                    string? encabezado = sr.ReadLine();
                    if (encabezado == null) return lista;

                    string? linea;
                    while ((linea = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(linea)) continue;
                        string[] p = linea.Split(',');
                        if (p.Length < 14) continue;

                        DateTime.TryParse(p[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha);
                        decimal.TryParse(p[9].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto);
                        decimal.TryParse(p[11].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cupoOrig);
                        decimal.TryParse(p[12].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cupoDest);

                        lista.Add(new RegistroTransferenciaBancaria
                        {
                            TokenTransferencia = p[0].Trim(),
                            FechaHora = fecha,
                            TipoTransferencia = p[2].Trim(),
                            BancoOrigen = p[3].Trim(),
                            CuentaOrigen = p[4].Trim(),
                            NombreOrigen = p[5].Trim(),
                            BancoDestino = p[6].Trim(),
                            CuentaDestino = p[7].Trim(),
                            NombreDestino = p[8].Trim(),
                            Monto = monto,
                            Concepto = p[10].Trim(),
                            CupoRestanteBancoOrigen = cupoOrig,
                            CupoRestanteBancoDestino = cupoDest,
                            Estado = p[13].Trim()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                RegistrarError("ObtenerTransferenciasCompartidas", "Error al leer TransferenciasNuestroBanco.csv.", ex);
            }

            return lista.OrderByDescending(x => x.FechaHora).ToList();
        }

        #endregion

        #region Registro de Errores y Advertencias

        private static void RegistrarError(string metodo, string contexto, Exception ex)
        {
            string mensaje = $"[DAL ERROR] {metodo}: {contexto} | Detalle: {ex.Message}";
            Debug.WriteLine(mensaje);
            Console.WriteLine(mensaje);
        }

        private static void RegistrarAdvertencia(string metodo, string mensaje)
        {
            string log = $"[DAL WARN] {metodo}: {mensaje}";
            Debug.WriteLine(log);
            Console.WriteLine(log);
        }

        #endregion
    }
}
