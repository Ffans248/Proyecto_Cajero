namespace Cajerro.DAL.Modelos
{
    /// <summary>
    /// Representa un lote de billetes de una denominación específica dentro de la bóveda del cajero.
    /// Denominaciones soportadas: 200, 100, 50, 20, 10, 5 y 1.
    /// </summary>
    public class Billete
    {
        /// <summary>
        /// Valor facial del billete (ej. 200, 100, 50, 20, 10, 5, 1).
        /// </summary>
        public int Denominacion { get; set; }

        /// <summary>
        /// Número de piezas o unidades disponibles de esta denominación en el cajero.
        /// </summary>
        public int Cantidad { get; set; }

        /// <summary>
        /// Monto total en dinero representado por este lote de billetes (Denominación * Cantidad).
        /// </summary>
        public decimal Subtotal => Denominacion * Cantidad;

        /// <summary>
        /// Constructor vacío para inicialización por propiedades.
        /// </summary>
        public Billete()
        {
        }

        /// <summary>
        /// Constructor parametrizado para crear un registro de billetes.
        /// </summary>
        /// <param name="denominacion">Valor monetario del billete.</param>
        /// <param name="cantidad">Cantidad de unidades.</param>
        public Billete(int denominacion, int cantidad)
        {
            Denominacion = denominacion;
            Cantidad = cantidad;
        }

        public override string ToString()
        {
            return $"Billete de Q{Denominacion} - Cantidad: {Cantidad} (Subtotal: Q{Subtotal:N2})";
        }
    }
}
