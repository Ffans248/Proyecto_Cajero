namespace Cajero.BLL.DTOs
{
    /// <summary>
    /// Representa un lote de billetes de una denominación específica para cargas o arqueo de bóveda.
    /// </summary>
    public class LoteBilletesDto
    {
        public int Denominacion { get; set; }
        public int Cantidad { get; set; }
        public decimal Subtotal => Denominacion * Cantidad;

        public LoteBilletesDto()
        {
        }

        public LoteBilletesDto(int denominacion, int cantidad)
        {
            Denominacion = denominacion;
            Cantidad = cantidad;
        }

        public override string ToString()
        {
            return $"Q{Denominacion} x {Cantidad} billetes = Q{Subtotal:N2}";
        }
    }
}
