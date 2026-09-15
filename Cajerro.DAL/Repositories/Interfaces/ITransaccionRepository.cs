using Cajero.DAL.Models;

namespace Cajero.DAL.Repositories.Interfaces
{
    /// <summary>
    /// Contrato para el registro y consulta del histórico de transacciones en Transacciones.csv.
    /// </summary>
    public interface ITransaccionRepository
    {
        void Registrar(Transaccion transaccion);
        List<Transaccion> ObtenerTodas();
        List<Transaccion> ObtenerPorTarjeta(string numeroTarjeta);
    }
}
