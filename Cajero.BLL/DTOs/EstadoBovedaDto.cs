namespace Cajero.BLL.DTOs
{
    /// <summary>
    /// Representa el estado actual o arqueo físico de la bóveda del cajero automático.
    /// </summary>
    public class EstadoBovedaDto
    {
        public decimal SaldoTotal { get; set; }
        public List<LoteBilletesDto> Denominaciones { get; set; } = new();

        public EstadoBovedaDto()
        {
        }

        public EstadoBovedaDto(decimal saldoTotal, List<LoteBilletesDto> denominaciones)
        {
            SaldoTotal = saldoTotal;
            Denominaciones = denominaciones;
        }
    }
}
