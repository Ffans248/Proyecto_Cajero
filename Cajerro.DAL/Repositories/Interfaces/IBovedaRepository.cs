using Cajero.DAL.Models;

namespace Cajero.DAL.Repositories.Interfaces
{
    /// <summary>
    /// Contrato para la persistencia y gestión de denominaciones en Boveda.csv.
    /// </summary>
    public interface IBovedaRepository
    {
        List<DenominacionBoveda> ObtenerBoveda();
        void GuardarBoveda(List<DenominacionBoveda> boveda);
        void ActualizarDenominacion(int denominacion, int cantidadNueva);
        decimal ObtenerSaldoTotalBoveda();
    }
}
