using Cajero.DAL.Models;

namespace Cajero.BLL.DTOs
{
    /// <summary>
    /// Resumen o reporte de control diario de transacciones y movimientos para el Módulo Administrativo.
    /// </summary>
    public class ReporteDiarioDto
    {
        public DateTime Fecha { get; set; }
        public int TotalTransacciones { get; set; }
        public decimal TotalRetiros { get; set; }
        public decimal TotalDepositos { get; set; }
        public decimal TotalCargasBoveda { get; set; }
        public List<Transaccion> Movimientos { get; set; } = new();

        public ReporteDiarioDto()
        {
        }

        public ReporteDiarioDto(
            DateTime fecha,
            int totalTransacciones,
            decimal totalRetiros,
            decimal totalDepositos,
            decimal totalCargasBoveda,
            List<Transaccion> movimientos)
        {
            Fecha = fecha;
            TotalTransacciones = totalTransacciones;
            TotalRetiros = totalRetiros;
            TotalDepositos = totalDepositos;
            TotalCargasBoveda = totalCargasBoveda;
            Movimientos = movimientos;
        }
    }
}
