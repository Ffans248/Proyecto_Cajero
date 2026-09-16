using System;
using System.Collections.Generic;
using System.Linq;
using Cajerro.DAL.Interfaces;
using Cajerro.DAL.Modelos;
using Cajerro.DAL;

namespace Cajero.BLL
{
    /// <summary>
    /// Adaptador que implementa la interfaz IDatosCajero (requerida por Cajero.BLL)
    /// envolviendo y traduciendo las operaciones hacia IGestorArchivos (de Cajerro.DAL).
    /// Aplica el Patrón de Diseño Adaptador (Adapter Pattern) para resolver la incompatibilidad de interfaces.
    /// </summary>
    public class AdaptadorCajero : IDatosCajero
    {
        private readonly IGestorArchivos _gestor;

        /// <summary>
        /// Constructor principal que recibe la dependencia de acceso a datos.
        /// </summary>
        /// <param name="gestor">Instancia concreta de IGestorArchivos.</param>
        public AdaptadorCajero(IGestorArchivos gestor)
        {
            _gestor = gestor ?? throw new ArgumentNullException(nameof(gestor));
        }

        /// <summary>
        /// Constructor por defecto que utiliza la implementación estándar GestorArchivosCSV.
        /// </summary>
        public AdaptadorCajero() : this(new GestorArchivosCSV())
        {
        }

        /// <summary>
        /// Verifica si existe un usuario con el número de tarjeta (token) indicado.
        /// </summary>
        public bool ExisteUsuario(string token)
        {
            return _gestor.ObtenerUsuarioPorTarjeta(token) != null;
        }

        /// <summary>
        /// Verifica si el usuario está activo. Dado que en el CSV no existe campo de estado,
        /// se considera activo si el usuario existe.
        /// </summary>
        public bool UsuarioActivo(string token)
        {
            return ExisteUsuario(token);
        }

        /// <summary>
        /// Obtiene el saldo actual del usuario a partir de su tarjeta. Retorna 0 si no existe.
        /// </summary>
        public decimal ObtenerSaldo(string token)
        {
            var usuario = _gestor.ObtenerUsuarioPorTarjeta(token);
            return usuario != null ? usuario.SaldoActual : 0m;
        }

        /// <summary>
        /// Obtiene el límite diario de retiro permitido para el usuario. Retorna 0 si no existe.
        /// </summary>
        public decimal ObtenerLimiteDiario(string token)
        {
            var usuario = _gestor.ObtenerUsuarioPorTarjeta(token);
            return usuario != null ? usuario.LimiteDiario : 0m;
        }

        /// <summary>
        /// Obtiene la sumatoria de retiros realizados durante el día de hoy (DateTime.Today) por el usuario.
        /// </summary>
        public decimal ObtenerRetirosDelDia(string token)
        {
            var historial = _gestor.ObtenerHistorial(token);
            if (historial == null || historial.Count == 0)
            {
                return 0m;
            }

            return historial
                .Where(t => string.Equals(t.TipoMovimiento, "Retiro", StringComparison.OrdinalIgnoreCase)
                         && t.FechaHora.Date == DateTime.Today)
                .Sum(t => t.Monto);
        }

        /// <summary>
        /// Obtiene el inventario actual de billetes convirtiendo la List&lt;Billete&gt; de la DAL
        /// a un Dictionary&lt;int, int&gt; (Denominación -> Cantidad) requerido por la BLL.
        /// </summary>
        public Dictionary<int, int> ObtenerInventarioBilletes()
        {
            var listaBilletes = _gestor.ObtenerInventarioBilletes();
            var resultado = new Dictionary<int, int>();

            if (listaBilletes != null)
            {
                foreach (var billete in listaBilletes)
                {
                    resultado[billete.Denominacion] = billete.Cantidad;
                }
            }

            return resultado;
        }

        /// <summary>
        /// Actualiza el saldo monetario del usuario especificado.
        /// </summary>
        public void ActualizarSaldo(string token, decimal nuevoSaldo)
        {
            _gestor.ActualizarSaldo(token, nuevoSaldo);
        }

        /// <summary>
        /// Registra una nueva transacción en el sistema creando una entidad Transaccion con marca temporal actual.
        /// </summary>
        public void RegistrarTransaccion(string token, string tipo, decimal monto)
        {
            var transaccion = new Transaccion(token, DateTime.Now, tipo, monto);
            _gestor.RegistrarTransaccion(transaccion);
        }

        /// <summary>
        /// Convierte el Dictionary&lt;int, int&gt; de billetes recibido de la BLL a una List&lt;Billete&gt;
        /// y la envía a la DAL para su persistencia en el archivo Boveda.csv.
        /// </summary>
        public void ActualizarInventarioBilletes(Dictionary<int, int> nuevosBilletes)
        {
            var lista = new List<Billete>();

            if (nuevosBilletes != null)
            {
                foreach (var kvp in nuevosBilletes)
                {
                    lista.Add(new Billete(kvp.Key, kvp.Value));
                }
            }

            _gestor.GuardarInventarioBilletes(lista);
        }
    }
}
