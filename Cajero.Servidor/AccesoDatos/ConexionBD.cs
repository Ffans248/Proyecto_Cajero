using System;
using Microsoft.Data.SqlClient;

namespace Cajero.Servidor.AccesoDatos
{
    public static class ConexionBD
    {
        // Cadena de conexión hacia la Base de Datos Maestra BD_CajeroRed en SQL Server Express local
        public static string CadenaConexion { get; set; } = 
            "Server=localhost\\SQLEXPRESS;Database=BD_CajeroRed;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=15;";

        private static bool _instanciaResuelta = false;
        private static readonly object _lockInstancia = new();

        public static SqlConnection ObtenerConexion()
        {
            AsegurarInstanciaSqlActiva();
            return new SqlConnection(CadenaConexion);
        }

        private static void AsegurarInstanciaSqlActiva()
        {
            if (_instanciaResuelta) return;
            lock (_lockInstancia)
            {
                if (_instanciaResuelta) return;

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
                        CadenaConexion = cad.Replace("Connect Timeout=5;", "Connect Timeout=15;");
                        _instanciaResuelta = true;
                        return;
                    }
                    catch { }
                }

                _instanciaResuelta = true;
            }
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
