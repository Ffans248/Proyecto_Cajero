using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cajerro.DAL.Interfaces;
using Cajerro.DAL.Modelos;
using Microsoft.Data.SqlClient;

namespace Cajerro.DAL
{
    /// <summary>
    /// Gestor de Persistencia Unificado en SQL Server (BD_CajeroRed).
    /// Reemplaza por completo el antiguo manejo por archivos CSV para que toda la solución
    /// (Servidor Central, Panel Unificado y Cajeros Clientes) consulte únicamente la Base de Datos Maestra SQL Server alojada en este equipo.
    /// </summary>
    public class GestorArchivosCSV : IGestorArchivos
    {
        private static readonly int[] DenominacionesOficiales = [200, 100, 50, 20, 10, 5, 1];
        private static string _cadenaConexion =
            "Server=localhost\\SQLEXPRESS;Database=BD_CajeroRed;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=15;";
        private static bool _esquemaVerificado = false;
        private static readonly object _lockEsquema = new();

        public GestorArchivosCSV(string? rutaDirectorio = null)
        {
            AsegurarEsquemaSqlYLimpiarCsv(rutaDirectorio);
        }

        public static SqlConnection ObtenerConexionSql()
        {
            AsegurarEsquemaSqlYLimpiarCsv(null);
            return new SqlConnection(_cadenaConexion);
        }

        private static void AsegurarEsquemaSqlYLimpiarCsv(string? rutaDir)
        {
            if (_esquemaVerificado) return;
            lock (_lockEsquema)
            {
                if (_esquemaVerificado) return;

                string[] candidatas =
                {
                    "Server=localhost\\SQLEXPRESS;Database=BD_CajeroRed;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=5;",
                    "Server=localhost\\SQLEXPRESS;Database=BD_CajeroRed;User Id=cajero_red;Password=Cajero2026!;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=5;",
                    "Server=(localdb)\\MSSQLLocalDB;Database=BD_CajeroRed;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=5;",
                    "Server=localhost;Database=BD_CajeroRed;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=5;"
                };

                foreach (var cad in candidatas)
                {
                    try
                    {
                        using var conn = new SqlConnection(cad);
                        conn.Open();
                        _cadenaConexion = cad.Replace("Connect Timeout=5;", "Connect Timeout=15;");

                        string sqlExt = @"
                            IF COL_LENGTH('dbo.Tarjeta', 'PinTexto') IS NULL
                                ALTER TABLE dbo.Tarjeta ADD PinTexto VARCHAR(10) NOT NULL DEFAULT '1234';
                            IF COL_LENGTH('dbo.Cajero', 'CodigoBanco') IS NULL
                                ALTER TABLE dbo.Cajero ADD CodigoBanco VARCHAR(10) NOT NULL DEFAULT 'BANC01';
                            IF COL_LENGTH('dbo.Cajero', 'Puerto') IS NULL
                                ALTER TABLE dbo.Cajero ADD Puerto INT NOT NULL DEFAULT 5000;
                            IF COL_LENGTH('dbo.Cajero', 'EsLocal') IS NULL
                                ALTER TABLE dbo.Cajero ADD EsLocal BIT NOT NULL DEFAULT 0;
                            IF COL_LENGTH('dbo.BancoRed', 'EnLinea') IS NULL
                                ALTER TABLE dbo.BancoRed ADD EnLinea BIT NOT NULL DEFAULT 0;";

                        using var cmd = new SqlCommand(sqlExt, conn);
                        cmd.ExecuteNonQuery();
                        break;
                    }
                    catch { }
                }

                // Eliminar archivos CSV antiguos para garantizar que todo opere 100% sobre SQL Server
                try
                {
                    string baseDir = string.IsNullOrWhiteSpace(rutaDir) ? AppDomain.CurrentDomain.BaseDirectory : rutaDir;
                    string[] archivosObsoletos =
                    {
                        "Usuarios.csv", "Transacciones.csv", "Boveda.csv", "Bitacora.csv",
                        "EstadoCajero.txt", "CajerosRed.csv", "BancosRed.csv",
                        "CuentasExternas.csv", "TransferenciasNuestroBanco.csv", "TransferenciasOtroBanco.csv"
                    };
                    foreach (var arch in archivosObsoletos)
                    {
                        string p = Path.Combine(baseDir, arch);
                        if (File.Exists(p)) File.Delete(p);
                    }
                }
                catch { }

                _esquemaVerificado = true;
            }
        }

        private static string CalcularPinHash(string pin, string salt)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(pin + salt);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToUpperInvariant();
        }

        #region Operaciones de Usuario (SQL Server: dbo.Usuario + dbo.Tarjeta)

        public List<Usuario> ObtenerTodosLosUsuarios()
        {
            var lista = new List<Usuario>();
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    SELECT t.NumeroTarjeta, ISNULL(t.PinTexto, '1234') AS PIN, u.Nombre, u.Saldo, u.LimiteDiario
                    FROM dbo.Tarjeta t
                    JOIN dbo.Usuario u ON t.IdUsuario = u.IdUsuario
                    WHERE t.Activa = 1 AND u.Activo = 1
                    ORDER BY u.IdUsuario ASC";

                using var cmd = new SqlCommand(sql, conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lista.Add(new Usuario
                    {
                        NumeroTarjeta = r["NumeroTarjeta"].ToString()!,
                        PIN = r["PIN"].ToString()!,
                        Nombre = r["Nombre"].ToString()!,
                        SaldoActual = Convert.ToDecimal(r["Saldo"]),
                        LimiteDiario = Convert.ToDecimal(r["LimiteDiario"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ObtenerTodosLosUsuarios: {ex.Message}");
            }
            return lista;
        }

        public Usuario? ObtenerUsuarioPorTarjeta(string tarjeta)
        {
            if (string.IsNullOrWhiteSpace(tarjeta)) return null;
            string buscada = tarjeta.Trim();

            if (buscada.Equals("99A90BA4", StringComparison.OrdinalIgnoreCase))
                buscada = "1111222233334444";
            else if (buscada.Equals("A77B2F63", StringComparison.OrdinalIgnoreCase))
                buscada = "2222333344445555";

            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    SELECT TOP 1 t.NumeroTarjeta, ISNULL(t.PinTexto, '1234') AS PIN, u.Nombre, u.Saldo, u.LimiteDiario
                    FROM dbo.Tarjeta t
                    JOIN dbo.Usuario u ON t.IdUsuario = u.IdUsuario
                    WHERE t.Activa = 1 AND u.Activo = 1
                      AND (t.NumeroTarjeta = @Buscada OR u.NumeroCuenta = @Buscada)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Buscada", buscada);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    return new Usuario
                    {
                        NumeroTarjeta = r["NumeroTarjeta"].ToString()!,
                        PIN = r["PIN"].ToString()!,
                        Nombre = r["Nombre"].ToString()!,
                        SaldoActual = Convert.ToDecimal(r["Saldo"]),
                        LimiteDiario = Convert.ToDecimal(r["LimiteDiario"])
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ObtenerUsuarioPorTarjeta: {ex.Message}");
            }
            return null;
        }

        public bool CrearNuevoUsuario(Usuario nuevo)
        {
            if (nuevo == null || string.IsNullOrWhiteSpace(nuevo.NumeroTarjeta)) return false;

            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();
                using var tx = conn.BeginTransaction();

                string salt = "SALT" + Random.Shared.Next(10, 99);
                string pin = string.IsNullOrWhiteSpace(nuevo.PIN) ? "1234" : nuevo.PIN.Trim();
                string hash = CalcularPinHash(pin, salt);

                string sqlU = @"
                    INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos, Activo)
                    VALUES (@Nombre, @Saldo, @Limite, 'Activo', 0, 1);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int idUsuario;
                using (var cmdU = new SqlCommand(sqlU, conn, tx))
                {
                    cmdU.Parameters.AddWithValue("@Nombre", nuevo.Nombre.Trim());
                    cmdU.Parameters.AddWithValue("@Saldo", nuevo.SaldoActual);
                    cmdU.Parameters.AddWithValue("@Limite", nuevo.LimiteDiario > 0 ? nuevo.LimiteDiario : 2000m);
                    idUsuario = Convert.ToInt32(cmdU.ExecuteScalar());
                }

                string cuenta = $"1001{idUsuario:D6}";
                using (var cmdCta = new SqlCommand("UPDATE dbo.Usuario SET NumeroCuenta = @Cta WHERE IdUsuario = @Id", conn, tx))
                {
                    cmdCta.Parameters.AddWithValue("@Cta", cuenta);
                    cmdCta.Parameters.AddWithValue("@Id", idUsuario);
                    cmdCta.ExecuteNonQuery();
                }

                string sqlT = @"
                    INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, PinTexto, IdUsuario, Activa)
                    VALUES (@Tarjeta, @Hash, @Salt, @Pin, @IdUsuario, 1);";

                using (var cmdT = new SqlCommand(sqlT, conn, tx))
                {
                    cmdT.Parameters.AddWithValue("@Tarjeta", nuevo.NumeroTarjeta.Trim());
                    cmdT.Parameters.AddWithValue("@Hash", hash);
                    cmdT.Parameters.AddWithValue("@Salt", salt);
                    cmdT.Parameters.AddWithValue("@Pin", pin);
                    cmdT.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cmdT.ExecuteNonQuery();
                }

                tx.Commit();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] CrearNuevoUsuario: {ex.Message}");
                return false;
            }
        }

        public bool ActualizarSaldo(string tarjeta, decimal nuevoSaldo)
        {
            if (string.IsNullOrWhiteSpace(tarjeta)) return false;

            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    UPDATE u
                    SET u.Saldo = @Saldo
                    FROM dbo.Usuario u
                    JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario
                    WHERE t.NumeroTarjeta = @Tarjeta OR u.NumeroCuenta = @Tarjeta";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Saldo", nuevoSaldo);
                cmd.Parameters.AddWithValue("@Tarjeta", tarjeta.Trim());
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ActualizarSaldo: {ex.Message}");
                return false;
            }
        }

        public bool ActualizarUsuario(Usuario usuarioActualizado)
        {
            if (usuarioActualizado == null || string.IsNullOrWhiteSpace(usuarioActualizado.NumeroTarjeta)) return false;

            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string pin = string.IsNullOrWhiteSpace(usuarioActualizado.PIN) ? "1234" : usuarioActualizado.PIN.Trim();

                string sql = @"
                    DECLARE @IdU INT, @Salt VARCHAR(36);
                    SELECT @IdU = IdUsuario, @Salt = PinSalt FROM dbo.Tarjeta WHERE NumeroTarjeta = @Tarjeta;
                    IF @IdU IS NOT NULL
                    BEGIN
                        UPDATE dbo.Usuario
                        SET Nombre = @Nombre,
                            Saldo = @Saldo,
                            LimiteDiario = @Limite,
                            Estado = 'Activo',
                            IntentosFallidos = 0
                        WHERE IdUsuario = @IdU;

                        UPDATE dbo.Tarjeta
                        SET PinTexto = @Pin,
                            PinHash = CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CONCAT(@Pin, @Salt)), 2)
                        WHERE NumeroTarjeta = @Tarjeta;
                    END";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Tarjeta", usuarioActualizado.NumeroTarjeta.Trim());
                cmd.Parameters.AddWithValue("@Nombre", usuarioActualizado.Nombre.Trim());
                cmd.Parameters.AddWithValue("@Saldo", usuarioActualizado.SaldoActual);
                cmd.Parameters.AddWithValue("@Limite", usuarioActualizado.LimiteDiario);
                cmd.Parameters.AddWithValue("@Pin", pin);
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ActualizarUsuario: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Operaciones de Transacciones (SQL Server: dbo.Transaccion)

        public bool RegistrarTransaccion(Transaccion t)
        {
            if (t == null) return false;

            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string tipoNormalizado = "CONSULTA";
                string mov = (t.TipoMovimiento ?? "").ToUpperInvariant();
                if (mov.Contains("RETIRO")) tipoNormalizado = "RETIRO";
                else if (mov.Contains("DEP")) tipoNormalizado = "DEPOSITO";
                else if (mov.Contains("PIN")) tipoNormalizado = "CAMBIO_PIN";
                else if (mov.Contains("INTERBANCARIA") || mov.Contains("BANC")) tipoNormalizado = "TRANSFERENCIA_INTERBANCARIA";
                else if (mov.Contains("TRANSF")) tipoNormalizado = "TRANSFERENCIA_ENVIADA";

                string sql = @"
                    DECLARE @IdU INT, @IdT INT, @IdC INT;
                    SELECT TOP 1 @IdU = IdUsuario, @IdT = IdTarjeta FROM dbo.Tarjeta WHERE NumeroTarjeta = @Tarjeta;
                    SELECT TOP 1 @IdC = IdCajero FROM dbo.Cajero WHERE Codigo = 'ATM-01';

                    IF @IdU IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.Transaccion (IdCajero, IdUsuario, IdTarjeta, Tipo, Monto, CodigoRespuesta, MotivoRechazo, TokenTransferencia, Fecha)
                        VALUES (@IdC, @IdU, @IdT, @Tipo, @Monto, '00', @Detalle, @Token, @Fecha);
                    END";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Tarjeta", t.NumeroTarjeta.Trim());
                cmd.Parameters.AddWithValue("@Tipo", tipoNormalizado);
                cmd.Parameters.AddWithValue("@Monto", t.Monto);
                cmd.Parameters.AddWithValue("@Detalle", t.TipoMovimiento ?? "");
                cmd.Parameters.AddWithValue("@Token", string.IsNullOrWhiteSpace(t.Token) ? DBNull.Value : t.Token.Trim());
                cmd.Parameters.AddWithValue("@Fecha", t.FechaHora == default ? DateTime.Now : t.FechaHora);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] RegistrarTransaccion: {ex.Message}");
                return false;
            }
        }

        public List<Transaccion> ObtenerHistorial(string tarjeta)
        {
            if (string.IsNullOrWhiteSpace(tarjeta)) return new List<Transaccion>();
            return ObtenerTodasLasTransacciones()
                .Where(x => x.NumeroTarjeta.Equals(tarjeta.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public List<Transaccion> ObtenerTodasLasTransacciones()
        {
            var lista = new List<Transaccion>();
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    SELECT ISNULL(tar.NumeroTarjeta, ISNULL(u.NumeroCuenta, '')) AS NumeroTarjeta,
                           tx.Fecha,
                           ISNULL(tx.MotivoRechazo, tx.Tipo) AS TipoMovimiento,
                           tx.Monto,
                           ISNULL(tx.TokenTransferencia, '') AS Token
                    FROM dbo.Transaccion tx
                    JOIN dbo.Usuario u ON tx.IdUsuario = u.IdUsuario
                    LEFT JOIN dbo.Tarjeta tar ON tx.IdTarjeta = tar.IdTarjeta
                    ORDER BY tx.Fecha DESC";

                using var cmd = new SqlCommand(sql, conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lista.Add(new Transaccion
                    {
                        NumeroTarjeta = r["NumeroTarjeta"].ToString()!,
                        FechaHora = Convert.ToDateTime(r["Fecha"]),
                        TipoMovimiento = r["TipoMovimiento"].ToString()!,
                        Monto = Convert.ToDecimal(r["Monto"]),
                        Token = r["Token"].ToString()!
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ObtenerTodasLasTransacciones: {ex.Message}");
            }
            return lista;
        }

        #endregion

        #region Operaciones de Bóveda (SQL Server: dbo.InventarioCajero + dbo.Cajero)

        public List<Billete> ObtenerInventarioBilletes()
        {
            var lista = new List<Billete>();
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    SELECT d.Valor, ISNULL(ic.Cantidad, 0) AS Cantidad
                    FROM dbo.Denominacion d
                    LEFT JOIN dbo.InventarioCajero ic ON d.IdDenominacion = ic.IdDenominacion
                        AND ic.IdCajero = (SELECT TOP 1 IdCajero FROM dbo.Cajero WHERE Codigo = 'ATM-01')
                    ORDER BY d.Valor DESC";

                using var cmd = new SqlCommand(sql, conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lista.Add(new Billete(Convert.ToInt32(r["Valor"]), Convert.ToInt32(r["Cantidad"])));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ObtenerInventarioBilletes: {ex.Message}");
            }

            if (lista.Count == 0)
            {
                foreach (var d in DenominacionesOficiales)
                    lista.Add(new Billete(d, 20));
            }
            return lista;
        }

        public bool GuardarInventarioBilletes(List<Billete> inventarioActualizado)
        {
            if (inventarioActualizado == null) return false;

            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                foreach (var b in inventarioActualizado)
                {
                    string sql = @"
                        DECLARE @IdC INT = (SELECT TOP 1 IdCajero FROM dbo.Cajero WHERE Codigo = 'ATM-01');
                        IF @IdC IS NOT NULL
                        BEGIN
                            IF EXISTS (SELECT 1 FROM dbo.InventarioCajero WHERE IdCajero = @IdC AND IdDenominacion = @Denom)
                                UPDATE dbo.InventarioCajero SET Cantidad = @Cant WHERE IdCajero = @IdC AND IdDenominacion = @Denom;
                            ELSE
                                INSERT INTO dbo.InventarioCajero (IdCajero, IdDenominacion, Cantidad) VALUES (@IdC, @Denom, @Cant);
                        END";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Denom", b.Denominacion);
                    cmd.Parameters.AddWithValue("@Cant", Math.Max(0, b.Cantidad));
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] GuardarInventarioBilletes: {ex.Message}");
                return false;
            }
        }

        public bool EstaCajeroInicializado()
        {
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                using var cmd = new SqlCommand("SELECT TOP 1 Inicializado FROM dbo.Cajero WHERE Codigo = 'ATM-01'", conn);
                var val = cmd.ExecuteScalar();
                return val != null && val != DBNull.Value && Convert.ToBoolean(val);
            }
            catch
            {
                return true;
            }
        }

        public bool InicializarCajero(List<Billete> billetesIniciales)
        {
            if (billetesIniciales == null || billetesIniciales.Count == 0)
                throw new ArgumentException("Debe proporcionar la cantidad de billetes para cada denominación.");

            decimal total = billetesIniciales.Sum(b => b.Subtotal);
            if (total <= 0m)
                throw new InvalidOperationException("El monto de inicialización debe ser mayor a Q0.00.");
            if (total > 10000m)
                throw new InvalidOperationException($"El monto total de inicialización no puede ser mayor a Q10,000.00 (Monto ingresado: Q{total:N2}).");

            bool ok = GuardarInventarioBilletes(billetesIniciales);
            if (ok)
            {
                using var conn = ObtenerConexionSql();
                conn.Open();
                using var cmd = new SqlCommand("UPDATE dbo.Cajero SET Inicializado = 1, FechaUltimaInicializacion = CAST(GETDATE() AS DATE), Estado = 'En línea' WHERE Codigo = 'ATM-01'", conn);
                cmd.ExecuteNonQuery();

                RegistrarEnBitacora("Inicialización Cajero", "ADMIN", $"Carga inicial en SQL Server por Q{total:N2}.");
            }
            return ok;
        }

        public bool ReiniciarInicializacionDia()
        {
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();
                using var cmd = new SqlCommand("UPDATE dbo.Cajero SET Inicializado = 0, FechaUltimaInicializacion = NULL WHERE Codigo = 'ATM-01'", conn);
                cmd.ExecuteNonQuery();
                RegistrarEnBitacora("Reinicio de Apertura", "ADMIN", "Se reinició el estado de inicialización diaria del cajero en SQL Server.");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool AgregarEfectivoBoveda(List<Billete> billetesRecarga)
        {
            if (billetesRecarga == null || billetesRecarga.Count == 0)
                throw new ArgumentException("Debe ingresar los billetes a recargar.");

            decimal totalRecarga = billetesRecarga.Sum(b => b.Subtotal);
            if (totalRecarga <= 0m)
                throw new InvalidOperationException("Debe ingresar una cantidad mayor a cero billetes para recargar.");

            var inventarioActual = ObtenerInventarioBilletes();
            decimal totalActual = inventarioActual.Sum(b => b.Subtotal);
            if (totalActual + totalRecarga > 30000m)
                throw new InvalidOperationException($"La recarga excede el límite máximo de Q30,000.00 en bóveda (Actual: Q{totalActual:N2}).");

            foreach (var rec in billetesRecarga)
            {
                var item = inventarioActual.FirstOrDefault(x => x.Denominacion == rec.Denominacion);
                if (item != null) item.Cantidad += Math.Max(0, rec.Cantidad);
            }

            bool ok = GuardarInventarioBilletes(inventarioActual);
            if (ok)
            {
                RegistrarEnBitacora("Recarga de Efectivo", "ADMIN", $"Recarga en SQL Server por Q{totalRecarga:N2}. Nuevo total: Q{(totalActual + totalRecarga):N2}.");
            }
            return ok;
        }

        #endregion

        #region Bitácora Administrativa y Control (SQL Server: dbo.Bitacora)

        public bool RegistrarEnBitacora(string tipoEvento, string usuarioOTarjeta, string detalle)
        {
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
                    VALUES (@Origen, @Accion, @Detalle, GETDATE())";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Origen", string.IsNullOrWhiteSpace(usuarioOTarjeta) ? "Sistema" : usuarioOTarjeta.Trim());
                cmd.Parameters.AddWithValue("@Accion", string.IsNullOrWhiteSpace(tipoEvento) ? "Evento" : tipoEvento.Trim());
                cmd.Parameters.AddWithValue("@Detalle", detalle ?? "");
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] RegistrarEnBitacora: {ex.Message}");
                return false;
            }
        }

        public List<RegistroBitacora> ObtenerBitacora()
        {
            var lista = new List<RegistroBitacora>();
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = "SELECT TOP 300 Fecha, Accion, Origen, Detalle FROM dbo.Bitacora ORDER BY Fecha DESC";
                using var cmd = new SqlCommand(sql, conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lista.Add(new RegistroBitacora
                    {
                        FechaHora = Convert.ToDateTime(r["Fecha"]),
                        TipoEvento = r["Accion"].ToString()!,
                        UsuarioOTarjeta = r["Origen"].ToString()!,
                        Detalle = r["Detalle"].ToString()!
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ObtenerBitacora: {ex.Message}");
            }
            return lista;
        }

        public bool ModificarNumeroTarjeta(string tarjetaActual, string nuevaTarjeta)
        {
            if (string.IsNullOrWhiteSpace(tarjetaActual) || string.IsNullOrWhiteSpace(nuevaTarjeta))
                throw new ArgumentException("Los números de tarjeta no pueden estar vacíos.");

            tarjetaActual = tarjetaActual.Trim();
            nuevaTarjeta = nuevaTarjeta.Trim();

            if (nuevaTarjeta.Length != 16 || !nuevaTarjeta.All(char.IsDigit))
                throw new ArgumentException("El nuevo número de tarjeta debe tener exactamente 16 dígitos numéricos.");

            using var conn = ObtenerConexionSql();
            conn.Open();

            using (var cmdCheck = new SqlCommand("SELECT COUNT(1) FROM dbo.Tarjeta WHERE NumeroTarjeta = @Nueva", conn))
            {
                cmdCheck.Parameters.AddWithValue("@Nueva", nuevaTarjeta);
                if (Convert.ToInt32(cmdCheck.ExecuteScalar()) > 0)
                    throw new InvalidOperationException($"La tarjeta {nuevaTarjeta} ya está registrada en SQL Server.");
            }

            using var cmdUpd = new SqlCommand("UPDATE dbo.Tarjeta SET NumeroTarjeta = @Nueva WHERE NumeroTarjeta = @Actual", conn);
            cmdUpd.Parameters.AddWithValue("@Nueva", nuevaTarjeta);
            cmdUpd.Parameters.AddWithValue("@Actual", tarjetaActual);
            bool ok = cmdUpd.ExecuteNonQuery() > 0;

            if (ok)
            {
                RegistrarEnBitacora("Cambio de Tarjeta", nuevaTarjeta, $"Tarjeta actualizada en SQL Server. Tarjeta previa: {tarjetaActual}.");
            }
            return ok;
        }

        public List<string> ObtenerTarjetasVinculadas(string tarjeta)
        {
            return new List<string> { (tarjeta ?? "").Trim() };
        }

        public DateTime? ObtenerUltimoAcceso(string tarjeta)
        {
            var bitacora = ObtenerBitacora();
            return bitacora.FirstOrDefault(r => r.UsuarioOTarjeta.Equals(tarjeta?.Trim(), StringComparison.OrdinalIgnoreCase))?.FechaHora;
        }

        public RegistroBitacora? ObtenerUltimoAccesoGeneral()
        {
            return ObtenerBitacora().FirstOrDefault();
        }

        #endregion

        #region Red Interbancaria y Cajeros en Red (SQL Server: dbo.Cajero, dbo.BancoRed, dbo.TransferenciaBancaria)

        public List<CajeroRedInfo> ObtenerCajerosRed()
        {
            var lista = new List<CajeroRedInfo>();
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    SELECT c.Codigo,
                           c.Nombre,
                           ISNULL(c.CodigoBanco, 'BANC01') AS CodigoBanco,
                           c.IP,
                           ISNULL(c.Puerto, 5000) AS Puerto,
                           ISNULL(c.EsLocal, CASE WHEN c.Codigo IN ('ATM-01', 'ATM-01-VPN') THEN 1 ELSE 0 END) AS EsLocal,
                           c.Estado,
                           ISNULL(v.EfectivoTotal, 10000.00) AS EfectivoTotal,
                           ISNULL(c.UltimoHeartbeat, GETDATE()) AS UltimoHeartbeat
                    FROM dbo.Cajero c
                    LEFT JOIN dbo.vw_EstadoCajeros v ON c.IdCajero = v.IdCajero
                    WHERE c.Activo = 1
                    ORDER BY c.Codigo ASC";

                using var cmd = new SqlCommand(sql, conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string codigo = r["Codigo"].ToString()!;
                    bool esLocal = Convert.ToBoolean(r["EsLocal"]) || codigo.StartsWith("ATM-01", StringComparison.OrdinalIgnoreCase);
                    string estadoStr = r["Estado"].ToString()!;
                    bool enLinea = esLocal || estadoStr.StartsWith("En l", StringComparison.OrdinalIgnoreCase);

                    lista.Add(new CajeroRedInfo
                    {
                        CodigoCajero = codigo,
                        NombreCajero = r["Nombre"].ToString()!,
                        CodigoBanco = r["CodigoBanco"].ToString()!,
                        IP = r["IP"].ToString()!,
                        Puerto = Convert.ToInt32(r["Puerto"]),
                        EsLocal = esLocal,
                        EnLinea = enLinea,
                        EfectivoActual = Convert.ToDecimal(r["EfectivoTotal"]),
                        UltimaConexionBdMaestra = Convert.ToDateTime(r["UltimoHeartbeat"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ObtenerCajerosRed: {ex.Message}");
            }
            return lista;
        }

        public bool GuardarCajerosRed(List<CajeroRedInfo> cajeros)
        {
            if (cajeros == null) return false;
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                foreach (var c in cajeros)
                {
                    bool enLinea = c.EsLocal || c.EnLinea;
                    string estado = enLinea ? "En línea" : "Desconectado";
                    string ubicacion = $"{c.CodigoBanco} | {c.IP}:{c.Puerto}";

                    string sql = @"
                        IF NOT EXISTS (SELECT 1 FROM dbo.Cajero WHERE Codigo = @Codigo)
                        BEGIN
                            INSERT INTO dbo.Cajero (Codigo, Nombre, Ubicacion, IP, Estado, Inicializado, FechaUltimaInicializacion, UltimoHeartbeat, Activo, CodigoBanco, Puerto, EsLocal)
                            VALUES (@Codigo, @Nombre, @Ubicacion, @IP, @Estado, 1, CAST(GETDATE() AS DATE), GETDATE(), 1, @Banco, @Puerto, @EsLocal);

                            DECLARE @NewId INT = SCOPE_IDENTITY();
                            INSERT INTO dbo.InventarioCajero (IdCajero, IdDenominacion, Cantidad)
                            VALUES (@NewId, 200, 20), (@NewId, 100, 30), (@NewId, 50, 30), (@NewId, 20, 40), (@NewId, 10, 50), (@NewId, 5, 30), (@NewId, 1, 50);
                        END
                        ELSE
                        BEGIN
                            UPDATE dbo.Cajero
                            SET Nombre = @Nombre,
                                Ubicacion = @Ubicacion,
                                IP = @IP,
                                CodigoBanco = @Banco,
                                Puerto = @Puerto,
                                EsLocal = @EsLocal,
                                Estado = @Estado,
                                UltimoHeartbeat = CASE WHEN @EnLinea = 1 THEN GETDATE() ELSE UltimoHeartbeat END,
                                Activo = 1
                            WHERE Codigo = @Codigo;
                        END";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Codigo", c.CodigoCajero.Trim());
                    cmd.Parameters.AddWithValue("@Nombre", c.NombreCajero.Trim());
                    cmd.Parameters.AddWithValue("@Ubicacion", ubicacion);
                    cmd.Parameters.AddWithValue("@IP", c.IP.Trim());
                    cmd.Parameters.AddWithValue("@Estado", estado);
                    cmd.Parameters.AddWithValue("@Banco", string.IsNullOrWhiteSpace(c.CodigoBanco) ? "BANC01" : c.CodigoBanco.Trim());
                    cmd.Parameters.AddWithValue("@Puerto", c.Puerto > 0 ? c.Puerto : 5000);
                    cmd.Parameters.AddWithValue("@EsLocal", c.EsLocal ? 1 : 0);
                    cmd.Parameters.AddWithValue("@EnLinea", enLinea ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] GuardarCajerosRed: {ex.Message}");
                return false;
            }
        }

        public List<BancoRedInfo> ObtenerBancosRed()
        {
            var lista = new List<BancoRedInfo>();
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sqlReset = @"
                    UPDATE dbo.BancoRed
                    SET TransferidoHoy = 0.00, FechaUltimoReset = CAST(GETDATE() AS DATE)
                    WHERE FechaUltimoReset < CAST(GETDATE() AS DATE);";
                using (var cmdR = new SqlCommand(sqlReset, conn))
                {
                    cmdR.ExecuteNonQuery();
                }

                string sql = @"
                    SELECT CodigoBanco, NombreBanco, IP, Puerto, EsPropio, LimiteDiario, TransferidoHoy, FechaUltimoReset, ISNULL(EnLinea, EsPropio) AS EnLinea
                    FROM dbo.BancoRed
                    WHERE Activo = 1
                    ORDER BY EsPropio DESC, CodigoBanco ASC";

                using var cmd = new SqlCommand(sql, conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    bool propio = Convert.ToBoolean(r["EsPropio"]);
                    lista.Add(new BancoRedInfo
                    {
                        CodigoBanco = r["CodigoBanco"].ToString()!,
                        NombreBanco = r["NombreBanco"].ToString()!,
                        IP = r["IP"].ToString()!,
                        Puerto = Convert.ToInt32(r["Puerto"]),
                        EsPropio = propio,
                        EnLinea = propio || Convert.ToBoolean(r["EnLinea"]),
                        LimiteDiario = Convert.ToDecimal(r["LimiteDiario"]),
                        TransferidoHoy = Convert.ToDecimal(r["TransferidoHoy"]),
                        FechaUltimoReinicio = Convert.ToDateTime(r["FechaUltimoReset"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ObtenerBancosRed: {ex.Message}");
            }
            return lista;
        }

        public bool GuardarBancosRed(List<BancoRedInfo> bancos)
        {
            if (bancos == null) return false;
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                foreach (var b in bancos)
                {
                    string sql = @"
                        IF NOT EXISTS (SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco = @Cod)
                        BEGIN
                            INSERT INTO dbo.BancoRed (CodigoBanco, NombreBanco, IP, Puerto, EsPropio, LimiteDiario, TransferidoHoy, FechaUltimoReset, Activo, EnLinea)
                            VALUES (@Cod, @Nom, @IP, @Pto, @Propio, @Lim, @Transf, CAST(GETDATE() AS DATE), 1, @EnLinea);
                        END
                        ELSE
                        BEGIN
                            UPDATE dbo.BancoRed
                            SET NombreBanco = @Nom,
                                IP = @IP,
                                Puerto = @Pto,
                                EsPropio = @Propio,
                                LimiteDiario = @Lim,
                                TransferidoHoy = @Transf,
                                EnLinea = @EnLinea,
                                Activo = 1
                            WHERE CodigoBanco = @Cod;
                        END";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Cod", b.CodigoBanco.Trim());
                    cmd.Parameters.AddWithValue("@Nom", b.NombreBanco.Trim());
                    cmd.Parameters.AddWithValue("@IP", b.IP.Trim());
                    cmd.Parameters.AddWithValue("@Pto", b.Puerto > 0 ? b.Puerto : 5000);
                    cmd.Parameters.AddWithValue("@Propio", b.EsPropio ? 1 : 0);
                    cmd.Parameters.AddWithValue("@Lim", b.LimiteDiario > 0 ? b.LimiteDiario : 25000m);
                    cmd.Parameters.AddWithValue("@Transf", b.TransferidoHoy);
                    cmd.Parameters.AddWithValue("@EnLinea", (b.EsPropio || b.EnLinea) ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] GuardarBancosRed: {ex.Message}");
                return false;
            }
        }

        public bool ReiniciarCuposDiariosBancos()
        {
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();
                using var cmd = new SqlCommand("UPDATE dbo.BancoRed SET TransferidoHoy = 0.00, FechaUltimoReset = CAST(GETDATE() AS DATE)", conn);
                cmd.ExecuteNonQuery();
                RegistrarEnBitacora("Reinicio Cupos Bancos", "ADMIN", "Se reiniciaron los límites diarios coordinados de todos los bancos en SQL Server.");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public List<CuentaExternaInfo> ObtenerCuentasExternas(string? codigoBanco = null)
        {
            var lista = new List<CuentaExternaInfo>();
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    SELECT CodigoBanco, NumeroCuenta, NombreTitular, SaldoSimulado, Activa
                    FROM dbo.CuentaBancoExterno
                    WHERE (@Banco IS NULL OR CodigoBanco = @Banco)
                    ORDER BY CodigoBanco, NumeroCuenta";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Banco", string.IsNullOrWhiteSpace(codigoBanco) ? DBNull.Value : codigoBanco.Trim());
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lista.Add(new CuentaExternaInfo
                    {
                        CodigoBanco = r["CodigoBanco"].ToString()!,
                        NumeroCuenta = r["NumeroCuenta"].ToString()!,
                        NombreTitular = r["NombreTitular"].ToString()!,
                        SaldoActual = Convert.ToDecimal(r["SaldoSimulado"]),
                        Activa = Convert.ToBoolean(r["Activa"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ObtenerCuentasExternas: {ex.Message}");
            }
            return lista;
        }

        public bool GuardarCuentaExterna(CuentaExternaInfo cuenta)
        {
            if (cuenta == null) return false;
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    IF NOT EXISTS (SELECT 1 FROM dbo.CuentaBancoExterno WHERE CodigoBanco = @Banco AND NumeroCuenta = @Cuenta)
                    BEGIN
                        INSERT INTO dbo.CuentaBancoExterno (CodigoBanco, NumeroCuenta, NombreTitular, SaldoSimulado, Activa)
                        VALUES (@Banco, @Cuenta, @Nombre, @Saldo, @Activa);
                    END
                    ELSE
                    BEGIN
                        UPDATE dbo.CuentaBancoExterno
                        SET NombreTitular = @Nombre,
                            SaldoSimulado = @Saldo,
                            Activa = @Activa
                        WHERE CodigoBanco = @Banco AND NumeroCuenta = @Cuenta;
                    END";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Banco", cuenta.CodigoBanco.Trim().ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Cuenta", cuenta.NumeroCuenta.Trim());
                cmd.Parameters.AddWithValue("@Nombre", cuenta.NombreTitular.Trim());
                cmd.Parameters.AddWithValue("@Saldo", cuenta.SaldoActual);
                cmd.Parameters.AddWithValue("@Activa", cuenta.Activa ? 1 : 0);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] GuardarCuentaExterna: {ex.Message}");
                return false;
            }
        }

        public bool RegistrarTransferenciaCompartida(RegistroTransferenciaBancaria t)
        {
            if (t == null) return false;
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string tipo = t.TipoTransferencia.Equals("INTERNA", StringComparison.OrdinalIgnoreCase) ? "INTERNA" : "INTERBANCARIA";
                string dir = tipo == "INTERNA" ? "INTERNA" : "SALIENTE";

                string sql = @"
                    IF NOT EXISTS (SELECT 1 FROM dbo.TransferenciaBancaria WHERE TokenTransferencia = @Token)
                    BEGIN
                        INSERT INTO dbo.TransferenciaBancaria
                            (TokenTransferencia, TipoTransferencia, Direccion, BancoOrigen, CuentaOrigen, NombreOrigen,
                             BancoDestino, CuentaDestino, NombreDestino, Monto, Moneda, Concepto, Estado, CodigoRespuesta,
                             MensajeRespuesta, CupoRestanteBancoOrigen, CupoRestanteBancoDestino, IdCajero, FechaCreacion, FechaConfirmacion)
                        VALUES
                            (@Token, @Tipo, @Dir, @BOrig, @COrig, @NOrig,
                             @BDest, @CDest, @NDest, @Monto, 'GTQ', @Concepto, 'COMPLETADA', '00',
                             'Transferencia aprobada en BD Maestra SQL', @CupoOrig, @CupoDest, 'ATM-01', @Fecha, @Fecha);
                    END";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Token", t.TokenTransferencia.Trim());
                cmd.Parameters.AddWithValue("@Tipo", tipo);
                cmd.Parameters.AddWithValue("@Dir", dir);
                cmd.Parameters.AddWithValue("@BOrig", t.BancoOrigen.Trim());
                cmd.Parameters.AddWithValue("@COrig", t.CuentaOrigen.Trim());
                cmd.Parameters.AddWithValue("@NOrig", t.NombreOrigen.Trim());
                cmd.Parameters.AddWithValue("@BDest", t.BancoDestino.Trim());
                cmd.Parameters.AddWithValue("@CDest", t.CuentaDestino.Trim());
                cmd.Parameters.AddWithValue("@NDest", t.NombreDestino.Trim());
                cmd.Parameters.AddWithValue("@Monto", t.Monto);
                cmd.Parameters.AddWithValue("@Concepto", t.Concepto ?? "Transferencia");
                cmd.Parameters.AddWithValue("@CupoOrig", t.CupoRestanteBancoOrigen);
                cmd.Parameters.AddWithValue("@CupoDest", t.CupoRestanteBancoDestino);
                cmd.Parameters.AddWithValue("@Fecha", t.FechaHora == default ? DateTime.Now : t.FechaHora);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] RegistrarTransferenciaCompartida: {ex.Message}");
                return false;
            }
        }

        public List<RegistroTransferenciaBancaria> ObtenerTransferenciasCompartidas()
        {
            var lista = new List<RegistroTransferenciaBancaria>();
            try
            {
                using var conn = ObtenerConexionSql();
                conn.Open();

                string sql = @"
                    SELECT TOP 200 TokenTransferencia, FechaCreacion, TipoTransferencia, BancoOrigen, CuentaOrigen, NombreOrigen,
                                   BancoDestino, CuentaDestino, NombreDestino, Monto, ISNULL(Concepto, '') AS Concepto,
                                   ISNULL(CupoRestanteBancoOrigen, 0) AS CupoOrig, ISNULL(CupoRestanteBancoDestino, 0) AS CupoDest, Estado
                    FROM dbo.TransferenciaBancaria
                    ORDER BY FechaCreacion DESC";

                using var cmd = new SqlCommand(sql, conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lista.Add(new RegistroTransferenciaBancaria
                    {
                        TokenTransferencia = r["TokenTransferencia"].ToString()!,
                        FechaHora = Convert.ToDateTime(r["FechaCreacion"]),
                        TipoTransferencia = r["TipoTransferencia"].ToString()!,
                        BancoOrigen = r["BancoOrigen"].ToString()!,
                        CuentaOrigen = r["CuentaOrigen"].ToString()!,
                        NombreOrigen = r["NombreOrigen"].ToString()!,
                        BancoDestino = r["BancoDestino"].ToString()!,
                        CuentaDestino = r["CuentaDestino"].ToString()!,
                        NombreDestino = r["NombreDestino"].ToString()!,
                        Monto = Convert.ToDecimal(r["Monto"]),
                        Concepto = r["Concepto"].ToString()!,
                        CupoRestanteBancoOrigen = Convert.ToDecimal(r["CupoOrig"]),
                        CupoRestanteBancoDestino = Convert.ToDecimal(r["CupoDest"]),
                        Estado = r["Estado"].ToString()!
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQL DAL ERROR] ObtenerTransferenciasCompartidas: {ex.Message}");
            }
            return lista;
        }

        #endregion
    }
}
