using Cajero.DAL.Models.Enums;

namespace Cajero.BLL.DTOs
{
    /// <summary>
    /// Proyección de un usuario para visualización segura en la interfaz gráfica del Módulo Administrativo.
    /// Omite deliberadamente la exposición del hash del PIN.
    /// </summary>
    public class UsuarioDto
    {
        public string NumeroTarjeta { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string RfidUID { get; set; } = string.Empty;
        public decimal Saldo { get; set; }
        public decimal LimiteDiarioRetiro { get; set; }
        public decimal MontoRetiradoHoy { get; set; }
        public DateTime UltimaFechaRetiro { get; set; }
        public RolUsuario Rol { get; set; }
        public EstadoUsuario Estado { get; set; }
        public bool EsAdministrador => Rol == RolUsuario.Administrador;

        public UsuarioDto()
        {
        }
    }
}
