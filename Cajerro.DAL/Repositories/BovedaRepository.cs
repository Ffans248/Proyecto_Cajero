using System.Globalization;
using Cajero.DAL.Helpers;
using Cajero.DAL.Models;
using Cajero.DAL.Repositories.Interfaces;

namespace Cajero.DAL.Repositories
{
    /// <summary>
    /// Implementación de IBovedaRepository para la persistencia del inventario de billetes
    /// en Boveda.csv utilizando StreamReader y StreamWriter.
    /// </summary>
    public class BovedaRepository : IBovedaRepository
    {
        private readonly CsvFileContext _context;

        public BovedaRepository(CsvFileContext? context = null)
        {
            _context = context ?? new CsvFileContext();
        }

        public List<DenominacionBoveda> ObtenerBoveda()
        {
            var lineas = _context.LeerTodasLasLineas(_context.RutaBoveda, _context.LockBoveda);
            var resultado = new List<DenominacionBoveda>();

            // Omitir fila de encabezado
            for (int i = 1; i < lineas.Count; i++)
            {
                string linea = lineas[i];
                if (string.IsNullOrWhiteSpace(linea)) continue;

                string[] partes = linea.Split(CsvFileContext.Separador);
                if (partes.Length >= 2 &&
                    int.TryParse(partes[0].Trim(), out int denom) &&
                    int.TryParse(partes[1].Trim(), out int cantidad))
                {
                    resultado.Add(new DenominacionBoveda(denom, cantidad));
                }
            }

            // Orden descendente por denominación (200, 100, 50, 20, 10, 5, 1)
            // Facilita la lógica de desglose matemático en la capa BLL
            return resultado.OrderByDescending(d => d.Denominacion).ToList();
        }

        public void GuardarBoveda(List<DenominacionBoveda> boveda)
        {
            ArgumentNullException.ThrowIfNull(boveda);

            var lineas = new List<string>
            {
                "Denominacion;Cantidad"
            };

            foreach (var item in boveda.OrderByDescending(d => d.Denominacion))
            {
                lineas.Add($"{item.Denominacion}{CsvFileContext.Separador}{item.Cantidad}");
            }

            _context.EscribirTodasLasLineas(_context.RutaBoveda, lineas, _context.LockBoveda);
        }

        public void ActualizarDenominacion(int denominacion, int cantidadNueva)
        {
            if (cantidadNueva < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cantidadNueva), "La cantidad de billetes no puede ser negativa.");
            }

            lock (_context.LockBoveda)
            {
                var boveda = ObtenerBoveda();
                var item = boveda.FirstOrDefault(d => d.Denominacion == denominacion);

                if (item != null)
                {
                    item.Cantidad = cantidadNueva;
                }
                else
                {
                    boveda.Add(new DenominacionBoveda(denominacion, cantidadNueva));
                }

                GuardarBoveda(boveda);
            }
        }

        public decimal ObtenerSaldoTotalBoveda()
        {
            return ObtenerBoveda().Sum(d => d.Subtotal);
        }
    }
}
