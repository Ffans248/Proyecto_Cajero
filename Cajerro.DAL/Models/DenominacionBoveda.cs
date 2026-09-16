namespace Cajero.DAL.Models
{
    /// <summary>
    /// Representa un lote de billetes de una denominación específica dentro de la bóveda.
    /// Mapea los registros del archivo Boveda.csv.
    /// </summary>
    public class DenominacionBoveda
    {
        /// <summary>
        /// Valor nominal del billete en Quetzales (200, 100, 50, 20, 10, 5, 1).
        /// </summary>
        public int Denominacion { get; set; }

        /// <summary>
        /// Cantidad de piezas/billetes disponibles de esta denominación en el cajero.
        /// </summary>
        public int Cantidad { get; set; }

        /// <summary>
        /// Subtotal monetario acumulado por esta denominación (Denominación * Cantidad).
        /// </summary>
        public decimal Subtotal => Denominacion * Cantidad;

        public DenominacionBoveda()
        {
        }

        public DenominacionBoveda(int denominacion, int cantidad)
        {
            Denominacion = denominacion;
            Cantidad = cantidad;
        }

        public override string ToString()
        {
            return $"Billetes de Q{Denominacion}: {Cantidad} unidades (Subtotal: Q{Subtotal:N2})";
        }
    }
}
