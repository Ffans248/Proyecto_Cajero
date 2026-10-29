using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Cajero.Protocolo;
using Cajero.Servidor.Comunicaciones;
using Cajero.Servidor.Modelos;
using Microsoft.Data.SqlClient;

namespace Cajero.Servidor.AccesoDatos
{
    public class RepositorioServidor
    {
        private bool _esquemaTransferenciasInicializado = false;
        private readonly object _lockInit = new();

        public RepositorioServidor()
        {
            AsegurarEsquemaTransferencias();
        }

        public void AsegurarEsquemaTransferencias()
        {
            if (_esquemaTransferenciasInicializado) return;
            lock (_lockInit)
            {
                if (_esquemaTransferenciasInicializado) return;
                try
                {
                    using var conn = ConexionBD.ObtenerConexion();
                    conn.Open();

                    string sqlMig = @"
                        IF COL_LENGTH('dbo.Usuario', 'NumeroCuenta') IS NULL
                        BEGIN
                            ALTER TABLE dbo.Usuario ADD NumeroCuenta VARCHAR(20) NULL;
                        END

                        IF COL_LENGTH('dbo.Transaccion', 'TokenTransferencia') IS NULL
                        BEGIN
                            ALTER TABLE dbo.Transaccion ADD TokenTransferencia VARCHAR(32) NULL;
                        END

                        DECLARE @chkTx NVARCHAR(128);
                        SELECT @chkTx = dc.name
                        FROM sys.check_constraints dc
                        JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                        WHERE dc.parent_object_id = OBJECT_ID('dbo.Transaccion') AND c.name = 'Tipo';
                        IF @chkTx IS NOT NULL AND @chkTx <> 'CK_Transaccion_Tipo_V2'
                        BEGIN
                            EXEC('ALTER TABLE dbo.Transaccion DROP CONSTRAINT [' + @chkTx + ']');
                        END
                        ALTER TABLE dbo.Transaccion ALTER COLUMN Tipo VARCHAR(30) NOT NULL;
                        ALTER TABLE dbo.Transaccion ALTER COLUMN IdTarjeta INT NULL;
                        ALTER TABLE dbo.Transaccion ALTER COLUMN IdCajero INT NULL;
                        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Transaccion_Tipo_V2')
                        BEGIN
                            ALTER TABLE dbo.Transaccion ADD CONSTRAINT CK_Transaccion_Tipo_V2
                                CHECK (Tipo IN ('RETIRO', 'DEPOSITO', 'CONSULTA', 'CAMBIO_PIN', 'TRANSFERENCIA_ENVIADA', 'TRANSFERENCIA_RECIBIDA', 'TRANSFERENCIA_INTERBANCARIA'));
                        END

                        IF OBJECT_ID('dbo.BancoRed', 'U') IS NULL
                        BEGIN
                            CREATE TABLE dbo.BancoRed (
                                IdBanco INT IDENTITY(1,1) PRIMARY KEY,
                                CodigoBanco VARCHAR(10) NOT NULL UNIQUE,
                                NombreBanco VARCHAR(100) NOT NULL,
                                IP VARCHAR(50) NOT NULL DEFAULT '127.0.0.1',
                                Puerto INT NOT NULL DEFAULT 5000,
                                EsPropio BIT NOT NULL DEFAULT 0,
                                LimiteDiario DECIMAL(18,2) NOT NULL DEFAULT 50000.00 CHECK (LimiteDiario > 0),
                                TransferidoHoy DECIMAL(18,2) NOT NULL DEFAULT 0.00 CHECK (TransferidoHoy >= 0),
                                FechaUltimoReset DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
                                CadenaConexionBD VARCHAR(500) NULL,
                                Activo BIT NOT NULL DEFAULT 1,
                                FechaRegistro DATETIME NOT NULL DEFAULT GETDATE()
                            );
                        END

                        IF OBJECT_ID('dbo.TransferenciaBancaria', 'U') IS NULL
                        BEGIN
                            CREATE TABLE dbo.TransferenciaBancaria (
                                IdTransferencia INT IDENTITY(1,1) PRIMARY KEY,
                                TokenTransferencia VARCHAR(32) NOT NULL UNIQUE,
                                TipoTransferencia VARCHAR(20) NOT NULL
                                    CHECK (TipoTransferencia IN ('INTERNA', 'INTERBANCARIA')),
                                Direccion VARCHAR(15) NOT NULL DEFAULT 'SALIENTE'
                                    CHECK (Direccion IN ('INTERNA', 'SALIENTE', 'ENTRANTE')),
                                BancoOrigen VARCHAR(10) NOT NULL,
                                CuentaOrigen VARCHAR(20) NOT NULL,
                                NombreOrigen VARCHAR(100) NOT NULL,
                                BancoDestino VARCHAR(10) NOT NULL,
                                CuentaDestino VARCHAR(20) NOT NULL,
                                NombreDestino VARCHAR(100) NOT NULL,
                                Monto DECIMAL(18,2) NOT NULL CHECK (Monto > 0),
                                Moneda VARCHAR(5) NOT NULL DEFAULT 'GTQ',
                                Concepto VARCHAR(200) NULL,
                                Estado VARCHAR(20) NOT NULL DEFAULT 'PENDIENTE'
                                    CHECK (Estado IN ('PENDIENTE', 'COMPLETADA', 'RECHAZADA', 'REVERTIDA', 'ANULADA')),
                                CodigoRespuesta VARCHAR(10) NOT NULL DEFAULT '00',
                                MensajeRespuesta VARCHAR(255) NULL,
                                CupoRestanteBancoOrigen DECIMAL(18,2) NULL,
                                CupoRestanteBancoDestino DECIMAL(18,2) NULL,
                                IdUsuarioLocal INT NULL FOREIGN KEY REFERENCES dbo.Usuario(IdUsuario),
                                IdCajero VARCHAR(20) NULL,
                                FechaCreacion DATETIME NOT NULL DEFAULT GETDATE(),
                                FechaConfirmacion DATETIME NULL
                            );
                        END

                        IF OBJECT_ID('dbo.CuentaBancoExterno', 'U') IS NULL
                        BEGIN
                            CREATE TABLE dbo.CuentaBancoExterno (
                                IdCuentaExterna INT IDENTITY(1,1) PRIMARY KEY,
                                CodigoBanco VARCHAR(10) NOT NULL,
                                NumeroCuenta VARCHAR(20) NOT NULL,
                                NombreTitular VARCHAR(100) NOT NULL,
                                SaldoSimulado DECIMAL(18,2) NOT NULL DEFAULT 5000.00,
                                Activa BIT NOT NULL DEFAULT 1,
                                CONSTRAINT UQ_Banco_CuentaExterna UNIQUE (CodigoBanco, NumeroCuenta)
                            );
                        END";

                    using (var cmdM = new SqlCommand(sqlMig, conn))
                    {
                        cmdM.ExecuteNonQuery();
                    }

                    string sqlSeed = @"
                        UPDATE dbo.Usuario
                        SET NumeroCuenta = CONCAT('1001', RIGHT('000000' + CAST(IdUsuario AS VARCHAR(10)), 6))
                        WHERE NumeroCuenta IS NULL OR NumeroCuenta = '';

                        IF NOT EXISTS (SELECT 1 FROM dbo.Parametro WHERE Clave = 'CodigoBancoPropio')
                            INSERT INTO dbo.Parametro (Clave, Valor, Descripcion) VALUES ('CodigoBancoPropio', 'BANC01', 'Código identificador único de nuestro banco en la red interbancaria');
                        IF NOT EXISTS (SELECT 1 FROM dbo.Parametro WHERE Clave = 'NombreBancoPropio')
                            INSERT INTO dbo.Parametro (Clave, Valor, Descripcion) VALUES ('NombreBancoPropio', 'Banco Nacional Central', 'Nombre oficial de nuestro banco');
                        IF NOT EXISTS (SELECT 1 FROM dbo.Parametro WHERE Clave = 'LimiteInterbancarioDiario')
                            INSERT INTO dbo.Parametro (Clave, Valor, Descripcion) VALUES ('LimiteInterbancarioDiario', '50000.00', 'Cupo diario global para transferencias interbancarias');

                        IF NOT EXISTS (SELECT 1 FROM dbo.BancoRed)
                        BEGIN
                            INSERT INTO dbo.BancoRed (CodigoBanco, NombreBanco, IP, Puerto, EsPropio, LimiteDiario, TransferidoHoy, FechaUltimoReset, Activo)
                            VALUES 
                                ('BANC01', 'Banco Nacional Central (Nuestro Banco)', '127.0.0.1', 5000, 1, 50000.00, 0.00, CAST(GETDATE() AS DATE), 1),
                                ('BANC02', 'Banco Industrial Universitario',         '192.168.1.102', 5000, 0, 50000.00, 0.00, CAST(GETDATE() AS DATE), 1),
                                ('BANC03', 'Banco Metropolitano del Sur',            '192.168.1.103', 5000, 0, 50000.00, 0.00, CAST(GETDATE() AS DATE), 1),
                                ('BANC04', 'Banco Continental de Occidente',         '192.168.1.104', 5000, 0, 50000.00, 0.00, CAST(GETDATE() AS DATE), 1);
                        END

                        IF NOT EXISTS (SELECT 1 FROM dbo.CuentaBancoExterno)
                        BEGIN
                            INSERT INTO dbo.CuentaBancoExterno (CodigoBanco, NumeroCuenta, NombreTitular, SaldoSimulado, Activa)
                            VALUES
                                ('BANC02', '2002000001', 'Roberto Carlos Estrada (BANC02)', 9500.00, 1),
                                ('BANC02', '2002000002', 'Gabriela Fernanda Solares (BANC02)', 6400.00, 1),
                                ('BANC02', '9000111122223333', 'Pedro Antonio Alvarado (BANC02)', 12000.00, 1),
                                ('BANC03', '3003000001', 'Lucía Fernanda Coronado (BANC03)', 7800.00, 1),
                                ('BANC03', '3003000002', 'Mario Alejandro Cabrera (BANC03)', 4300.00, 1),
                                ('BANC04', '4004000001', 'Claudia Patricia Herrera (BANC04)', 15200.00, 1);
                        END";

                    using (var cmdS = new SqlCommand(sqlSeed, conn))
                    {
                        cmdS.ExecuteNonQuery();
                    }

                    _esquemaTransferenciasInicializado = true;
                }
                catch
                {
                    // Si aún no está creada la BD se intentará en la próxima operación
                }
            }
        }

        #region Seguridad y Utilidades

        public static string GenerarHashSha256(string texto, string salt)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(texto + salt);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToUpper();
        }

        public static string GenerarTokenTransferenciaUnico()
        {
            int num = RandomNumberGenerator.GetInt32(100000, 999999);
            return $"TRF-{DateTime.Now:yyyyMMdd}-{num}";
        }

        #endregion

        #region Autenticación de Administradores

        public AdministradorSesion? AutenticarAdmin(string usuario, string password)
        {
            AsegurarEsquemaTransferencias();
            using var conn = ConexionBD.ObtenerConexion();
            conn.Open();

            string query = @"SELECT IdAdmin, Usuario, PasswordHash, PasswordSalt, Rol, Estado 
                             FROM dbo.Administrador 
                             WHERE Usuario = @Usuario AND Estado = 'Activo'";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                string storedHash = reader["PasswordHash"].ToString()!;
                string salt = reader["PasswordSalt"].ToString()!;
                string inputHash = GenerarHashSha256(password, salt);

                if (storedHash.Equals(inputHash, StringComparison.OrdinalIgnoreCase))
                {
                    return new AdministradorSesion
                    {
                        IdAdmin = Convert.ToInt32(reader["IdAdmin"]),
                        Usuario = reader["Usuario"].ToString()!,
                        Rol = reader["Rol"].ToString()!
                    };
                }
            }

            return null;
        }

        #endregion

        #region Protocolo Cajero - Procedimientos Almacenados

        public (string codigoRespuesta, string mensaje) RegistrarCajero(string codigo, string nombreEquipo, string ip, string version)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_RegistrarCajero", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@Codigo", codigo);
                cmd.Parameters.AddWithValue("@NombreEquipo", nombreEquipo);
                cmd.Parameters.AddWithValue("@IP", ip);
                cmd.Parameters.AddWithValue("@Version", version);

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);

                cmd.ExecuteNonQuery();

                return (pCodigo.Value?.ToString() ?? "96", pMensaje.Value?.ToString() ?? "Error desconocido");
            }
            catch (Exception ex)
            {
                return ("96", "Fallo al conectar con la base de datos: " + ex.Message);
            }
        }

        public (string codigoRespuesta, string mensaje) Heartbeat(string codigo)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_Heartbeat", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@Codigo", codigo);

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);

                cmd.ExecuteNonQuery();

                return (pCodigo.Value?.ToString() ?? "96", pMensaje.Value?.ToString() ?? "");
            }
            catch (Exception ex)
            {
                return ("96", ex.Message);
            }
        }

        public (string codigoRespuesta, string mensaje, int idUsuario, string nombreUsuario, bool requiereCambioPin) ValidarCredenciales(string tarjeta, string pin, string codigoCajero)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_ValidarCredenciales", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@NumeroTarjeta", tarjeta);
                cmd.Parameters.AddWithValue("@Pin", pin);
                cmd.Parameters.AddWithValue("@CodigoCajero", codigoCajero);

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                var pIdUsuario = new SqlParameter("@IdUsuario", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var pNombre = new SqlParameter("@NombreUsuario", SqlDbType.VarChar, 100) { Direction = ParameterDirection.Output };
                var pRequierePin = new SqlParameter("@RequiereCambioPin", SqlDbType.Bit) { Direction = ParameterDirection.Output };

                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);
                cmd.Parameters.Add(pIdUsuario);
                cmd.Parameters.Add(pNombre);
                cmd.Parameters.Add(pRequierePin);

                cmd.ExecuteNonQuery();

                string cod = pCodigo.Value?.ToString() ?? "96";
                string msj = pMensaje.Value?.ToString() ?? "";
                int idU = pIdUsuario.Value != DBNull.Value ? Convert.ToInt32(pIdUsuario.Value) : 0;
                string nom = pNombre.Value?.ToString() ?? "";
                bool req = pRequierePin.Value != DBNull.Value && Convert.ToBoolean(pRequierePin.Value);

                return (cod, msj, idU, nom, req);
            }
            catch (Exception ex)
            {
                return ("96", "Error en base de datos: " + ex.Message, 0, "", false);
            }
        }

        public (string token, DateTime expiracion) GenerarToken(int idUsuario, string codigoCajero)
        {
            using var conn = ConexionBD.ObtenerConexion();
            conn.Open();

            using var cmd = new SqlCommand("dbo.sp_GenerarToken", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
            cmd.Parameters.AddWithValue("@CodigoCajero", codigoCajero);

            var pToken = new SqlParameter("@TokenGenerado", SqlDbType.VarChar, 6) { Direction = ParameterDirection.Output };
            var pExp = new SqlParameter("@FechaExpiracion", SqlDbType.DateTime) { Direction = ParameterDirection.Output };

            cmd.Parameters.Add(pToken);
            cmd.Parameters.Add(pExp);

            cmd.ExecuteNonQuery();

            string tok = pToken.Value?.ToString() ?? "000000";
            DateTime exp = pExp.Value != DBNull.Value ? Convert.ToDateTime(pExp.Value) : DateTime.Now.AddMinutes(2);

            return (tok, exp);
        }

        public (string codigoRespuesta, string mensaje, int idUsuario, string nombreUsuario, decimal saldo, decimal limiteDiario, decimal disponibleHoy) ValidarToken(string codigoCajero, string tarjeta, string token)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_ValidarToken", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@CodigoCajero", codigoCajero);
                cmd.Parameters.AddWithValue("@NumeroTarjeta", tarjeta);
                cmd.Parameters.AddWithValue("@Token", token);

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                var pIdUsuario = new SqlParameter("@IdUsuario", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var pNombre = new SqlParameter("@NombreUsuario", SqlDbType.VarChar, 100) { Direction = ParameterDirection.Output };
                var pSaldo = new SqlParameter("@Saldo", SqlDbType.Decimal) { Precision = 18, Scale = 2, Direction = ParameterDirection.Output };
                var pLimite = new SqlParameter("@LimiteDiario", SqlDbType.Decimal) { Precision = 18, Scale = 2, Direction = ParameterDirection.Output };
                var pDisp = new SqlParameter("@DisponibleHoy", SqlDbType.Decimal) { Precision = 18, Scale = 2, Direction = ParameterDirection.Output };

                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);
                cmd.Parameters.Add(pIdUsuario);
                cmd.Parameters.Add(pNombre);
                cmd.Parameters.Add(pSaldo);
                cmd.Parameters.Add(pLimite);
                cmd.Parameters.Add(pDisp);

                cmd.ExecuteNonQuery();

                return (
                    pCodigo.Value?.ToString() ?? "96",
                    pMensaje.Value?.ToString() ?? "",
                    pIdUsuario.Value != DBNull.Value ? Convert.ToInt32(pIdUsuario.Value) : 0,
                    pNombre.Value?.ToString() ?? "",
                    pSaldo.Value != DBNull.Value ? Convert.ToDecimal(pSaldo.Value) : 0,
                    pLimite.Value != DBNull.Value ? Convert.ToDecimal(pLimite.Value) : 0,
                    pDisp.Value != DBNull.Value ? Convert.ToDecimal(pDisp.Value) : 0
                );
            }
            catch (Exception ex)
            {
                return ("96", "Error en base de datos: " + ex.Message, 0, "", 0, 0, 0);
            }
        }

        public (string codigoRespuesta, string mensaje, int idTransaccion, decimal nuevoSaldo, decimal disponibleHoy, Dictionary<string, int>? desgloseAlternativo) Retiro(
            string codigoCajero, string tarjeta, decimal monto, Dictionary<string, int> desglose)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_Retiro", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                string desgloseJson = JsonSerializer.Serialize(desglose);

                cmd.Parameters.AddWithValue("@CodigoCajero", codigoCajero);
                cmd.Parameters.AddWithValue("@NumeroTarjeta", tarjeta);
                cmd.Parameters.AddWithValue("@Monto", monto);
                cmd.Parameters.AddWithValue("@DesgloseJson", desgloseJson);

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                var pIdTx = new SqlParameter("@IdTransaccion", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var pSaldo = new SqlParameter("@NuevoSaldo", SqlDbType.Decimal) { Precision = 18, Scale = 2, Direction = ParameterDirection.Output };
                var pDisp = new SqlParameter("@DisponibleHoy", SqlDbType.Decimal) { Precision = 18, Scale = 2, Direction = ParameterDirection.Output };
                var pAltJson = new SqlParameter("@DesgloseAlternativoJson", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };

                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);
                cmd.Parameters.Add(pIdTx);
                cmd.Parameters.Add(pSaldo);
                cmd.Parameters.Add(pDisp);
                cmd.Parameters.Add(pAltJson);

                cmd.ExecuteNonQuery();

                string cod = pCodigo.Value?.ToString() ?? "96";
                string msj = pMensaje.Value?.ToString() ?? "";
                int idTx = pIdTx.Value != DBNull.Value ? Convert.ToInt32(pIdTx.Value) : 0;
                decimal saldo = pSaldo.Value != DBNull.Value ? Convert.ToDecimal(pSaldo.Value) : 0;
                decimal disp = pDisp.Value != DBNull.Value ? Convert.ToDecimal(pDisp.Value) : 0;

                Dictionary<string, int>? alt = null;
                if (pAltJson.Value != DBNull.Value && !string.IsNullOrWhiteSpace(pAltJson.Value?.ToString()))
                {
                    try
                    {
                        alt = JsonSerializer.Deserialize<Dictionary<string, int>>(pAltJson.Value.ToString()!);
                    }
                    catch { }
                }

                return (cod, msj, idTx, saldo, disp, alt);
            }
            catch (Exception ex)
            {
                return ("96", "Error interno en retiro: " + ex.Message, 0, 0, 0, null);
            }
        }

        public (string codigoRespuesta, string mensaje, int idTransaccion, decimal nuevoSaldo) Deposito(
            string codigoCajero, string tarjeta, decimal monto, Dictionary<string, int> desglose)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_Deposito", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                string desgloseJson = JsonSerializer.Serialize(desglose);

                cmd.Parameters.AddWithValue("@CodigoCajero", codigoCajero);
                cmd.Parameters.AddWithValue("@NumeroTarjeta", tarjeta);
                cmd.Parameters.AddWithValue("@Monto", monto);
                cmd.Parameters.AddWithValue("@DesgloseJson", desgloseJson);

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                var pIdTx = new SqlParameter("@IdTransaccion", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var pSaldo = new SqlParameter("@NuevoSaldo", SqlDbType.Decimal) { Precision = 18, Scale = 2, Direction = ParameterDirection.Output };

                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);
                cmd.Parameters.Add(pIdTx);
                cmd.Parameters.Add(pSaldo);

                cmd.ExecuteNonQuery();

                return (
                    pCodigo.Value?.ToString() ?? "96",
                    pMensaje.Value?.ToString() ?? "",
                    pIdTx.Value != DBNull.Value ? Convert.ToInt32(pIdTx.Value) : 0,
                    pSaldo.Value != DBNull.Value ? Convert.ToDecimal(pSaldo.Value) : 0
                );
            }
            catch (Exception ex)
            {
                return ("96", "Error interno en depósito: " + ex.Message, 0, 0);
            }
        }

        public (string codigoRespuesta, string mensaje) CambiarPin(string tarjeta, string pinActual, string pinNuevo, string origen, string codigoCajero)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_CambiarPin", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@NumeroTarjeta", tarjeta);
                cmd.Parameters.AddWithValue("@PinActual", pinActual ?? "");
                cmd.Parameters.AddWithValue("@PinNuevo", pinNuevo);
                cmd.Parameters.AddWithValue("@Origen", origen);
                cmd.Parameters.AddWithValue("@CodigoCajero", codigoCajero);

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };

                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);

                cmd.ExecuteNonQuery();

                return (pCodigo.Value?.ToString() ?? "96", pMensaje.Value?.ToString() ?? "");
            }
            catch (Exception ex)
            {
                return ("96", ex.Message);
            }
        }

        public (string nombreUsuario, string numeroCuenta, decimal saldo, decimal limiteDiario, decimal disponibleHoy) ConsultaSaldo(string codigoCajero, string tarjeta)
        {
            AsegurarEsquemaTransferencias();
            using var conn = ConexionBD.ObtenerConexion();
            conn.Open();

            string sql = @"
                SELECT u.IdUsuario, u.Nombre, ISNULL(u.NumeroCuenta, '') AS NumeroCuenta, u.Saldo, u.LimiteDiario,
                    (SELECT ISNULL(SUM(t.Monto), 0) 
                     FROM dbo.Transaccion t 
                     WHERE t.IdUsuario = u.IdUsuario AND t.Tipo = 'RETIRO' AND t.CodigoRespuesta = '00' 
                       AND CAST(t.Fecha AS DATE) = CAST(GETDATE() AS DATE)) AS RetiradoHoy
                FROM dbo.Tarjeta t
                JOIN dbo.Usuario u ON t.IdUsuario = u.IdUsuario
                WHERE t.NumeroTarjeta = @Tarjeta AND t.Activa = 1";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Tarjeta", tarjeta);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                int idU = Convert.ToInt32(reader["IdUsuario"]);
                string nombre = reader["Nombre"].ToString()!;
                string cuenta = reader["NumeroCuenta"].ToString()!;
                if (string.IsNullOrWhiteSpace(cuenta))
                {
                    cuenta = $"1001{idU:D6}";
                }
                decimal saldo = Convert.ToDecimal(reader["Saldo"]);
                decimal limite = Convert.ToDecimal(reader["LimiteDiario"]);
                decimal retirado = Convert.ToDecimal(reader["RetiradoHoy"]);
                decimal disponible = limite - retirado;
                if (disponible < 0) disponible = 0;

                return (nombre, cuenta, saldo, limite, disponible);
            }

            return ("", "", 0, 0, 0);
        }

        public List<ItemTransaccion> UltimasTransacciones(string tarjeta, int topN = 5)
        {
            AsegurarEsquemaTransferencias();
            var lista = new List<ItemTransaccion>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = @"
                    DECLARE @IdU INT;
                    SELECT @IdU = IdUsuario FROM dbo.Tarjeta WHERE NumeroTarjeta = @Tarjeta;

                    SELECT TOP (@TopN)
                        t.IdTransaccion,
                        t.Fecha,
                        ISNULL(c.Codigo, 'RED-IB') AS Cajero,
                        t.Tipo,
                        t.Monto,
                        t.CodigoRespuesta,
                        t.MotivoRechazo,
                        t.TokenTransferencia
                    FROM dbo.Transaccion t
                    LEFT JOIN dbo.Cajero c ON t.IdCajero = c.IdCajero
                    WHERE t.IdUsuario = @IdU
                    ORDER BY t.Fecha DESC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Tarjeta", tarjeta);
                cmd.Parameters.AddWithValue("@TopN", topN);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    lista.Add(new ItemTransaccion
                    {
                        IdTransaccion = Convert.ToInt32(reader["IdTransaccion"]),
                        Fecha = Convert.ToDateTime(reader["Fecha"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        Cajero = reader["Cajero"].ToString()!,
                        Tipo = reader["Tipo"].ToString()!,
                        Monto = Convert.ToDecimal(reader["Monto"]),
                        CodigoRespuesta = reader["CodigoRespuesta"].ToString()!,
                        MotivoRechazo = reader["MotivoRechazo"] != DBNull.Value ? reader["MotivoRechazo"].ToString() : null,
                        TokenTransferencia = reader["TokenTransferencia"] != DBNull.Value ? reader["TokenTransferencia"].ToString() : null
                    });
                }
            }
            catch { }

            return lista;
        }

        public string? ObtenerTarjetaPorRfid(string uidRfid)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = "SELECT NumeroTarjeta FROM dbo.MapeoRfid WHERE UidRfid = @Uid";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Uid", uidRfid.Trim());

                var res = cmd.ExecuteScalar();
                return res?.ToString();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Transferencias entre Usuarios y entre Bancos (Interbancarias)

        public (string codigoBanco, string nombreBanco) ObtenerInfoBancoPropio()
        {
            AsegurarEsquemaTransferencias();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("SELECT TOP 1 CodigoBanco, NombreBanco FROM dbo.BancoRed WHERE EsPropio = 1", conn);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    return (r["CodigoBanco"].ToString()!, r["NombreBanco"].ToString()!);
                }
            }
            catch { }

            return ("BANC01", "Banco Nacional Central");
        }

        public List<BancoRedDto> ObtenerBancosRed(bool soloActivos = false)
        {
            AsegurarEsquemaTransferencias();
            var lista = new List<BancoRedDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                // Reiniciar automáticamente el acumulado diario si cambió la fecha
                string sqlReset = @"
                    UPDATE dbo.BancoRed
                    SET TransferidoHoy = 0.00, FechaUltimoReset = CAST(GETDATE() AS DATE)
                    WHERE FechaUltimoReset < CAST(GETDATE() AS DATE);";
                using (var cmdR = new SqlCommand(sqlReset, conn))
                {
                    cmdR.ExecuteNonQuery();
                }

                string sql = @"
                    SELECT IdBanco, CodigoBanco, NombreBanco, IP, Puerto, EsPropio, 
                           LimiteDiario, TransferidoHoy, FechaUltimoReset, CadenaConexionBD, Activo
                    FROM dbo.BancoRed
                    WHERE (@SoloActivos = 0 OR Activo = 1)
                    ORDER BY EsPropio DESC, CodigoBanco ASC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SoloActivos", soloActivos ? 1 : 0);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    lista.Add(new BancoRedDto
                    {
                        IdBanco = Convert.ToInt32(reader["IdBanco"]),
                        CodigoBanco = reader["CodigoBanco"].ToString()!,
                        NombreBanco = reader["NombreBanco"].ToString()!,
                        IP = reader["IP"].ToString()!,
                        Puerto = Convert.ToInt32(reader["Puerto"]),
                        EsPropio = Convert.ToBoolean(reader["EsPropio"]),
                        LimiteDiario = Convert.ToDecimal(reader["LimiteDiario"]),
                        TransferidoHoy = Convert.ToDecimal(reader["TransferidoHoy"]),
                        FechaUltimoReset = Convert.ToDateTime(reader["FechaUltimoReset"]),
                        CadenaConexionBD = reader["CadenaConexionBD"] != DBNull.Value ? reader["CadenaConexionBD"].ToString() : null,
                        Activo = Convert.ToBoolean(reader["Activo"])
                    });
                }
            }
            catch { }

            return lista;
        }

        public (bool exito, string mensaje) GuardarBancoRed(string codigoBanco, string nombreBanco, string ip, int puerto, bool esPropio, decimal limiteDiario, string? cadenaConexionBD, bool activo)
        {
            AsegurarEsquemaTransferencias();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                if (esPropio)
                {
                    using var cmdDes = new SqlCommand("UPDATE dbo.BancoRed SET EsPropio = 0 WHERE CodigoBanco <> @Cod", conn);
                    cmdDes.Parameters.AddWithValue("@Cod", codigoBanco.Trim().ToUpper());
                    cmdDes.ExecuteNonQuery();
                }

                string sql = @"
                    MERGE dbo.BancoRed AS target
                    USING (SELECT @Cod AS CodigoBanco) AS source
                    ON target.CodigoBanco = source.CodigoBanco
                    WHEN MATCHED THEN
                        UPDATE SET NombreBanco = @Nom, IP = @IP, Puerto = @Puerto, EsPropio = @Propio,
                                   LimiteDiario = @Lim, CadenaConexionBD = @Cad, Activo = @Act
                    WHEN NOT MATCHED THEN
                        INSERT (CodigoBanco, NombreBanco, IP, Puerto, EsPropio, LimiteDiario, TransferidoHoy, FechaUltimoReset, CadenaConexionBD, Activo)
                        VALUES (@Cod, @Nom, @IP, @Puerto, @Propio, @Lim, 0.00, CAST(GETDATE() AS DATE), @Cad, @Act);";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Cod", codigoBanco.Trim().ToUpper());
                cmd.Parameters.AddWithValue("@Nom", nombreBanco.Trim());
                cmd.Parameters.AddWithValue("@IP", ip.Trim());
                cmd.Parameters.AddWithValue("@Puerto", puerto);
                cmd.Parameters.AddWithValue("@Propio", esPropio ? 1 : 0);
                cmd.Parameters.AddWithValue("@Lim", limiteDiario);
                cmd.Parameters.AddWithValue("@Cad", string.IsNullOrWhiteSpace(cadenaConexionBD) ? DBNull.Value : cadenaConexionBD.Trim());
                cmd.Parameters.AddWithValue("@Act", activo ? 1 : 0);

                cmd.ExecuteNonQuery();
                RegistrarEnBitacora("Administrador", "Configuración de Banco en Red", $"Banco {codigoBanco} ({nombreBanco}) actualizado. Límite diario: Q {limiteDiario:N2}");
                return (true, "Banco guardado exitosamente.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public bool ReiniciarCuposDiariosBancos()
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();
                using var cmd = new SqlCommand("UPDATE dbo.BancoRed SET TransferidoHoy = 0.00, FechaUltimoReset = CAST(GETDATE() AS DATE)", conn);
                cmd.ExecuteNonQuery();
                RegistrarEnBitacora("Administrador", "Reinicio de Cupos Interbancarios", "Se reiniciaron a Q 0.00 los acumulados de transferencias interbancarias del día.");
                return true;
            }
            catch { return false; }
        }

        public List<CuentaBancoExternoDto> ObtenerCuentasExternasSimuladas(string? codigoBanco = null)
        {
            AsegurarEsquemaTransferencias();
            var lista = new List<CuentaBancoExternoDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = @"
                    SELECT IdCuentaExterna, CodigoBanco, NumeroCuenta, NombreTitular, SaldoSimulado, Activa
                    FROM dbo.CuentaBancoExterno
                    WHERE (@Cod IS NULL OR CodigoBanco = @Cod)
                    ORDER BY CodigoBanco, NumeroCuenta";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Cod", string.IsNullOrWhiteSpace(codigoBanco) ? DBNull.Value : codigoBanco.Trim());
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lista.Add(new CuentaBancoExternoDto
                    {
                        IdCuentaExterna = Convert.ToInt32(r["IdCuentaExterna"]),
                        CodigoBanco = r["CodigoBanco"].ToString()!,
                        NumeroCuenta = r["NumeroCuenta"].ToString()!,
                        NombreTitular = r["NombreTitular"].ToString()!,
                        SaldoSimulado = Convert.ToDecimal(r["SaldoSimulado"]),
                        Activa = Convert.ToBoolean(r["Activa"])
                    });
                }
            }
            catch { }
            return lista;
        }

        public (bool exito, string mensaje) GuardarCuentaExternaSimulada(string codigoBanco, string numeroCuenta, string nombreTitular, decimal saldoSimulado)
        {
            AsegurarEsquemaTransferencias();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = @"
                    MERGE dbo.CuentaBancoExterno AS target
                    USING (SELECT @Banco AS CodigoBanco, @Cta AS NumeroCuenta) AS source
                    ON target.CodigoBanco = source.CodigoBanco AND target.NumeroCuenta = source.NumeroCuenta
                    WHEN MATCHED THEN
                        UPDATE SET NombreTitular = @Nom, SaldoSimulado = @Saldo, Activa = 1
                    WHEN NOT MATCHED THEN
                        INSERT (CodigoBanco, NumeroCuenta, NombreTitular, SaldoSimulado, Activa)
                        VALUES (@Banco, @Cta, @Nom, @Saldo, 1);";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Banco", codigoBanco.Trim().ToUpper());
                cmd.Parameters.AddWithValue("@Cta", numeroCuenta.Trim());
                cmd.Parameters.AddWithValue("@Nom", nombreTitular.Trim());
                cmd.Parameters.AddWithValue("@Saldo", saldoSimulado);
                cmd.ExecuteNonQuery();

                return (true, "Cuenta externa registrada.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public List<TransferenciaBancariaDto> ObtenerHistorialTransferencias(string? tipo = null, string? busqueda = null)
        {
            AsegurarEsquemaTransferencias();
            var lista = new List<TransferenciaBancariaDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                var sql = new StringBuilder(@"
                    SELECT TOP 300
                        IdTransferencia, TokenTransferencia, TipoTransferencia, Direccion,
                        BancoOrigen, CuentaOrigen, NombreOrigen,
                        BancoDestino, CuentaDestino, NombreDestino,
                        Monto, Moneda, ISNULL(Concepto, '') AS Concepto,
                        Estado, CodigoRespuesta, MensajeRespuesta,
                        CupoRestanteBancoOrigen, CupoRestanteBancoDestino,
                        IdCajero, FechaCreacion, FechaConfirmacion
                    FROM dbo.TransferenciaBancaria
                    WHERE 1=1 ");

                if (!string.IsNullOrWhiteSpace(tipo) && tipo != "Todas")
                {
                    sql.Append(" AND TipoTransferencia = @Tipo ");
                }
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    sql.Append(@" AND (TokenTransferencia LIKE @Busq OR CuentaOrigen LIKE @Busq OR CuentaDestino LIKE @Busq 
                                       OR NombreOrigen LIKE @Busq OR NombreDestino LIKE @Busq OR BancoOrigen LIKE @Busq OR BancoDestino LIKE @Busq) ");
                }

                sql.Append(" ORDER BY FechaCreacion DESC");

                using var cmd = new SqlCommand(sql.ToString(), conn);
                if (!string.IsNullOrWhiteSpace(tipo) && tipo != "Todas") cmd.Parameters.AddWithValue("@Tipo", tipo);
                if (!string.IsNullOrWhiteSpace(busqueda)) cmd.Parameters.AddWithValue("@Busq", $"%{busqueda.Trim()}%");

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lista.Add(new TransferenciaBancariaDto
                    {
                        IdTransferencia = Convert.ToInt32(r["IdTransferencia"]),
                        TokenTransferencia = r["TokenTransferencia"].ToString()!,
                        TipoTransferencia = r["TipoTransferencia"].ToString()!,
                        Direccion = r["Direccion"].ToString()!,
                        BancoOrigen = r["BancoOrigen"].ToString()!,
                        CuentaOrigen = r["CuentaOrigen"].ToString()!,
                        NombreOrigen = r["NombreOrigen"].ToString()!,
                        BancoDestino = r["BancoDestino"].ToString()!,
                        CuentaDestino = r["CuentaDestino"].ToString()!,
                        NombreDestino = r["NombreDestino"].ToString()!,
                        Monto = Convert.ToDecimal(r["Monto"]),
                        Moneda = r["Moneda"].ToString()!,
                        Concepto = r["Concepto"].ToString()!,
                        Estado = r["Estado"].ToString()!,
                        CodigoRespuesta = r["CodigoRespuesta"].ToString()!,
                        MensajeRespuesta = r["MensajeRespuesta"] != DBNull.Value ? r["MensajeRespuesta"].ToString() : null,
                        CupoRestanteBancoOrigen = r["CupoRestanteBancoOrigen"] != DBNull.Value ? Convert.ToDecimal(r["CupoRestanteBancoOrigen"]) : null,
                        CupoRestanteBancoDestino = r["CupoRestanteBancoDestino"] != DBNull.Value ? Convert.ToDecimal(r["CupoRestanteBancoDestino"]) : null,
                        IdCajero = r["IdCajero"] != DBNull.Value ? r["IdCajero"].ToString() : null,
                        FechaCreacion = Convert.ToDateTime(r["FechaCreacion"]),
                        FechaConfirmacion = r["FechaConfirmacion"] != DBNull.Value ? Convert.ToDateTime(r["FechaConfirmacion"]) : null
                    });
                }
            }
            catch { }

            return lista;
        }

        /// <summary>
        /// Verifica una cuenta local (por Número de Cuenta de 10 dígitos o Número de Tarjeta de 16 dígitos).
        /// Usado tanto para transferencias internas como para responder consultas IB_VERIFICAR_CUENTA de otros bancos.
        /// </summary>
        public (bool existe, int idUsuario, string numeroCuenta, string numeroTarjeta, string nombreTitular) VerificarCuentaLocal(string cuentaOTarjeta)
        {
            AsegurarEsquemaTransferencias();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string limpia = cuentaOTarjeta.Trim();

                string sql = @"
                    SELECT TOP 1 
                        u.IdUsuario,
                        ISNULL(u.NumeroCuenta, CONCAT('1001', RIGHT('000000' + CAST(u.IdUsuario AS VARCHAR(10)), 6))) AS NumeroCuenta,
                        ISNULL(t.NumeroTarjeta, '') AS NumeroTarjeta,
                        u.Nombre
                    FROM dbo.Usuario u
                    LEFT JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario AND t.Activa = 1
                    WHERE u.Activo = 1 AND u.Estado = 'Activo'
                      AND (u.NumeroCuenta = @Cta OR t.NumeroTarjeta = @Cta)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Cta", limpia);

                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    return (
                        true,
                        Convert.ToInt32(r["IdUsuario"]),
                        r["NumeroCuenta"].ToString()!,
                        r["NumeroTarjeta"].ToString()!,
                        r["Nombre"].ToString()!
                    );
                }
            }
            catch { }

            return (false, 0, "", "", "");
        }

        /// <summary>
        /// Verifica una cuenta de destino (interna de nuestro banco o externa de otro banco seleccionado).
        /// Devuelve también los límites disponibles de ambos bancos para coordinar.
        /// </summary>
        public async Task<(string codigo, string mensaje, DatosVerificarCuenta datos)> VerificarCuentaDestinoAsync(string bancoDestinoCodigo, string cuentaDestino)
        {
            AsegurarEsquemaTransferencias();
            var bancos = ObtenerBancosRed(true);
            var bancoPropio = bancos.FirstOrDefault(b => b.EsPropio) ?? new BancoRedDto
            {
                CodigoBanco = "BANC01",
                NombreBanco = "Banco Nacional Central",
                EsPropio = true,
                LimiteDiario = 50000m,
                TransferidoHoy = 0m
            };

            string codDest = string.IsNullOrWhiteSpace(bancoDestinoCodigo) ? bancoPropio.CodigoBanco : bancoDestinoCodigo.Trim().ToUpper();
            bool esInterna = codDest.Equals(bancoPropio.CodigoBanco, StringComparison.OrdinalIgnoreCase);

            if (esInterna)
            {
                var (existe, _, numCuenta, _, titular) = VerificarCuentaLocal(cuentaDestino);
                if (!existe)
                {
                    return (CodigosRespuesta.CuentaInexistente, "La cuenta o tarjeta ingresada no existe o está inactiva en nuestro banco.", new DatosVerificarCuenta
                    {
                        Existe = false,
                        CuentaDestino = cuentaDestino,
                        BancoDestino = bancoPropio.CodigoBanco,
                        NombreBancoDestino = bancoPropio.NombreBanco
                    });
                }

                return (CodigosRespuesta.Aprobado, $"Cuenta verificada: {titular}", new DatosVerificarCuenta
                {
                    Existe = true,
                    CuentaDestino = numCuenta,
                    NombreTitular = titular,
                    BancoDestino = bancoPropio.CodigoBanco,
                    NombreBancoDestino = bancoPropio.NombreBanco,
                    CupoDisponibleOrigen = bancoPropio.DisponibleHoy,
                    CupoDisponibleDestino = bancoPropio.DisponibleHoy
                });
            }

            // Es de otro banco: buscar en catálogo BancoRed
            var bancoExterno = bancos.FirstOrDefault(b => b.CodigoBanco.Equals(codDest, StringComparison.OrdinalIgnoreCase));
            if (bancoExterno == null || !bancoExterno.Activo)
            {
                return (CodigosRespuesta.BancoNoAutorizado, $"El banco '{codDest}' no está registrado o se encuentra inactivo.", new DatosVerificarCuenta
                {
                    Existe = false,
                    CuentaDestino = cuentaDestino,
                    BancoDestino = codDest
                });
            }

            // 1. Intentar consultar al servidor del otro banco en tiempo real (TCP o SQL directo)
            var (alcanzable, existeRemoto, ctaRemota, titularRemoto, cupoRemoto, msjRemoto) =
                await ClienteInterbancario.VerificarCuentaRemotaAsync(bancoExterno, bancoPropio.CodigoBanco, cuentaDestino);

            if (alcanzable)
            {
                if (!existeRemoto)
                {
                    return (CodigosRespuesta.CuentaInexistente,
                        string.IsNullOrWhiteSpace(msjRemoto) ? $"La cuenta {cuentaDestino} no existe en {bancoExterno.NombreBanco}." : msjRemoto,
                        new DatosVerificarCuenta
                        {
                            Existe = false,
                            CuentaDestino = cuentaDestino,
                            BancoDestino = bancoExterno.CodigoBanco,
                            NombreBancoDestino = bancoExterno.NombreBanco,
                            CupoDisponibleOrigen = bancoPropio.DisponibleHoy,
                            CupoDisponibleDestino = cupoRemoto ?? bancoExterno.DisponibleHoy
                        });
                }

                return (CodigosRespuesta.Aprobado,
                    $"Usuario verificado en {bancoExterno.NombreBanco}: {titularRemoto}",
                    new DatosVerificarCuenta
                    {
                        Existe = true,
                        CuentaDestino = ctaRemota,
                        NombreTitular = titularRemoto,
                        BancoDestino = bancoExterno.CodigoBanco,
                        NombreBancoDestino = bancoExterno.NombreBanco,
                        CupoDisponibleOrigen = bancoPropio.DisponibleHoy,
                        CupoDisponibleDestino = cupoRemoto ?? bancoExterno.DisponibleHoy
                    });
            }

            // 2. Fallback local: verificar en tabla CuentaBancoExterno (cuando el servidor del otro banco aún no está en línea en el router)
            using var conn = ConexionBD.ObtenerConexion();
            conn.Open();

            string sqlExt = @"
                SELECT TOP 1 NumeroCuenta, NombreTitular
                FROM dbo.CuentaBancoExterno
                WHERE CodigoBanco = @Banco AND NumeroCuenta = @Cta AND Activa = 1";

            using var cmd = new SqlCommand(sqlExt, conn);
            cmd.Parameters.AddWithValue("@Banco", bancoExterno.CodigoBanco);
            cmd.Parameters.AddWithValue("@Cta", cuentaDestino.Trim());

            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                string cta = r["NumeroCuenta"].ToString()!;
                string tit = r["NombreTitular"].ToString()!;

                return (CodigosRespuesta.Aprobado,
                    $"Usuario verificado en {bancoExterno.NombreBanco}: {tit}",
                    new DatosVerificarCuenta
                    {
                        Existe = true,
                        CuentaDestino = cta,
                        NombreTitular = tit,
                        BancoDestino = bancoExterno.CodigoBanco,
                        NombreBancoDestino = bancoExterno.NombreBanco,
                        CupoDisponibleOrigen = bancoPropio.DisponibleHoy,
                        CupoDisponibleDestino = bancoExterno.DisponibleHoy
                    });
            }

            return (CodigosRespuesta.CuentaInexistente,
                $"No existe la cuenta '{cuentaDestino}' en {bancoExterno.NombreBanco}.",
                new DatosVerificarCuenta
                {
                    Existe = false,
                    CuentaDestino = cuentaDestino,
                    BancoDestino = bancoExterno.CodigoBanco,
                    NombreBancoDestino = bancoExterno.NombreBanco,
                    CupoDisponibleOrigen = bancoPropio.DisponibleHoy,
                    CupoDisponibleDestino = bancoExterno.DisponibleHoy
                });
        }

        /// <summary>
        /// Ejecuta una transferencia entre usuarios del mismo banco o una transferencia interbancaria coordinando
        /// el descuento del límite de ambos bancos y guardando el TokenTransferencia en nuestro banco, el otro banco y el usuario.
        /// </summary>
        public async Task<(string codigo, string mensaje, DatosTransferencia? datos)> EjecutarTransferenciaAsync(
            string codigoCajero,
            string tarjetaOrigen,
            bool esInterbancaria,
            string codigoBancoDestino,
            string cuentaDestinoInput,
            decimal monto,
            string concepto)
        {
            AsegurarEsquemaTransferencias();

            if (monto <= 0)
            {
                return (CodigosRespuesta.TransaccionInvalida, "El monto a transferir debe ser mayor a Q 0.00.", null);
            }

            var bancos = ObtenerBancosRed(true);
            var bancoPropio = bancos.FirstOrDefault(b => b.EsPropio) ?? new BancoRedDto
            {
                CodigoBanco = "BANC01",
                NombreBanco = "Banco Nacional Central",
                EsPropio = true,
                LimiteDiario = 50000m,
                TransferidoHoy = 0m
            };

            string codDest = (!esInterbancaria || string.IsNullOrWhiteSpace(codigoBancoDestino))
                ? bancoPropio.CodigoBanco
                : codigoBancoDestino.Trim().ToUpper();

            bool internaReal = !esInterbancaria || codDest.Equals(bancoPropio.CodigoBanco, StringComparison.OrdinalIgnoreCase);
            string tokenTransferencia = GenerarTokenTransferenciaUnico();

            if (internaReal)
            {
                return EjecutarTransferenciaInterna(
                    codigoCajero,
                    tarjetaOrigen,
                    cuentaDestinoInput.Trim(),
                    monto,
                    concepto,
                    tokenTransferencia,
                    bancoPropio);
            }
            else
            {
                var bancoDestino = bancos.FirstOrDefault(b => b.CodigoBanco.Equals(codDest, StringComparison.OrdinalIgnoreCase));
                if (bancoDestino == null || !bancoDestino.Activo)
                {
                    return (CodigosRespuesta.BancoNoAutorizado, $"El banco destino '{codDest}' no está habilitado.", null);
                }

                return await EjecutarTransferenciaInterbancariaCoordinadaAsync(
                    codigoCajero,
                    tarjetaOrigen,
                    cuentaDestinoInput.Trim(),
                    monto,
                    concepto,
                    tokenTransferencia,
                    bancoPropio,
                    bancoDestino);
            }
        }

        private (string codigo, string mensaje, DatosTransferencia? datos) EjecutarTransferenciaInterna(
            string codigoCajero,
            string tarjetaOrigen,
            string cuentaDestinoInput,
            decimal monto,
            string concepto,
            string tokenTransferencia,
            BancoRedDto bancoPropio)
        {
            using var conn = ConexionBD.ObtenerConexion();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                // Obtener cajero
                int? idCajero = null;
                using (var cmdC = new SqlCommand("SELECT IdCajero FROM dbo.Cajero WHERE Codigo = @Cod", conn, tx))
                {
                    cmdC.Parameters.AddWithValue("@Cod", codigoCajero);
                    var val = cmdC.ExecuteScalar();
                    if (val != null && val != DBNull.Value) idCajero = Convert.ToInt32(val);
                }

                // 1. Obtener y bloquear usuario emisor
                int idUsuarioOrigen = 0, idTarjetaOrigen = 0;
                string nombreOrigen = "", cuentaOrigen = "";
                decimal saldoOrigen = 0m;

                string sqlOrigen = @"
                    SELECT u.IdUsuario, t.IdTarjeta, u.Nombre, 
                           ISNULL(u.NumeroCuenta, CONCAT('1001', RIGHT('000000' + CAST(u.IdUsuario AS VARCHAR(10)), 6))) AS NumeroCuenta,
                           u.Saldo
                    FROM dbo.Usuario u WITH (UPDLOCK, ROWLOCK)
                    JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario
                    WHERE t.NumeroTarjeta = @Tar AND t.Activa = 1 AND u.Activo = 1 AND u.Estado = 'Activo'";

                using (var cmdO = new SqlCommand(sqlOrigen, conn, tx))
                {
                    cmdO.Parameters.AddWithValue("@Tar", tarjetaOrigen);
                    using var r = cmdO.ExecuteReader();
                    if (r.Read())
                    {
                        idUsuarioOrigen = Convert.ToInt32(r["IdUsuario"]);
                        idTarjetaOrigen = Convert.ToInt32(r["IdTarjeta"]);
                        nombreOrigen = r["Nombre"].ToString()!;
                        cuentaOrigen = r["NumeroCuenta"].ToString()!;
                        saldoOrigen = Convert.ToDecimal(r["Saldo"]);
                    }
                }

                if (idUsuarioOrigen == 0)
                {
                    tx.Rollback();
                    return (CodigosRespuesta.TarjetaBloqueadaOInactiva, "La cuenta o tarjeta de origen no está activa.", null);
                }

                // 2. Obtener y bloquear usuario receptor en nuestro banco
                int idUsuarioDest = 0;
                int? idTarjetaDest = null;
                string nombreDest = "", cuentaDest = "";

                string sqlDest = @"
                    SELECT TOP 1 u.IdUsuario, t.IdTarjeta, u.Nombre,
                           ISNULL(u.NumeroCuenta, CONCAT('1001', RIGHT('000000' + CAST(u.IdUsuario AS VARCHAR(10)), 6))) AS NumeroCuenta
                    FROM dbo.Usuario u WITH (UPDLOCK, ROWLOCK)
                    LEFT JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario AND t.Activa = 1
                    WHERE u.Activo = 1 AND u.Estado = 'Activo'
                      AND (u.NumeroCuenta = @Cta OR t.NumeroTarjeta = @Cta)";

                using (var cmdD = new SqlCommand(sqlDest, conn, tx))
                {
                    cmdD.Parameters.AddWithValue("@Cta", cuentaDestinoInput);
                    using var r = cmdD.ExecuteReader();
                    if (r.Read())
                    {
                        idUsuarioDest = Convert.ToInt32(r["IdUsuario"]);
                        if (r["IdTarjeta"] != DBNull.Value) idTarjetaDest = Convert.ToInt32(r["IdTarjeta"]);
                        nombreDest = r["Nombre"].ToString()!;
                        cuentaDest = r["NumeroCuenta"].ToString()!;
                    }
                }

                if (idUsuarioDest == 0)
                {
                    tx.Rollback();
                    return (CodigosRespuesta.CuentaInexistente, "El usuario o número de cuenta destino no existe en este banco.", null);
                }

                if (idUsuarioDest == idUsuarioOrigen)
                {
                    tx.Rollback();
                    return (CodigosRespuesta.TransaccionInvalida, "No puede transferir fondos a su misma cuenta.", null);
                }

                if (saldoOrigen < monto)
                {
                    tx.Rollback();
                    return (CodigosRespuesta.SaldoInsuficiente, "Saldo insuficiente para realizar la transferencia.", null);
                }

                // 3. Debitar a origen y acreditar a destino
                using (var cmdDeb = new SqlCommand("UPDATE dbo.Usuario SET Saldo = Saldo - @Monto WHERE IdUsuario = @Id", conn, tx))
                {
                    cmdDeb.Parameters.AddWithValue("@Monto", monto);
                    cmdDeb.Parameters.AddWithValue("@Id", idUsuarioOrigen);
                    cmdDeb.ExecuteNonQuery();
                }

                using (var cmdAcr = new SqlCommand("UPDATE dbo.Usuario SET Saldo = Saldo + @Monto WHERE IdUsuario = @Id", conn, tx))
                {
                    cmdAcr.Parameters.AddWithValue("@Monto", monto);
                    cmdAcr.Parameters.AddWithValue("@Id", idUsuarioDest);
                    cmdAcr.ExecuteNonQuery();
                }

                decimal nuevoSaldoOrigen = saldoOrigen - monto;

                // 4. Guardar TokenTransferencia en la tabla común TransferenciaBancaria
                string sqlInsTrf = @"
                    INSERT INTO dbo.TransferenciaBancaria
                        (TokenTransferencia, TipoTransferencia, Direccion, BancoOrigen, CuentaOrigen, NombreOrigen,
                         BancoDestino, CuentaDestino, NombreDestino, Monto, Moneda, Concepto, Estado, CodigoRespuesta,
                         MensajeRespuesta, CupoRestanteBancoOrigen, CupoRestanteBancoDestino, IdUsuarioLocal, IdCajero, FechaCreacion, FechaConfirmacion)
                    VALUES
                        (@Token, 'INTERNA', 'INTERNA', @Banco, @CtaOrig, @NomOrig,
                         @Banco, @CtaDest, @NomDest, @Monto, 'GTQ', @Concepto, 'COMPLETADA', '00',
                         'Transferencia interna exitosa', @Cupo, @Cupo, @IdUsr, @Cajero, GETDATE(), GETDATE())";

                using (var cmdTrf = new SqlCommand(sqlInsTrf, conn, tx))
                {
                    cmdTrf.Parameters.AddWithValue("@Token", tokenTransferencia);
                    cmdTrf.Parameters.AddWithValue("@Banco", bancoPropio.CodigoBanco);
                    cmdTrf.Parameters.AddWithValue("@CtaOrig", cuentaOrigen);
                    cmdTrf.Parameters.AddWithValue("@NomOrig", nombreOrigen);
                    cmdTrf.Parameters.AddWithValue("@CtaDest", cuentaDest);
                    cmdTrf.Parameters.AddWithValue("@NomDest", nombreDest);
                    cmdTrf.Parameters.AddWithValue("@Monto", monto);
                    cmdTrf.Parameters.AddWithValue("@Concepto", string.IsNullOrWhiteSpace(concepto) ? "Transferencia entre usuarios" : concepto.Trim());
                    cmdTrf.Parameters.AddWithValue("@Cupo", bancoPropio.DisponibleHoy);
                    cmdTrf.Parameters.AddWithValue("@IdUsr", idUsuarioOrigen);
                    cmdTrf.Parameters.AddWithValue("@Cajero", codigoCajero);
                    cmdTrf.ExecuteNonQuery();
                }

                // 5. Registrar en Transaccion (historial del usuario emisor y del usuario receptor con el mismo TokenTransferencia)
                int idTxEmisor;
                string sqlTxOrig = @"
                    INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, TokenTransferencia, Fecha)
                    OUTPUT INSERTED.IdTransaccion
                    VALUES (@IdU, @IdT, @IdC, 'TRANSFERENCIA_ENVIADA', @Monto, '00', @Detalle, @Token, GETDATE())";

                using (var cmdTxO = new SqlCommand(sqlTxOrig, conn, tx))
                {
                    cmdTxO.Parameters.AddWithValue("@IdU", idUsuarioOrigen);
                    cmdTxO.Parameters.AddWithValue("@IdT", idTarjetaOrigen);
                    cmdTxO.Parameters.AddWithValue("@IdC", (object?)idCajero ?? DBNull.Value);
                    cmdTxO.Parameters.AddWithValue("@Monto", monto);
                    cmdTxO.Parameters.AddWithValue("@Detalle", $"A {nombreDest} ({cuentaDest}) | Token: {tokenTransferencia}");
                    cmdTxO.Parameters.AddWithValue("@Token", tokenTransferencia);
                    idTxEmisor = Convert.ToInt32(cmdTxO.ExecuteScalar());
                }

                string sqlTxDest = @"
                    INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, TokenTransferencia, Fecha)
                    VALUES (@IdU, @IdT, @IdC, 'TRANSFERENCIA_RECIBIDA', @Monto, '00', @Detalle, @Token, GETDATE())";

                using (var cmdTxD = new SqlCommand(sqlTxDest, conn, tx))
                {
                    cmdTxD.Parameters.AddWithValue("@IdU", idUsuarioDest);
                    cmdTxD.Parameters.AddWithValue("@IdT", (object?)idTarjetaDest ?? DBNull.Value);
                    cmdTxD.Parameters.AddWithValue("@IdC", (object?)idCajero ?? DBNull.Value);
                    cmdTxD.Parameters.AddWithValue("@Monto", monto);
                    cmdTxD.Parameters.AddWithValue("@Detalle", $"De {nombreOrigen} ({cuentaOrigen}) | Token: {tokenTransferencia}");
                    cmdTxD.Parameters.AddWithValue("@Token", tokenTransferencia);
                    cmdTxD.ExecuteNonQuery();
                }

                tx.Commit();

                RegistrarEnBitacora("Cajero", "Transferencia Interna",
                    $"Token [{tokenTransferencia}]: {nombreOrigen} ({cuentaOrigen}) transfirió Q {monto:N2} a {nombreDest} ({cuentaDest}) en {codigoCajero}.");

                return (CodigosRespuesta.Aprobado, "Transferencia entre usuarios completada exitosamente.", new DatosTransferencia
                {
                    IdTransaccion = idTxEmisor,
                    TokenTransferencia = tokenTransferencia,
                    EsInterbancaria = false,
                    BancoOrigen = bancoPropio.CodigoBanco,
                    NombreBancoOrigen = bancoPropio.NombreBanco,
                    CuentaOrigen = cuentaOrigen,
                    NombreOrigen = nombreOrigen,
                    BancoDestino = bancoPropio.CodigoBanco,
                    NombreBancoDestino = bancoPropio.NombreBanco,
                    CuentaDestino = cuentaDest,
                    NombreDestino = nombreDest,
                    Monto = monto,
                    Concepto = string.IsNullOrWhiteSpace(concepto) ? "Transferencia entre usuarios" : concepto.Trim(),
                    Saldo = nuevoSaldoOrigen,
                    CupoRestanteBancoOrigen = bancoPropio.DisponibleHoy,
                    CupoRestanteBancoDestino = bancoPropio.DisponibleHoy
                });
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                return (CodigosRespuesta.ErrorDelSistema, "Error al procesar transferencia interna: " + ex.Message, null);
            }
        }

        private async Task<(string codigo, string mensaje, DatosTransferencia? datos)> EjecutarTransferenciaInterbancariaCoordinadaAsync(
            string codigoCajero,
            string tarjetaOrigen,
            string cuentaDestinoInput,
            decimal monto,
            string concepto,
            string tokenTransferencia,
            BancoRedDto bancoPropio,
            BancoRedDto bancoDestino)
        {
            // 1. Primero verificar que existe el usuario en el banco destino seleccionado
            var (codVerif, msjVerif, datosVerif) = await VerificarCuentaDestinoAsync(bancoDestino.CodigoBanco, cuentaDestinoInput);
            if (codVerif != CodigosRespuesta.Aprobado || !datosVerif.Existe)
            {
                return (codVerif, msjVerif, null);
            }

            string nombreDestino = datosVerif.NombreTitular;
            string cuentaDestinoFinal = datosVerif.CuentaDestino;

            // 2. Validar saldo del usuario emisor y cupo coordinado de ambos bancos en una transacción local
            int idUsuarioOrigen = 0, idTarjetaOrigen = 0;
            int? idCajero = null;
            string nombreOrigen = "", cuentaOrigen = "";
            decimal saldoOrigen = 0m;
            decimal cupoDisponiblePropio = 0m, cupoDisponibleDestinoLocal = 0m;

            using (var conn = ConexionBD.ObtenerConexion())
            {
                conn.Open();
                using var tx = conn.BeginTransaction();
                try
                {
                    using (var cmdC = new SqlCommand("SELECT IdCajero FROM dbo.Cajero WHERE Codigo = @Cod", conn, tx))
                    {
                        cmdC.Parameters.AddWithValue("@Cod", codigoCajero);
                        var val = cmdC.ExecuteScalar();
                        if (val != null && val != DBNull.Value) idCajero = Convert.ToInt32(val);
                    }

                    // Obtener usuario origen
                    string sqlOrigen = @"
                        SELECT u.IdUsuario, t.IdTarjeta, u.Nombre, 
                               ISNULL(u.NumeroCuenta, CONCAT('1001', RIGHT('000000' + CAST(u.IdUsuario AS VARCHAR(10)), 6))) AS NumeroCuenta,
                               u.Saldo
                        FROM dbo.Usuario u WITH (UPDLOCK, ROWLOCK)
                        JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario
                        WHERE t.NumeroTarjeta = @Tar AND t.Activa = 1 AND u.Activo = 1 AND u.Estado = 'Activo'";

                    using (var cmdO = new SqlCommand(sqlOrigen, conn, tx))
                    {
                        cmdO.Parameters.AddWithValue("@Tar", tarjetaOrigen);
                        using var r = cmdO.ExecuteReader();
                        if (r.Read())
                        {
                            idUsuarioOrigen = Convert.ToInt32(r["IdUsuario"]);
                            idTarjetaOrigen = Convert.ToInt32(r["IdTarjeta"]);
                            nombreOrigen = r["Nombre"].ToString()!;
                            cuentaOrigen = r["NumeroCuenta"].ToString()!;
                            saldoOrigen = Convert.ToDecimal(r["Saldo"]);
                        }
                    }

                    if (idUsuarioOrigen == 0)
                    {
                        tx.Rollback();
                        return (CodigosRespuesta.TarjetaBloqueadaOInactiva, "Usuario emisor no encontrado o inactivo.", null);
                    }

                    if (saldoOrigen < monto)
                    {
                        tx.Rollback();
                        return (CodigosRespuesta.SaldoInsuficiente, "Saldo insuficiente en su cuenta para realizar la transferencia interbancaria.", null);
                    }

                    // Verificar cupo diario de NUESTRO banco y del OTRO banco (coordinación de límites de los dos bancos)
                    string sqlCupos = @"
                        SELECT CodigoBanco, LimiteDiario, TransferidoHoy
                        FROM dbo.BancoRed WITH (UPDLOCK)
                        WHERE CodigoBanco IN (@BOrig, @BDest)";

                    using (var cmdCup = new SqlCommand(sqlCupos, conn, tx))
                    {
                        cmdCup.Parameters.AddWithValue("@BOrig", bancoPropio.CodigoBanco);
                        cmdCup.Parameters.AddWithValue("@BDest", bancoDestino.CodigoBanco);
                        using var r = cmdCup.ExecuteReader();
                        while (r.Read())
                        {
                            string codB = r["CodigoBanco"].ToString()!;
                            decimal limB = Convert.ToDecimal(r["LimiteDiario"]);
                            decimal trfB = Convert.ToDecimal(r["TransferidoHoy"]);
                            if (codB.Equals(bancoPropio.CodigoBanco, StringComparison.OrdinalIgnoreCase))
                                cupoDisponiblePropio = limB - trfB;
                            if (codB.Equals(bancoDestino.CodigoBanco, StringComparison.OrdinalIgnoreCase))
                                cupoDisponibleDestinoLocal = limB - trfB;
                        }
                    }

                    if (monto > cupoDisponiblePropio)
                    {
                        tx.Rollback();
                        return (CodigosRespuesta.ExcedeCupoInterbancario,
                            $"El monto (Q {monto:N2}) excede el límite diario interbancario disponible de nuestro banco ({bancoPropio.CodigoBanco}: Q {Math.Max(0, cupoDisponiblePropio):N2}).",
                            null);
                    }

                    if (monto > cupoDisponibleDestinoLocal)
                    {
                        tx.Rollback();
                        return (CodigosRespuesta.ExcedeCupoInterbancario,
                            $"El monto (Q {monto:N2}) excede el límite diario interbancario disponible del banco destino ({bancoDestino.CodigoBanco}: Q {Math.Max(0, cupoDisponibleDestinoLocal):N2}).",
                            null);
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    return (CodigosRespuesta.ErrorDelSistema, "Error al preparar transferencia interbancaria: " + ex.Message, null);
                }
            }

            // 3. Coordinar con el servidor del otro banco enviando el TokenTransferencia (si está conectado por router o SQL remoto)
            var solicitudIb = new SolicitudIbTransferir
            {
                TokenTransferencia = tokenTransferencia,
                BancoOrigen = bancoPropio.CodigoBanco,
                CuentaOrigen = cuentaOrigen,
                NombreOrigen = nombreOrigen,
                BancoDestino = bancoDestino.CodigoBanco,
                CuentaDestino = cuentaDestinoFinal,
                NombreDestino = nombreDestino,
                Monto = monto,
                Moneda = "GTQ",
                Concepto = string.IsNullOrWhiteSpace(concepto) ? "Transferencia interbancaria" : concepto.Trim()
            };

            var (alcanzableRemoto, aprobadaRemoto, codRemoto, msjRemoto, nomRemotoConfirmado, cupoRestanteRemoto) =
                await ClienteInterbancario.EnviarTransferenciaRemotaAsync(bancoDestino, solicitudIb);

            if (alcanzableRemoto && !aprobadaRemoto)
            {
                // El banco remoto respondió rechazando la operación (ej. sin cupo o cuenta bloqueada)
                return (codRemoto, $"Rechazado por {bancoDestino.NombreBanco}: {msjRemoto}", null);
            }

            if (!string.IsNullOrWhiteSpace(nomRemotoConfirmado))
            {
                nombreDestino = nomRemotoConfirmado;
            }

            // 4. Confirmar en nuestra base de datos:
            //    - Descontar saldo al usuario origen
            //    - Restar el monto al límite de los DOS bancos (BancoOrigen y BancoDestino) coordinándose
            //    - Guardar el TokenTransferencia en TransferenciaBancaria (para ambos bancos) y en Transaccion (para el usuario)
            using (var conn = ConexionBD.ObtenerConexion())
            {
                conn.Open();
                using var tx = conn.BeginTransaction();
                try
                {
                    // Descontar saldo al usuario emisor
                    using (var cmdDeb = new SqlCommand("UPDATE dbo.Usuario SET Saldo = Saldo - @Monto WHERE IdUsuario = @Id AND Saldo >= @Monto", conn, tx))
                    {
                        cmdDeb.Parameters.AddWithValue("@Monto", monto);
                        cmdDeb.Parameters.AddWithValue("@Id", idUsuarioOrigen);
                        int rows = cmdDeb.ExecuteNonQuery();
                        if (rows == 0)
                        {
                            tx.Rollback();
                            return (CodigosRespuesta.SaldoInsuficiente, "Saldo insuficiente al confirmar la transferencia.", null);
                        }
                    }

                    // Restar monto al límite diario de nuestro banco (BancoOrigen) y del otro banco (BancoDestino)
                    string sqlRestarLimites = @"
                        UPDATE dbo.BancoRed
                        SET TransferidoHoy = TransferidoHoy + @Monto
                        WHERE CodigoBanco IN (@BOrig, @BDest)";

                    using (var cmdLim = new SqlCommand(sqlRestarLimites, conn, tx))
                    {
                        cmdLim.Parameters.AddWithValue("@Monto", monto);
                        cmdLim.Parameters.AddWithValue("@BOrig", bancoPropio.CodigoBanco);
                        cmdLim.Parameters.AddWithValue("@BDest", bancoDestino.CodigoBanco);
                        cmdLim.ExecuteNonQuery();
                    }

                    // Si se usó la cuenta externa simulada local, acreditar su saldo simulado también
                    if (!alcanzableRemoto)
                    {
                        using var cmdSim = new SqlCommand(@"
                            UPDATE dbo.CuentaBancoExterno 
                            SET SaldoSimulado = SaldoSimulado + @Monto 
                            WHERE CodigoBanco = @BDest AND NumeroCuenta = @CtaDest", conn, tx);
                        cmdSim.Parameters.AddWithValue("@Monto", monto);
                        cmdSim.Parameters.AddWithValue("@BDest", bancoDestino.CodigoBanco);
                        cmdSim.Parameters.AddWithValue("@CtaDest", cuentaDestinoFinal);
                        cmdSim.ExecuteNonQuery();
                    }

                    decimal nuevoCupoOrigen = Math.Max(0m, cupoDisponiblePropio - monto);
                    decimal nuevoCupoDestino = cupoRestanteRemoto ?? Math.Max(0m, cupoDisponibleDestinoLocal - monto);
                    decimal nuevoSaldo = saldoOrigen - monto;

                    // Guardar en la tabla común TransferenciaBancaria con el TokenTransferencia
                    string sqlInsTrf = @"
                        INSERT INTO dbo.TransferenciaBancaria
                            (TokenTransferencia, TipoTransferencia, Direccion, BancoOrigen, CuentaOrigen, NombreOrigen,
                             BancoDestino, CuentaDestino, NombreDestino, Monto, Moneda, Concepto, Estado, CodigoRespuesta,
                             MensajeRespuesta, CupoRestanteBancoOrigen, CupoRestanteBancoDestino, IdUsuarioLocal, IdCajero, FechaCreacion, FechaConfirmacion)
                        VALUES
                            (@Token, 'INTERBANCARIA', 'SALIENTE', @BOrig, @CtaOrig, @NomOrig,
                             @BDest, @CtaDest, @NomDest, @Monto, 'GTQ', @Concepto, 'COMPLETADA', '00',
                             @Msj, @CupoOrig, @CupoDest, @IdUsr, @Cajero, GETDATE(), GETDATE())";

                    string modoCoordinacion = alcanzableRemoto
                        ? $"Coordinada en red con {bancoDestino.CodigoBanco} (Token guardado en ambos bancos)"
                        : $"Coordinada y registrada con token para {bancoDestino.CodigoBanco}";

                    using (var cmdTrf = new SqlCommand(sqlInsTrf, conn, tx))
                    {
                        cmdTrf.Parameters.AddWithValue("@Token", tokenTransferencia);
                        cmdTrf.Parameters.AddWithValue("@BOrig", bancoPropio.CodigoBanco);
                        cmdTrf.Parameters.AddWithValue("@CtaOrig", cuentaOrigen);
                        cmdTrf.Parameters.AddWithValue("@NomOrig", nombreOrigen);
                        cmdTrf.Parameters.AddWithValue("@BDest", bancoDestino.CodigoBanco);
                        cmdTrf.Parameters.AddWithValue("@CtaDest", cuentaDestinoFinal);
                        cmdTrf.Parameters.AddWithValue("@NomDest", nombreDestino);
                        cmdTrf.Parameters.AddWithValue("@Monto", monto);
                        cmdTrf.Parameters.AddWithValue("@Concepto", string.IsNullOrWhiteSpace(concepto) ? "Transferencia interbancaria" : concepto.Trim());
                        cmdTrf.Parameters.AddWithValue("@Msj", modoCoordinacion);
                        cmdTrf.Parameters.AddWithValue("@CupoOrig", nuevoCupoOrigen);
                        cmdTrf.Parameters.AddWithValue("@CupoDest", nuevoCupoDestino);
                        cmdTrf.Parameters.AddWithValue("@IdUsr", idUsuarioOrigen);
                        cmdTrf.Parameters.AddWithValue("@Cajero", codigoCajero);
                        cmdTrf.ExecuteNonQuery();
                    }

                    // Guardar también en el historial del usuario (dbo.Transaccion) con su TokenTransferencia
                    int idTx;
                    string sqlTx = @"
                        INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, TokenTransferencia, Fecha)
                        OUTPUT INSERTED.IdTransaccion
                        VALUES (@IdU, @IdT, @IdC, 'TRANSFERENCIA_INTERBANCARIA', @Monto, '00', @Detalle, @Token, GETDATE())";

                    using (var cmdTx = new SqlCommand(sqlTx, conn, tx))
                    {
                        cmdTx.Parameters.AddWithValue("@IdU", idUsuarioOrigen);
                        cmdTx.Parameters.AddWithValue("@IdT", idTarjetaOrigen);
                        cmdTx.Parameters.AddWithValue("@IdC", (object?)idCajero ?? DBNull.Value);
                        cmdTx.Parameters.AddWithValue("@Monto", monto);
                        cmdTx.Parameters.AddWithValue("@Detalle", $"A {bancoDestino.CodigoBanco} - {nombreDestino} ({cuentaDestinoFinal}) | Token: {tokenTransferencia}");
                        cmdTx.Parameters.AddWithValue("@Token", tokenTransferencia);
                        idTx = Convert.ToInt32(cmdTx.ExecuteScalar());
                    }

                    tx.Commit();

                    RegistrarEnBitacora("Servidor", "Transferencia Interbancaria",
                        $"Token [{tokenTransferencia}]: {bancoPropio.CodigoBanco} ({cuentaOrigen}) -> {bancoDestino.CodigoBanco} ({cuentaDestinoFinal}) por Q {monto:N2}. " +
                        $"Cupo restante {bancoPropio.CodigoBanco}: Q {nuevoCupoOrigen:N2} | Cupo restante {bancoDestino.CodigoBanco}: Q {nuevoCupoDestino:N2}.");

                    return (CodigosRespuesta.Aprobado, "Transferencia interbancaria aprobada y coordinada entre ambos bancos.", new DatosTransferencia
                    {
                        IdTransaccion = idTx,
                        TokenTransferencia = tokenTransferencia,
                        EsInterbancaria = true,
                        BancoOrigen = bancoPropio.CodigoBanco,
                        NombreBancoOrigen = bancoPropio.NombreBanco,
                        CuentaOrigen = cuentaOrigen,
                        NombreOrigen = nombreOrigen,
                        BancoDestino = bancoDestino.CodigoBanco,
                        NombreBancoDestino = bancoDestino.NombreBanco,
                        CuentaDestino = cuentaDestinoFinal,
                        NombreDestino = nombreDestino,
                        Monto = monto,
                        Concepto = string.IsNullOrWhiteSpace(concepto) ? "Transferencia interbancaria" : concepto.Trim(),
                        Saldo = nuevoSaldo,
                        CupoRestanteBancoOrigen = nuevoCupoOrigen,
                        CupoRestanteBancoDestino = nuevoCupoDestino
                    });
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    return (CodigosRespuesta.ErrorDelSistema, "Error al registrar transferencia interbancaria: " + ex.Message, null);
                }
            }
        }

        /// <summary>
        /// Procesa una transferencia interbancaria ENTRANTE enviada por el servidor de otro banco (mensaje IB_TRANSFERIR).
        /// Verifica que exista el usuario en nuestro banco, resta el monto al límite diario de ambos bancos (nuestro banco y el banco emisor),
        /// acredita el saldo al usuario receptor y guarda el MISMO TokenTransferencia tanto en TransferenciaBancaria como en Transaccion del usuario.
        /// </summary>
        public (string codigo, string mensaje, DatosIbTransferir? datos) RecibirTransferenciaInterbancariaEntrante(SolicitudIbTransferir req)
        {
            AsegurarEsquemaTransferencias();

            if (req.Monto <= 0 || string.IsNullOrWhiteSpace(req.TokenTransferencia))
            {
                return (CodigosRespuesta.TransaccionInvalida, "Monto o token de transferencia inválido.", null);
            }

            var (codPropio, nomPropio) = ObtenerInfoBancoPropio();

            using var conn = ConexionBD.ObtenerConexion();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                // 1. Verificar idempotencia: que el TokenTransferencia no esté duplicado
                using (var cmdDup = new SqlCommand("SELECT COUNT(*) FROM dbo.TransferenciaBancaria WHERE TokenTransferencia = @Tok", conn, tx))
                {
                    cmdDup.Parameters.AddWithValue("@Tok", req.TokenTransferencia.Trim());
                    int existeTok = Convert.ToInt32(cmdDup.ExecuteScalar());
                    if (existeTok > 0)
                    {
                        tx.Rollback();
                        return (CodigosRespuesta.TransaccionDuplicada, $"El token de transferencia '{req.TokenTransferencia}' ya fue registrado previamente.", null);
                    }
                }

                // 2. Verificar que exista el usuario destino en nuestro banco
                int idUsuarioDest = 0;
                int? idTarjetaDest = null;
                string nombreDest = "", cuentaDest = "";

                string sqlUsr = @"
                    SELECT TOP 1 u.IdUsuario, t.IdTarjeta, u.Nombre,
                           ISNULL(u.NumeroCuenta, CONCAT('1001', RIGHT('000000' + CAST(u.IdUsuario AS VARCHAR(10)), 6))) AS NumeroCuenta
                    FROM dbo.Usuario u WITH (UPDLOCK, ROWLOCK)
                    LEFT JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario AND t.Activa = 1
                    WHERE u.Activo = 1 AND u.Estado = 'Activo'
                      AND (u.NumeroCuenta = @Cta OR t.NumeroTarjeta = @Cta)";

                using (var cmdU = new SqlCommand(sqlUsr, conn, tx))
                {
                    cmdU.Parameters.AddWithValue("@Cta", req.CuentaDestino.Trim());
                    using var r = cmdU.ExecuteReader();
                    if (r.Read())
                    {
                        idUsuarioDest = Convert.ToInt32(r["IdUsuario"]);
                        if (r["IdTarjeta"] != DBNull.Value) idTarjetaDest = Convert.ToInt32(r["IdTarjeta"]);
                        nombreDest = r["Nombre"].ToString()!;
                        cuentaDest = r["NumeroCuenta"].ToString()!;
                    }
                }

                if (idUsuarioDest == 0)
                {
                    tx.Rollback();
                    return (CodigosRespuesta.CuentaInexistente, $"La cuenta '{req.CuentaDestino}' no existe o está inactiva en {nomPropio}.", null);
                }

                // 3. Validar y descontar del límite diario de nuestro banco y del banco emisor
                decimal cupoDispPropio = 50000m;
                string sqlCheckPropio = @"
                    UPDATE dbo.BancoRed
                    SET TransferidoHoy = CASE WHEN FechaUltimoReset < CAST(GETDATE() AS DATE) THEN 0 ELSE TransferidoHoy END,
                        FechaUltimoReset = CAST(GETDATE() AS DATE)
                    WHERE EsPropio = 1 OR CodigoBanco IN (@BOrig, @BDest);

                    SELECT TOP 1 (LimiteDiario - TransferidoHoy) AS Disponible
                    FROM dbo.BancoRed WITH (UPDLOCK)
                    WHERE EsPropio = 1 OR CodigoBanco = @BDest;";

                using (var cmdCp = new SqlCommand(sqlCheckPropio, conn, tx))
                {
                    cmdCp.Parameters.AddWithValue("@BOrig", req.BancoOrigen.Trim().ToUpper());
                    cmdCp.Parameters.AddWithValue("@BDest", codPropio);
                    var val = cmdCp.ExecuteScalar();
                    if (val != null && val != DBNull.Value) cupoDispPropio = Convert.ToDecimal(val);
                }

                if (req.Monto > cupoDispPropio)
                {
                    tx.Rollback();
                    return (CodigosRespuesta.ExcedeCupoInterbancario,
                        $"El monto excede el límite interbancario diario disponible en {codPropio} (Disponible: Q {Math.Max(0, cupoDispPropio):N2}).",
                        null);
                }

                // Restar monto al límite de ambos bancos en nuestro catálogo
                using (var cmdUpdCupo = new SqlCommand(@"
                    UPDATE dbo.BancoRed
                    SET TransferidoHoy = TransferidoHoy + @Monto
                    WHERE EsPropio = 1 OR CodigoBanco IN (@BOrig, @BDest)", conn, tx))
                {
                    cmdUpdCupo.Parameters.AddWithValue("@Monto", req.Monto);
                    cmdUpdCupo.Parameters.AddWithValue("@BOrig", req.BancoOrigen.Trim().ToUpper());
                    cmdUpdCupo.Parameters.AddWithValue("@BDest", codPropio);
                    cmdUpdCupo.ExecuteNonQuery();
                }

                decimal cupoRestantePropio = Math.Max(0m, cupoDispPropio - req.Monto);

                // 4. Acreditar saldo al usuario receptor
                using (var cmdAcr = new SqlCommand("UPDATE dbo.Usuario SET Saldo = Saldo + @Monto WHERE IdUsuario = @Id", conn, tx))
                {
                    cmdAcr.Parameters.AddWithValue("@Monto", req.Monto);
                    cmdAcr.Parameters.AddWithValue("@Id", idUsuarioDest);
                    cmdAcr.ExecuteNonQuery();
                }

                // 5. Guardar el mismo TokenTransferencia en nuestra tabla común TransferenciaBancaria
                string sqlInsTrf = @"
                    INSERT INTO dbo.TransferenciaBancaria
                        (TokenTransferencia, TipoTransferencia, Direccion, BancoOrigen, CuentaOrigen, NombreOrigen,
                         BancoDestino, CuentaDestino, NombreDestino, Monto, Moneda, Concepto, Estado, CodigoRespuesta,
                         MensajeRespuesta, CupoRestanteBancoDestino, IdUsuarioLocal, FechaCreacion, FechaConfirmacion)
                    VALUES
                        (@Token, 'INTERBANCARIA', 'ENTRANTE', @BOrig, @CtaOrig, @NomOrig,
                         @BDest, @CtaDest, @NomDest, @Monto, @Moneda, @Concepto, 'COMPLETADA', '00',
                         'Transferencia interbancaria entrante acreditada', @CupoDest, @IdUsr, GETDATE(), GETDATE())";

                using (var cmdTrf = new SqlCommand(sqlInsTrf, conn, tx))
                {
                    cmdTrf.Parameters.AddWithValue("@Token", req.TokenTransferencia.Trim());
                    cmdTrf.Parameters.AddWithValue("@BOrig", req.BancoOrigen.Trim().ToUpper());
                    cmdTrf.Parameters.AddWithValue("@CtaOrig", req.CuentaOrigen.Trim());
                    cmdTrf.Parameters.AddWithValue("@NomOrig", req.NombreOrigen.Trim());
                    cmdTrf.Parameters.AddWithValue("@BDest", codPropio);
                    cmdTrf.Parameters.AddWithValue("@CtaDest", cuentaDest);
                    cmdTrf.Parameters.AddWithValue("@NomDest", nombreDest);
                    cmdTrf.Parameters.AddWithValue("@Monto", req.Monto);
                    cmdTrf.Parameters.AddWithValue("@Moneda", string.IsNullOrWhiteSpace(req.Moneda) ? "GTQ" : req.Moneda);
                    cmdTrf.Parameters.AddWithValue("@Concepto", req.Concepto ?? "Transferencia interbancaria recibida");
                    cmdTrf.Parameters.AddWithValue("@CupoDest", cupoRestantePropio);
                    cmdTrf.Parameters.AddWithValue("@IdUsr", idUsuarioDest);
                    cmdTrf.ExecuteNonQuery();
                }

                // 6. Guardar en el historial del usuario receptor (dbo.Transaccion) con el TokenTransferencia
                string sqlTx = @"
                    INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, TokenTransferencia, Fecha)
                    VALUES (@IdU, @IdT, NULL, 'TRANSFERENCIA_RECIBIDA', @Monto, '00', @Detalle, @Token, GETDATE())";

                using (var cmdTx = new SqlCommand(sqlTx, conn, tx))
                {
                    cmdTx.Parameters.AddWithValue("@IdU", idUsuarioDest);
                    cmdTx.Parameters.AddWithValue("@IdT", (object?)idTarjetaDest ?? DBNull.Value);
                    cmdTx.Parameters.AddWithValue("@Monto", req.Monto);
                    cmdTx.Parameters.AddWithValue("@Detalle", $"De {req.BancoOrigen}: {req.NombreOrigen} ({req.CuentaOrigen}) | Token: {req.TokenTransferencia}");
                    cmdTx.Parameters.AddWithValue("@Token", req.TokenTransferencia.Trim());
                    cmdTx.ExecuteNonQuery();
                }

                tx.Commit();

                RegistrarEnBitacora("Servidor", "Transferencia Interbancaria Entrante",
                    $"Token [{req.TokenTransferencia}]: Recibidos Q {req.Monto:N2} desde {req.BancoOrigen} ({req.CuentaOrigen}) para {nombreDest} ({cuentaDest}).");

                return (CodigosRespuesta.Aprobado, "Transferencia recibida, acreditada y token guardado.", new DatosIbTransferir
                {
                    TokenTransferencia = req.TokenTransferencia,
                    Estado = EstadosTransferencia.Completada,
                    NombreDestino = nombreDest,
                    CupoRestanteBancoDestino = cupoRestantePropio
                });
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                return (CodigosRespuesta.ErrorDelSistema, "Error al procesar transferencia entrante: " + ex.Message, null);
            }
        }

        public TransferenciaBancariaDto? ConsultarTransferenciaPorToken(string tokenTransferencia)
        {
            return ObtenerHistorialTransferencias(null, tokenTransferencia)
                .FirstOrDefault(t => t.TokenTransferencia.Equals(tokenTransferencia.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Dashboard y Métricas

        public ResumenDiarioDto ObtenerResumenDiario()
        {
            var dto = new ResumenDiarioDto();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("SELECT * FROM dbo.vw_ResumenDiario", conn);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    dto.CajerosEnLinea = reader["CajerosEnLinea"] != DBNull.Value ? Convert.ToInt32(reader["CajerosEnLinea"]) : 0;
                    dto.TotalCajeros = reader["TotalCajeros"] != DBNull.Value ? Convert.ToInt32(reader["TotalCajeros"]) : 0;
                    dto.EfectivoTotalRed = reader["EfectivoTotalRed"] != DBNull.Value ? Convert.ToDecimal(reader["EfectivoTotalRed"]) : 0;
                    dto.TotalRetiradoHoy = reader["TotalRetiradoHoy"] != DBNull.Value ? Convert.ToDecimal(reader["TotalRetiradoHoy"]) : 0;
                    dto.TotalDepositadoHoy = reader["TotalDepositadoHoy"] != DBNull.Value ? Convert.ToDecimal(reader["TotalDepositadoHoy"]) : 0;
                    dto.PromedioDepositadoPorUsuario = reader["PromedioDepositadoPorUsuario"] != DBNull.Value ? Convert.ToDecimal(reader["PromedioDepositadoPorUsuario"]) : 0;
                    dto.NumeroTransaccionesHoy = reader["NumeroTransaccionesHoy"] != DBNull.Value ? Convert.ToInt32(reader["NumeroTransaccionesHoy"]) : 0;
                    dto.CambiosPinHoy = reader["CambiosPinHoy"] != DBNull.Value ? Convert.ToInt32(reader["CambiosPinHoy"]) : 0;
                    dto.OperacionesRechazadasHoy = reader["OperacionesRechazadasHoy"] != DBNull.Value ? Convert.ToInt32(reader["OperacionesRechazadasHoy"]) : 0;
                    dto.AlertasActivas = reader["AlertasActivas"] != DBNull.Value ? Convert.ToInt32(reader["AlertasActivas"]) : 0;
                }
            }
            catch { }

            return dto;
        }

        public List<EstadoCajeroDto> ObtenerEstadoCajeros()
        {
            var lista = new List<EstadoCajeroDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("SELECT * FROM dbo.vw_EstadoCajeros ORDER BY Codigo", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    lista.Add(new EstadoCajeroDto
                    {
                        IdCajero = Convert.ToInt32(reader["IdCajero"]),
                        Codigo = reader["Codigo"].ToString()!,
                        Nombre = reader["Nombre"].ToString()!,
                        Ubicacion = reader["Ubicacion"].ToString()!,
                        IP = reader["IP"].ToString()!,
                        Estado = reader["Estado"].ToString()!,
                        Inicializado = Convert.ToBoolean(reader["Inicializado"]),
                        UltimoHeartbeat = reader["UltimoHeartbeat"] != DBNull.Value ? Convert.ToDateTime(reader["UltimoHeartbeat"]) : null,
                        EfectivoTotal = Convert.ToDecimal(reader["EfectivoTotal"]),
                        Billetes200 = Convert.ToInt32(reader["Billetes200"]),
                        Billetes100 = Convert.ToInt32(reader["Billetes100"]),
                        Billetes50 = Convert.ToInt32(reader["Billetes50"]),
                        Billetes20 = Convert.ToInt32(reader["Billetes20"]),
                        Billetes10 = Convert.ToInt32(reader["Billetes10"]),
                        Billetes5 = Convert.ToInt32(reader["Billetes5"]),
                        Billetes1 = Convert.ToInt32(reader["Billetes1"]),
                        UltimaTransaccion = reader["UltimaTransaccion"] != DBNull.Value ? Convert.ToDateTime(reader["UltimaTransaccion"]) : null
                    });
                }
            }
            catch { }

            return lista;
        }

        public List<TransaccionDto> ObtenerTransacciones(DateTime? desde = null, string? cajero = null, string? tipo = null, string? usuario = null)
        {
            AsegurarEsquemaTransferencias();
            var lista = new List<TransaccionDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                var sql = new StringBuilder(@"
                    SELECT 
                        t.IdTransaccion,
                        t.Fecha,
                        ISNULL(c.Codigo, 'RED-IB') AS Cajero,
                        u.Nombre AS Usuario,
                        ISNULL(tar.NumeroTarjeta, ISNULL(u.NumeroCuenta, '')) AS NumeroTarjeta,
                        t.Tipo,
                        t.Monto,
                        t.CodigoRespuesta,
                        t.MotivoRechazo,
                        t.TokenTransferencia,
                        ISNULL((
                            SELECT STRING_AGG(CONCAT(td.Cantidad, 'xQ', td.IdDenominacion), ', ')
                            FROM dbo.TransaccionDetalle td
                            WHERE td.IdTransaccion = t.IdTransaccion
                        ), CASE WHEN t.TokenTransferencia IS NOT NULL THEN CONCAT('Token: ', t.TokenTransferencia) ELSE '-' END) AS Desglose
                    FROM dbo.Transaccion t
                    LEFT JOIN dbo.Cajero c ON t.IdCajero = c.IdCajero
                    JOIN dbo.Usuario u ON t.IdUsuario = u.IdUsuario
                    LEFT JOIN dbo.Tarjeta tar ON t.IdTarjeta = tar.IdTarjeta
                    WHERE 1=1 ");

                if (desde.HasValue)
                {
                    sql.Append(" AND CAST(t.Fecha AS DATE) >= @Desde ");
                }
                if (!string.IsNullOrEmpty(cajero) && cajero != "Todos")
                {
                    sql.Append(" AND ISNULL(c.Codigo, 'RED-IB') = @Cajero ");
                }
                if (!string.IsNullOrEmpty(tipo) && tipo != "Todos")
                {
                    sql.Append(" AND t.Tipo = @Tipo ");
                }
                if (!string.IsNullOrEmpty(usuario))
                {
                    sql.Append(" AND (u.Nombre LIKE @Usuario OR ISNULL(tar.NumeroTarjeta, '') LIKE @Usuario OR ISNULL(t.TokenTransferencia, '') LIKE @Usuario) ");
                }

                sql.Append(" ORDER BY t.Fecha DESC");

                using var cmd = new SqlCommand(sql.ToString(), conn);
                if (desde.HasValue) cmd.Parameters.AddWithValue("@Desde", desde.Value.Date);
                if (!string.IsNullOrEmpty(cajero) && cajero != "Todos") cmd.Parameters.AddWithValue("@Cajero", cajero);
                if (!string.IsNullOrEmpty(tipo) && tipo != "Todos") cmd.Parameters.AddWithValue("@Tipo", tipo);
                if (!string.IsNullOrEmpty(usuario)) cmd.Parameters.AddWithValue("@Usuario", $"%{usuario.Trim()}%");

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string numTar = reader["NumeroTarjeta"].ToString()!;
                    string enmascarada = numTar.Length == 16 ? $"**** {numTar[^4..]}" : numTar;

                    lista.Add(new TransaccionDto
                    {
                        IdTransaccion = Convert.ToInt32(reader["IdTransaccion"]),
                        Fecha = Convert.ToDateTime(reader["Fecha"]),
                        Cajero = reader["Cajero"].ToString()!,
                        Usuario = reader["Usuario"].ToString()!,
                        TarjetaEnmascarada = enmascarada,
                        Tipo = reader["Tipo"].ToString()!,
                        Monto = Convert.ToDecimal(reader["Monto"]),
                        Desglose = reader["Desglose"].ToString()!,
                        CodigoRespuesta = reader["CodigoRespuesta"].ToString()!,
                        MotivoRechazo = reader["MotivoRechazo"] != DBNull.Value ? reader["MotivoRechazo"].ToString() : null,
                        TokenTransferencia = reader["TokenTransferencia"] != DBNull.Value ? reader["TokenTransferencia"].ToString() : null
                    });
                }
            }
            catch { }

            return lista;
        }

        public List<CambioPinDto> ObtenerCambiosPin(DateTime? desde = null)
        {
            var lista = new List<CambioPinDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = @"
                    SELECT cp.IdCambioPin, u.Nombre AS Usuario, ISNULL(c.Codigo, 'Consola') AS Cajero, cp.Fecha, cp.Origen, cp.Resultado
                    FROM dbo.CambioPin cp
                    JOIN dbo.Usuario u ON cp.IdUsuario = u.IdUsuario
                    LEFT JOIN dbo.Cajero c ON cp.IdCajero = c.IdCajero
                    WHERE (@Desde IS NULL OR CAST(cp.Fecha AS DATE) >= @Desde)
                    ORDER BY cp.Fecha DESC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Desde", desde.HasValue ? desde.Value.Date : DBNull.Value);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    lista.Add(new CambioPinDto
                    {
                        IdCambioPin = Convert.ToInt32(reader["IdCambioPin"]),
                        Usuario = reader["Usuario"].ToString()!,
                        Cajero = reader["Cajero"].ToString()!,
                        Fecha = Convert.ToDateTime(reader["Fecha"]),
                        Origen = reader["Origen"].ToString()!,
                        Resultado = reader["Resultado"].ToString()!
                    });
                }
            }
            catch { }

            return lista;
        }

        #endregion

        #region Gestión de Cajeros

        public (string codigo, string mensaje) InicializarCajero(string codigoCajero, int idAdmin, Dictionary<string, int> desglose)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_InicializarCajero", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@CodigoCajero", codigoCajero);
                cmd.Parameters.AddWithValue("@IdAdmin", idAdmin);
                cmd.Parameters.AddWithValue("@DesgloseJson", JsonSerializer.Serialize(desglose));

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);

                cmd.ExecuteNonQuery();

                return (pCodigo.Value?.ToString() ?? "96", pMensaje.Value?.ToString() ?? "");
            }
            catch (Exception ex)
            {
                return ("96", ex.Message);
            }
        }

        public (string codigo, string mensaje) AgregarEfectivo(string codigoCajero, int idAdmin, Dictionary<string, int> desglose)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_AgregarEfectivo", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@CodigoCajero", codigoCajero);
                cmd.Parameters.AddWithValue("@IdAdmin", idAdmin);
                cmd.Parameters.AddWithValue("@DesgloseJson", JsonSerializer.Serialize(desglose));

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);

                cmd.ExecuteNonQuery();

                return (pCodigo.Value?.ToString() ?? "96", pMensaje.Value?.ToString() ?? "");
            }
            catch (Exception ex)
            {
                return ("96", ex.Message);
            }
        }

        public bool ActualizarEstadoCajero(string codigo, string nuevoEstado)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = "UPDATE dbo.Cajero SET Estado = @Estado WHERE Codigo = @Codigo";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Estado", nuevoEstado);
                cmd.Parameters.AddWithValue("@Codigo", codigo);

                int rows = cmd.ExecuteNonQuery();

                RegistrarEnBitacora("Servidor", "Cambio de Estado de Cajero", $"Cajero {codigo} cambiado a estado '{nuevoEstado}'");
                return rows > 0;
            }
            catch
            {
                return false;
            }
        }

        public bool AltaCajero(string codigo, string nombre, string ubicacion, string ip)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = @"
                    INSERT INTO dbo.Cajero (Codigo, Nombre, Ubicacion, IP, Estado, Inicializado, Activo)
                    VALUES (@Codigo, @Nombre, @Ubicacion, @IP, 'Desconectado', 0, 1)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Codigo", codigo);
                cmd.Parameters.AddWithValue("@Nombre", nombre);
                cmd.Parameters.AddWithValue("@Ubicacion", ubicacion);
                cmd.Parameters.AddWithValue("@IP", ip);

                cmd.ExecuteNonQuery();

                RegistrarEnBitacora("Administrador", "Alta de Cajero", $"Cajero {codigo} creado ({nombre})");
                return true;
            }
            catch { return false; }
        }

        public bool BajaLogicaCajero(string codigo)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = "UPDATE dbo.Cajero SET Activo = 0, Estado = 'Desconectado' WHERE Codigo = @Codigo";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Codigo", codigo);
                cmd.ExecuteNonQuery();

                RegistrarEnBitacora("Administrador", "Baja Lógica de Cajero", $"Cajero {codigo} dado de baja.");
                return true;
            }
            catch { return false; }
        }

        #endregion

        #region Gestión de Usuarios

        public List<UsuarioDto> ObtenerUsuarios()
        {
            AsegurarEsquemaTransferencias();
            var lista = new List<UsuarioDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = @"
                    SELECT 
                        u.IdUsuario,
                        ISNULL(u.NumeroCuenta, CONCAT('1001', RIGHT('000000' + CAST(u.IdUsuario AS VARCHAR(10)), 6))) AS NumeroCuenta,
                        u.Nombre,
                        u.Saldo,
                        u.LimiteDiario,
                        u.Estado,
                        u.IntentosFallidos,
                        u.FechaCreacion,
                        ISNULL(t.NumeroTarjeta, '') AS NumeroTarjeta,
                        ISNULL(t.Activa, 0) AS TarjetaActiva,
                        ISNULL((
                            SELECT SUM(tr.Monto)
                            FROM dbo.Transaccion tr
                            WHERE tr.IdUsuario = u.IdUsuario AND tr.Tipo = 'RETIRO' AND tr.CodigoRespuesta = '00'
                              AND CAST(tr.Fecha AS DATE) = CAST(GETDATE() AS DATE)
                        ), 0) AS RetiradoHoy
                    FROM dbo.Usuario u
                    LEFT JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario AND t.Activa = 1
                    WHERE u.Activo = 1
                    ORDER BY u.Nombre";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    decimal limite = Convert.ToDecimal(reader["LimiteDiario"]);
                    decimal retirado = Convert.ToDecimal(reader["RetiradoHoy"]);
                    decimal disponible = limite - retirado;
                    if (disponible < 0) disponible = 0;

                    lista.Add(new UsuarioDto
                    {
                        IdUsuario = Convert.ToInt32(reader["IdUsuario"]),
                        NumeroCuenta = reader["NumeroCuenta"].ToString()!,
                        Nombre = reader["Nombre"].ToString()!,
                        Saldo = Convert.ToDecimal(reader["Saldo"]),
                        LimiteDiario = limite,
                        Estado = reader["Estado"].ToString()!,
                        IntentosFallidos = Convert.ToInt32(reader["IntentosFallidos"]),
                        FechaCreacion = Convert.ToDateTime(reader["FechaCreacion"]),
                        NumeroTarjeta = reader["NumeroTarjeta"].ToString()!,
                        TarjetaActiva = Convert.ToBoolean(reader["TarjetaActiva"]),
                        RetiradoHoy = retirado,
                        DisponibleHoy = disponible
                    });
                }
            }
            catch { }

            return lista;
        }

        public (bool exito, string mensaje) CrearUsuario(string nombre, string tarjeta, string pin, decimal saldo, decimal limiteDiario, string? rfidUid = null)
        {
            AsegurarEsquemaTransferencias();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                if (tarjeta.Length != 16) return (false, "La tarjeta debe tener 16 dígitos.");
                if (pin.Length != 4) return (false, "El PIN debe tener 4 dígitos.");

                using var tx = conn.BeginTransaction();
                try
                {
                    string sqlUsuario = @"INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos, Activo) 
                                         OUTPUT INSERTED.IdUsuario 
                                         VALUES (@Nombre, @Saldo, @Limite, 'Activo', 0, 1)";

                    int idUsuario;
                    using (var cmdU = new SqlCommand(sqlUsuario, conn, tx))
                    {
                        cmdU.Parameters.AddWithValue("@Nombre", nombre);
                        cmdU.Parameters.AddWithValue("@Saldo", saldo);
                        cmdU.Parameters.AddWithValue("@Limite", limiteDiario);
                        idUsuario = Convert.ToInt32(cmdU.ExecuteScalar());
                    }

                    string numCuenta = $"1001{idUsuario:D6}";
                    using (var cmdCta = new SqlCommand("UPDATE dbo.Usuario SET NumeroCuenta = @Cta WHERE IdUsuario = @Id", conn, tx))
                    {
                        cmdCta.Parameters.AddWithValue("@Cta", numCuenta);
                        cmdCta.Parameters.AddWithValue("@Id", idUsuario);
                        cmdCta.ExecuteNonQuery();
                    }

                    string salt = Guid.NewGuid().ToString("N");
                    string hash = GenerarHashSha256(pin, salt);

                    string sqlTarjeta = @"INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa, RequiereCambioPin)
                                          VALUES (@Tarjeta, @Hash, @Salt, @IdUsuario, 1, 0)";

                    using (var cmdT = new SqlCommand(sqlTarjeta, conn, tx))
                    {
                        cmdT.Parameters.AddWithValue("@Tarjeta", tarjeta);
                        cmdT.Parameters.AddWithValue("@Hash", hash);
                        cmdT.Parameters.AddWithValue("@Salt", salt);
                        cmdT.Parameters.AddWithValue("@IdUsuario", idUsuario);
                        cmdT.ExecuteNonQuery();
                    }

                    if (!string.IsNullOrWhiteSpace(rfidUid))
                    {
                        string sqlRfid = "INSERT INTO dbo.MapeoRfid (UidRfid, NumeroTarjeta, Descripcion) VALUES (@Uid, @Tarjeta, @Desc)";
                        using var cmdR = new SqlCommand(sqlRfid, conn, tx);
                        cmdR.Parameters.AddWithValue("@Uid", rfidUid.Trim());
                        cmdR.Parameters.AddWithValue("@Tarjeta", tarjeta);
                        cmdR.Parameters.AddWithValue("@Desc", $"Vinculado a {nombre}");
                        cmdR.ExecuteNonQuery();
                    }

                    tx.Commit();
                    RegistrarEnBitacora("Administrador", "Creación de Usuario", $"Usuario '{nombre}' creado con cuenta {numCuenta} y tarjeta **** {tarjeta[^4..]}");
                    return (true, $"Usuario creado exitosamente. Cuenta asignada: {numCuenta}");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return (false, ex.Message);
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public (bool exito, string mensaje) CambiarTarjetaUsuario(int idUsuario, string nuevaTarjeta, string pinNuevo, int idAdmin)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_CambiarTarjeta", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.Parameters.AddWithValue("@NuevaTarjeta", nuevaTarjeta);
                cmd.Parameters.AddWithValue("@PinNuevo", pinNuevo);
                cmd.Parameters.AddWithValue("@IdAdmin", idAdmin);

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);

                cmd.ExecuteNonQuery();

                bool ok = (pCodigo.Value?.ToString() == "00");
                return (ok, pMensaje.Value?.ToString() ?? "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public (bool exito, string mensaje) CambiarLimiteUsuario(int idUsuario, decimal nuevoLimite, int idAdmin)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_CambiarLimite", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.Parameters.AddWithValue("@NuevoLimite", nuevoLimite);
                cmd.Parameters.AddWithValue("@IdAdmin", idAdmin);

                var pCodigo = new SqlParameter("@CodigoRespuesta", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 255) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(pCodigo);
                cmd.Parameters.Add(pMensaje);

                cmd.ExecuteNonQuery();

                bool ok = (pCodigo.Value?.ToString() == "00");
                return (ok, pMensaje.Value?.ToString() ?? "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public bool CambiarEstadoBloqueoUsuario(int idUsuario, bool bloquear, int idAdmin)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string estado = bloquear ? "Bloqueado" : "Activo";
                int intentos = bloquear ? 3 : 0;

                string sql = "UPDATE dbo.Usuario SET Estado = @Estado, IntentosFallidos = @Intentos WHERE IdUsuario = @Id";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Estado", estado);
                cmd.Parameters.AddWithValue("@Intentos", intentos);
                cmd.Parameters.AddWithValue("@Id", idUsuario);

                cmd.ExecuteNonQuery();

                RegistrarEnBitacora("Administrador", bloquear ? "Bloqueo de Tarjeta" : "Desbloqueo de Tarjeta",
                    $"Usuario ID {idUsuario} fue {(bloquear ? "bloqueado" : "desbloqueado")} por Admin ID {idAdmin}");

                return true;
            }
            catch { return false; }
        }

        public (bool exito, string mensaje) AsignarPinTemporal(string tarjeta, string pinTemporal, int idAdmin)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string salt = Guid.NewGuid().ToString("N");
                string hash = GenerarHashSha256(pinTemporal, salt);

                string sql = @"UPDATE dbo.Tarjeta 
                               SET PinHash = @Hash, PinSalt = @Salt, RequiereCambioPin = 1 
                               WHERE NumeroTarjeta = @Tarjeta AND Activa = 1";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Hash", hash);
                cmd.Parameters.AddWithValue("@Salt", salt);
                cmd.Parameters.AddWithValue("@Tarjeta", tarjeta);

                int rows = cmd.ExecuteNonQuery();
                if (rows > 0)
                {
                    RegistrarEnBitacora("Administrador", "Asignación de PIN Temporal",
                        $"Admin ID {idAdmin} asignó un PIN temporal a tarjeta **** {tarjeta[^4..]}. Obliga cambio en primer uso.");
                    return (true, "PIN temporal asignado con éxito. El usuario deberá cambiarlo en su primer uso.");
                }

                return (false, "Tarjeta activa no encontrada.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        #endregion

        #region Alertas y Bitácora

        public List<AlertaDto> ObtenerAlertas(bool soloPendientes = false)
        {
            var lista = new List<AlertaDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = @"
                    SELECT a.IdAlerta, a.Tipo, ISNULL(c.Codigo, 'Sistema') AS Cajero, a.Descripcion, a.Fecha, 
                           a.Atendida, a.FechaAtendida, adm.Usuario AS AdminAtendio
                    FROM dbo.Alerta a
                    LEFT JOIN dbo.Cajero c ON a.IdCajero = c.IdCajero
                    LEFT JOIN dbo.Administrador adm ON a.IdAdminAtendio = adm.IdAdmin
                    WHERE (@SoloPendientes = 0 OR a.Atendida = 0)
                    ORDER BY a.Fecha DESC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SoloPendientes", soloPendientes ? 1 : 0);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    lista.Add(new AlertaDto
                    {
                        IdAlerta = Convert.ToInt32(reader["IdAlerta"]),
                        Tipo = reader["Tipo"].ToString()!,
                        Cajero = reader["Cajero"].ToString()!,
                        Descripcion = reader["Descripcion"].ToString()!,
                        Fecha = Convert.ToDateTime(reader["Fecha"]),
                        Atendida = Convert.ToBoolean(reader["Atendida"]),
                        FechaAtendida = reader["FechaAtendida"] != DBNull.Value ? Convert.ToDateTime(reader["FechaAtendida"]) : null,
                        AdminAtendio = reader["AdminAtendio"] != DBNull.Value ? reader["AdminAtendio"].ToString() : null
                    });
                }
            }
            catch { }

            return lista;
        }

        public bool MarcarAlertaAtendida(int idAlerta, int idAdmin)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = "UPDATE dbo.Alerta SET Atendida = 1, FechaAtendida = GETDATE(), IdAdminAtendio = @IdAdmin WHERE IdAlerta = @Id";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@IdAdmin", idAdmin);
                cmd.Parameters.AddWithValue("@Id", idAlerta);
                cmd.ExecuteNonQuery();

                return true;
            }
            catch { return false; }
        }

        public List<BitacoraDto> ObtenerBitacora(string? origen = null, string? busqueda = null)
        {
            var lista = new List<BitacoraDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                var sql = new StringBuilder("SELECT TOP 200 IdBitacora, Origen, Accion, Detalle, Fecha FROM dbo.Bitacora WHERE 1=1 ");

                if (!string.IsNullOrEmpty(origen) && origen != "Todos")
                {
                    sql.Append(" AND Origen = @Origen ");
                }
                if (!string.IsNullOrEmpty(busqueda))
                {
                    sql.Append(" AND (Accion LIKE @Busqueda OR Detalle LIKE @Busqueda) ");
                }

                sql.Append(" ORDER BY Fecha DESC");

                using var cmd = new SqlCommand(sql.ToString(), conn);
                if (!string.IsNullOrEmpty(origen) && origen != "Todos") cmd.Parameters.AddWithValue("@Origen", origen);
                if (!string.IsNullOrEmpty(busqueda)) cmd.Parameters.AddWithValue("@Busqueda", $"%{busqueda.Trim()}%");

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    lista.Add(new BitacoraDto
                    {
                        IdBitacora = Convert.ToInt32(reader["IdBitacora"]),
                        Origen = reader["Origen"].ToString()!,
                        Accion = reader["Accion"].ToString()!,
                        Detalle = reader["Detalle"].ToString()!,
                        Fecha = Convert.ToDateTime(reader["Fecha"])
                    });
                }
            }
            catch { }

            return lista;
        }

        public void RegistrarEnBitacora(string origen, string accion, string detalle)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = "INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha) VALUES (@Origen, @Accion, @Detalle, GETDATE())";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Origen", origen);
                cmd.Parameters.AddWithValue("@Accion", accion);
                cmd.Parameters.AddWithValue("@Detalle", detalle);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        #endregion

        #region Parámetros

        public List<ParametroDto> ObtenerParametros()
        {
            AsegurarEsquemaTransferencias();
            var lista = new List<ParametroDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("SELECT IdParametro, Clave, Valor, Descripcion FROM dbo.Parametro ORDER BY Clave", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    lista.Add(new ParametroDto
                    {
                        IdParametro = Convert.ToInt32(reader["IdParametro"]),
                        Clave = reader["Clave"].ToString()!,
                        Valor = reader["Valor"].ToString()!,
                        Descripcion = reader["Descripcion"] != DBNull.Value ? reader["Descripcion"].ToString()! : ""
                    });
                }
            }
            catch { }

            return lista;
        }

        public bool GuardarParametro(string clave, string valor)
        {
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = "UPDATE dbo.Parametro SET Valor = @Valor WHERE Clave = @Clave";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Valor", valor);
                cmd.Parameters.AddWithValue("@Clave", clave);
                int rows = cmd.ExecuteNonQuery();

                RegistrarEnBitacora("Administrador", "Configuración de Parámetro", $"Parámetro '{clave}' cambiado a '{valor}'");
                return rows > 0;
            }
            catch { return false; }
        }

        #endregion
    }
}
