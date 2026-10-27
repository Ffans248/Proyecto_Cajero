using System;
using Microsoft.Data.SqlClient;

namespace Cajero.Servidor.AccesoDatos
{
    public static class ConexionBD
    {
        // Cadena de conexión por defecto hacia la base de datos BD_CajeroRed en LocalDB o SQL Server local
        public static string CadenaConexion { get; set; } = 
            "Server=(localdb)\\MSSQLLocalDB;Database=BD_CajeroRed;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=15;";

        public static SqlConnection ObtenerConexion()
        {
            return new SqlConnection(CadenaConexion);
        }

        public static bool ProbarConexion(out string mensajeError)
        {
            try
            {
                using var conn = ObtenerConexion();
                conn.Open();
                mensajeError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                mensajeError = ex.Message;
                return false;
            }
        }
    }
}
