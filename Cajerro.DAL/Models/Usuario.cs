using Cajero.DAL.Models.Enums;

namespace Cajero.DAL.Models
{
    /// <summary>
    /// Representa a un usuario (cliente o administrador) registrado en el sistema.
    /// Mapea los registros del archivo Usuarios.csv.
    /// </summary>
    public class Usuario
    {
        public string NumeroTarjeta { get; set; } = string.Empty;
        public string PinHash { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string RfidUID { get; set; } = string.Empty;
        public decimal Saldo { get; set; }
        public decimal LimiteDiarioRetiro { get; set; }
        public decimal MontoRetiradoHoy { get; set; }
        public DateTime UltimaFechaRetiro { get; set; } = DateTime.Today;
        public RolUsuario Rol { get; set; } = RolUsuario.Cliente;
        public EstadoUsuario Estado { get; set; } = EstadoUsuario.Activo;

        public bool EsAdministrador => Rol == RolUsuario.Administrador;
        public bool EstaActivo => Estado == EstadoUsuario.Activo;

        public Usuario()
        {
        }

        public Usuario(
            string numeroTarjeta,
            string pinHash,
            string nombreCompleto,
            string rfidUID,
            decimal saldo,
            decimal limiteDiarioRetiro,
            decimal montoRetiradoHoy,
            DateTime ultimaFechaRetiro,
            RolUsuario rol,
            EstadoUsuario estado)
        {
            NumeroTarjeta = numeroTarjeta;
            PinHash = pinHash;
            NombreCompleto = nombreCompleto;
            RfidUID = rfidUID;
            Saldo = saldo;
            LimiteDiarioRetiro = limiteDiarioRetiro;
            MontoRetiradoHoy = montoRetiradoHoy;
            UltimaFechaRetiro = ultimaFechaRetiro;
            Rol = rol;
            Estado = estado;
        }
    }
}
