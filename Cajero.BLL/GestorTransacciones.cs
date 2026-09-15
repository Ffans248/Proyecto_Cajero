using System;
using System.Collections.Generic;
using System.Linq;

namespace Cajero.BLL
{
    public class GestorTransacciones
    {
        private readonly IDatosCajero _datosCajero;

        public GestorTransacciones(IDatosCajero datosCajero)
        {
            _datosCajero = datosCajero;
        }

        public RespuestaTransaccion CargaInicialBoveda(Dictionary<int, int> billetes)
        {
            decimal monto = CalcularMontoTotal(billetes);
            if (monto > 10000)
            {
                return new RespuestaTransaccion { Exito = false, Mensaje = "El monto inicial no puede superar los Q10,000." };
            }

            _datosCajero.ActualizarInventarioBilletes(billetes);
            return new RespuestaTransaccion { Exito = true, Mensaje = $"Carga inicial exitosa. Total en bóveda: Q{monto}" };
        }

        public RespuestaTransaccion RecargarBoveda(Dictionary<int, int> billetes)
        {
            decimal stockActual = ObtenerStockActual();
            decimal montoRecarga = CalcularMontoTotal(billetes);

            if (stockActual + montoRecarga > 30000)
            {
                return new RespuestaTransaccion { Exito = false, Mensaje = "La recarga excede el límite máximo de Q30,000 en la bóveda." };
            }

            var inventarioActual = _datosCajero.ObtenerInventarioBilletes();
            var nuevoInventario = SumarInventarios(inventarioActual, billetes);

            _datosCajero.ActualizarInventarioBilletes(nuevoInventario);
            return new RespuestaTransaccion { Exito = true, Mensaje = $"Recarga exitosa. Nuevo total en bóveda: Q{stockActual + montoRecarga}" };
        }

        public decimal ObtenerStockActual()
        {
            var inventario = _datosCajero.ObtenerInventarioBilletes();
            return CalcularMontoTotal(inventario);
        }

        public RespuestaTransaccion ProcesarRetiro(string token, decimal monto)
        {
            // Validaciones básicas
            if (monto <= 0)
                return new RespuestaTransaccion { Exito = false, Mensaje = "El monto debe ser mayor a cero." };

            // Filtro 1: Validación de Usuario
            if (!_datosCajero.ExisteUsuario(token))
                return new RespuestaTransaccion { Exito = false, Mensaje = "Usuario no existe." };
            if (!_datosCajero.UsuarioActivo(token))
                return new RespuestaTransaccion { Exito = false, Mensaje = "Usuario inactivo." };

            // Filtro 2: Validación de Saldo
            decimal saldoActual = _datosCajero.ObtenerSaldo(token);
            if (monto > saldoActual)
                return new RespuestaTransaccion { Exito = false, Mensaje = "Fondos insuficientes en la cuenta." };

            // Filtro 3: Límite Diario
            decimal limiteDiario = _datosCajero.ObtenerLimiteDiario(token);
            decimal retirosHoy = _datosCajero.ObtenerRetirosDelDia(token);
            if (retirosHoy + monto > limiteDiario)
                return new RespuestaTransaccion { Exito = false, Mensaje = "El retiro excede su límite diario permitido." };

            // Filtro 4: Validación de Bóveda
            decimal stockBoveda = ObtenerStockActual();
            if (monto > stockBoveda)
                return new RespuestaTransaccion { Exito = false, Mensaje = "El cajero no cuenta con fondos suficientes para este retiro." };

            // Algoritmo de Desglose de Billetes
            var inventarioActual = _datosCajero.ObtenerInventarioBilletes();
            var desglose = CalcularDesgloseBilletes(monto, inventarioActual);

            if (desglose == null)
            {
                return new RespuestaTransaccion { Exito = false, Mensaje = "El cajero no tiene las denominaciones necesarias para dar este monto exacto." };
            }

            // Si pasa todo, actualizamos datos en persistencia
            _datosCajero.ActualizarSaldo(token, saldoActual - monto);
            _datosCajero.RegistrarTransaccion(token, "Retiro", monto);
            
            // Restar billetes del inventario
            var inventarioFinal = RestarInventarios(inventarioActual, desglose);
            _datosCajero.ActualizarInventarioBilletes(inventarioFinal);

            return new RespuestaTransaccion 
            { 
                Exito = true, 
                Mensaje = "Retiro procesado exitosamente.",
                BilletesDesglosados = desglose
            };
        }

        public RespuestaTransaccion ProcesarDeposito(string token, Dictionary<int, int> billetesDepositados)
        {
            if (billetesDepositados == null || !billetesDepositados.Any(b => b.Value > 0))
                return new RespuestaTransaccion { Exito = false, Mensaje = "No se ha ingresado ningún billete." };

            if (!_datosCajero.ExisteUsuario(token))
                return new RespuestaTransaccion { Exito = false, Mensaje = "Usuario no existe." };
            if (!_datosCajero.UsuarioActivo(token))
                return new RespuestaTransaccion { Exito = false, Mensaje = "Usuario inactivo." };

            decimal monto = CalcularMontoTotal(billetesDepositados);
            decimal saldoActual = _datosCajero.ObtenerSaldo(token);

            // Depósito aumenta el saldo de la cuenta
            _datosCajero.ActualizarSaldo(token, saldoActual + monto);
            _datosCajero.RegistrarTransaccion(token, "Depósito", monto);

            // Se añaden los billetes físicamente a la bóveda
            var inventarioActual = _datosCajero.ObtenerInventarioBilletes();
            var nuevoInventario = SumarInventarios(inventarioActual, billetesDepositados);
            _datosCajero.ActualizarInventarioBilletes(nuevoInventario);

            return new RespuestaTransaccion 
            { 
                Exito = true, 
                Mensaje = $"Depósito de Q{monto} procesado exitosamente." 
            };
        }

        /// <summary>
        /// Algoritmo Greedy que intenta formar el monto dando primero billetes de mayor denominación.
        /// Si no lo logra, retorna null (transacción cancelada por falta de denominaciones correctas).
        /// </summary>
        private Dictionary<int, int>? CalcularDesgloseBilletes(decimal montoRequerido, Dictionary<int, int> inventario)
        {
            decimal montoFaltante = montoRequerido;
            var billetesAEntregar = new Dictionary<int, int>();

            // Ordenar denominaciones de mayor a menor (Q200, Q100, Q50, etc.)
            var denominaciones = inventario.Keys.OrderByDescending(k => k).ToList();

            foreach (var denom in denominaciones)
            {
                if (montoFaltante == 0) break;

                if (inventario.TryGetValue(denom, out int cantidadDisponible) && cantidadDisponible > 0)
                {
                    if (denom <= montoFaltante)
                    {
                        int cantidadNecesaria = (int)(montoFaltante / denom);
                        int cantidadADar = Math.Min(cantidadNecesaria, cantidadDisponible);

                        if (cantidadADar > 0)
                        {
                            billetesAEntregar.Add(denom, cantidadADar);
                            montoFaltante -= (denom * cantidadADar);
                        }
                    }
                }
            }

            // Si al terminar no logramos cubrir el monto exacto, devolvemos null para que el cajero rechace el retiro.
            if (montoFaltante > 0)
            {
                return null;
            }

            return billetesAEntregar;
        }

        private decimal CalcularMontoTotal(Dictionary<int, int> billetes)
        {
            decimal total = 0;
            if (billetes != null)
            {
                foreach (var kvp in billetes)
                {
                    total += kvp.Key * kvp.Value;
                }
            }
            return total;
        }

        private Dictionary<int, int> SumarInventarios(Dictionary<int, int> invActual, Dictionary<int, int> nuevosBilletes)
        {
            var resultado = new Dictionary<int, int>();
            
            if (invActual != null)
            {
                foreach (var kvp in invActual)
                {
                    resultado[kvp.Key] = kvp.Value;
                }
            }

            if (nuevosBilletes != null)
            {
                foreach (var kvp in nuevosBilletes)
                {
                    if (resultado.ContainsKey(kvp.Key))
                        resultado[kvp.Key] += kvp.Value;
                    else
                        resultado[kvp.Key] = kvp.Value;
                }
            }
            return resultado;
        }

        private Dictionary<int, int> RestarInventarios(Dictionary<int, int> invActual, Dictionary<int, int> billetesARestar)
        {
            var resultado = new Dictionary<int, int>(invActual);
            if (billetesARestar != null)
            {
                foreach (var kvp in billetesARestar)
                {
                    if (resultado.ContainsKey(kvp.Key))
                    {
                        resultado[kvp.Key] -= kvp.Value;
                        if (resultado[kvp.Key] < 0) resultado[kvp.Key] = 0; 
                    }
                }
            }
            return resultado;
        }
    }
}
