using Cajero.DAL.Models.Enums;

namespace Cajero.DAL.Models
{
    /// <summary>
    /// Representa el registro de una transacción bancaria realizada en el cajero.
    /// Mapea los registros del archivo Transacciones.csv.
    /// </summary>
    public class Transaccion
    {
        public string IdTransaccion { get; set; } = Guid.NewGuid().ToString("N");
        public string NumeroTarjeta { get; set; } = string.Empty;
        public TipoTransaccion Tipo { get; set; }
        public decimal Monto { get; set; }
        public DateTime FechaHora { get; set; } = DateTime.Now;
        public string DetalleDesglose { get; set; } = string.Empty;
        public decimal SaldoPosterior { get; set; }

        public Transaccion()
        {
        }

        public Transaccion(
            string idTransaccion,
            string numeroTarjeta,
            TipoTransaccion tipo,
            decimal monto,
            DateTime fechaHora,
            string detalleDesglose,
            decimal saldoPosterior)
        {
            IdTransaccion = idTransaccion;
            NumeroTarjeta = numeroTarjeta;
            Tipo = tipo;
            Monto = monto;
            FechaHora = fechaHora;
            DetalleDesglose = detalleDesglose;
            SaldoPosterior = saldoPosterior;
        }
    }
}
