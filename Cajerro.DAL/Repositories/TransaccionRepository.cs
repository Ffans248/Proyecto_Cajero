using System.Globalization;
using Cajero.DAL.Helpers;
using Cajero.DAL.Models;
using Cajero.DAL.Models.Enums;
using Cajero.DAL.Repositories.Interfaces;

namespace Cajero.DAL.Repositories
{
    /// <summary>
    /// Implementación de ITransaccionRepository para registrar y consultar
    /// el historial de operaciones en Transacciones.csv utilizando StreamReader y StreamWriter.
    /// </summary>
    public class TransaccionRepository : ITransaccionRepository
    {
        private readonly CsvFileContext _context;

        public TransaccionRepository(CsvFileContext? context = null)
        {
            _context = context ?? new CsvFileContext();
        }

        public void Registrar(Transaccion transaccion)
        {
            ArgumentNullException.ThrowIfNull(transaccion);

            string linea = SerializarTransaccion(transaccion);
            _context.AgregarLinea(_context.RutaTransacciones, linea, _context.LockTransacciones);
        }

        public List<Transaccion> ObtenerTodas()
        {
            var lineas = _context.LeerTodasLasLineas(_context.RutaTransacciones, _context.LockTransacciones);
            var transacciones = new List<Transaccion>();

            // Omitir encabezado
            for (int i = 1; i < lineas.Count; i++)
            {
                string linea = lineas[i];
                if (string.IsNullOrWhiteSpace(linea)) continue;

                var transaccion = ParsearTransaccion(linea);
                if (transaccion != null)
                {
                    transacciones.Add(transaccion);
                }
            }

            return transacciones.OrderByDescending(t => t.FechaHora).ToList();
        }

        public List<Transaccion> ObtenerPorTarjeta(string numeroTarjeta)
        {
            if (string.IsNullOrWhiteSpace(numeroTarjeta))
                return new List<Transaccion>();

            return ObtenerTodas()
                .Where(t => t.NumeroTarjeta.Trim().Equals(numeroTarjeta.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        #region Métodos de Mapeo CSV

        private static Transaccion? ParsearTransaccion(string linea)
        {
            string[] partes = linea.Split(CsvFileContext.Separador);
            if (partes.Length < 7)
                return null;

            try
            {
                string idTransaccion = partes[0].Trim();
                string numeroTarjeta = partes[1].Trim();

                if (!Enum.TryParse(partes[2].Trim(), true, out TipoTransaccion tipo))
                {
                    tipo = TipoTransaccion.ConsultaSaldo;
                }

                decimal monto = decimal.Parse(partes[3].Trim(), CultureInfo.InvariantCulture);
                DateTime fechaHora = DateTime.Parse(partes[4].Trim(), CultureInfo.InvariantCulture);
                string detalleDesglose = partes[5].Trim();
                decimal saldoPosterior = decimal.Parse(partes[6].Trim(), CultureInfo.InvariantCulture);

                return new Transaccion(
                    idTransaccion,
                    numeroTarjeta,
                    tipo,
                    monto,
                    fechaHora,
                    detalleDesglose,
                    saldoPosterior
                );
            }
            catch
            {
                return null;
            }
        }

        private static string SerializarTransaccion(Transaccion transaccion)
        {
            return string.Join(CsvFileContext.Separador,
                transaccion.IdTransaccion,
                transaccion.NumeroTarjeta,
                transaccion.Tipo.ToString(),
                transaccion.Monto.ToString("F2", CultureInfo.InvariantCulture),
                transaccion.FechaHora.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                transaccion.DetalleDesglose,
                transaccion.SaldoPosterior.ToString("F2", CultureInfo.InvariantCulture)
            );
        }

        #endregion
    }
}
