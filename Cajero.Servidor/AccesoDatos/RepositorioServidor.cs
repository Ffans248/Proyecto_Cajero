using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cajero.Protocolo;
using Cajero.Servidor.Modelos;
using Microsoft.Data.SqlClient;

namespace Cajero.Servidor.AccesoDatos
{
    public class RepositorioServidor
    {
        #region Seguridad y Utilidades

        public static string GenerarHashSha256(string texto, string salt)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(texto + salt);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToUpper();
        }

        #endregion

        #region Autenticación de Administradores

        public AdministradorSesion? AutenticarAdmin(string usuario, string password)
        {
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

        public (string nombreUsuario, decimal saldo, decimal limiteDiario, decimal disponibleHoy) ConsultaSaldo(string codigoCajero, string tarjeta)
        {
            using var conn = ConexionBD.ObtenerConexion();
            conn.Open();

            string sql = @"
                SELECT u.Nombre, u.Saldo, u.LimiteDiario,
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
                string nombre = reader["Nombre"].ToString()!;
                decimal saldo = Convert.ToDecimal(reader["Saldo"]);
                decimal limite = Convert.ToDecimal(reader["LimiteDiario"]);
                decimal retirado = Convert.ToDecimal(reader["RetiradoHoy"]);
                decimal disponible = limite - retirado;
                if (disponible < 0) disponible = 0;

                return (nombre, saldo, limite, disponible);
            }

            return ("", 0, 0, 0);
        }

        public List<ItemTransaccion> UltimasTransacciones(string tarjeta, int topN = 5)
        {
            var lista = new List<ItemTransaccion>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                using var cmd = new SqlCommand("dbo.sp_UltimasTransacciones", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@NumeroTarjeta", tarjeta);
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
                        MotivoRechazo = reader["MotivoRechazo"] != DBNull.Value ? reader["MotivoRechazo"].ToString() : null
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
            var lista = new List<TransaccionDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                var sql = new StringBuilder(@"
                    SELECT 
                        t.IdTransaccion,
                        t.Fecha,
                        c.Codigo AS Cajero,
                        u.Nombre AS Usuario,
                        tar.NumeroTarjeta,
                        t.Tipo,
                        t.Monto,
                        t.CodigoRespuesta,
                        t.MotivoRechazo,
                        ISNULL((
                            SELECT STRING_AGG(CONCAT(td.Cantidad, 'xQ', td.IdDenominacion), ', ')
                            FROM dbo.TransaccionDetalle td
                            WHERE td.IdTransaccion = t.IdTransaccion
                        ), '-') AS Desglose
                    FROM dbo.Transaccion t
                    JOIN dbo.Cajero c ON t.IdCajero = c.IdCajero
                    JOIN dbo.Usuario u ON t.IdUsuario = u.IdUsuario
                    JOIN dbo.Tarjeta tar ON t.IdTarjeta = tar.IdTarjeta
                    WHERE 1=1 ");

                if (desde.HasValue)
                {
                    sql.Append(" AND CAST(t.Fecha AS DATE) >= @Desde ");
                }
                if (!string.IsNullOrEmpty(cajero) && cajero != "Todos")
                {
                    sql.Append(" AND c.Codigo = @Cajero ");
                }
                if (!string.IsNullOrEmpty(tipo) && tipo != "Todos")
                {
                    sql.Append(" AND t.Tipo = @Tipo ");
                }
                if (!string.IsNullOrEmpty(usuario))
                {
                    sql.Append(" AND (u.Nombre LIKE @Usuario OR tar.NumeroTarjeta LIKE @Usuario) ");
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
                    string enmascarada = numTar.Length >= 4 ? $"**** {numTar[^4..]}" : numTar;

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
                        MotivoRechazo = reader["MotivoRechazo"] != DBNull.Value ? reader["MotivoRechazo"].ToString() : null
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
            var lista = new List<UsuarioDto>();
            try
            {
                using var conn = ConexionBD.ObtenerConexion();
                conn.Open();

                string sql = @"
                    SELECT 
                        u.IdUsuario,
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
                    RegistrarEnBitacora("Administrador", "Creación de Usuario", $"Usuario '{nombre}' creado con tarjeta **** {tarjeta[^4..]}");
                    return (true, "Usuario creado exitosamente.");
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
