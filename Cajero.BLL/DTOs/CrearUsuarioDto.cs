namespace Cajero.BLL.DTOs
{
    /// <summary>
    /// Parámetros requeridos por el Módulo Administrativo para registrar un nuevo usuario en el sistema.
    /// </summary>
    public class CrearUsuarioDto
    {
        public string NumeroTarjeta { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string RfidUID { get; set; } = string.Empty;
        public decimal SaldoInicial { get; set; }
        public decimal LimiteDiarioRetiro { get; set; }
        public bool EsAdministrador { get; set; }

        public CrearUsuarioDto()
        {
        }

        public CrearUsuarioDto(
            string numeroTarjeta,
            string pin,
            string nombreCompleto,
            string rfidUID,
            decimal saldoInicial,
            decimal limiteDiarioRetiro,
            bool esAdministrador = false)
        {
            NumeroTarjeta = numeroTarjeta;
            Pin = pin;
            NombreCompleto = nombreCompleto;
            RfidUID = rfidUID;
            SaldoInicial = saldoInicial;
            LimiteDiarioRetiro = limiteDiarioRetiro;
            EsAdministrador = esAdministrador;
        }
    }
}
