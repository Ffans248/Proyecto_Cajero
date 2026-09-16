using System;
using System.Collections.Generic;
using System.Linq;
using Cajerro.DAL.Interfaces;
using Cajerro.DAL.Modelos;

namespace Cajero.BLL
{
    public class UsuarioLogica
    {
        private readonly IGestorArchivos _dal;

        // Recibimos la interfaz. Así BLL no sabe si es CSV, SQL o TXT.
        public UsuarioLogica(IGestorArchivos dal)
        {
            _dal = dal;
        }

        /// <summary>
        /// Cambiar PIN. Requiere que el PIN actual sea correcto.
        /// </summary>
        public bool CambiarPIN(string numeroTarjeta, string pinActual, string nuevoPin)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);

            if (usuario == null || usuario.PIN != pinActual)
                throw new Exception("Credenciales incorrectas.");

            if (string.IsNullOrWhiteSpace(nuevoPin) || nuevoPin.Length != 4 || !nuevoPin.All(char.IsDigit))
                throw new Exception("El nuevo PIN debe tener exactamente 4 dígitos numéricos.");

            usuario.PIN = nuevoPin;
            return _dal.ActualizarUsuario(usuario);
        }

        /// <summary>
        /// Realiza un depósito detallando el desglose de denominaciones.
        /// </summary>
        public bool Depositar(string numeroTarjeta, List<Billete> billetesDepositados)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);
            if (usuario == null) throw new Exception("Usuario no encontrado.");

            decimal totalDeposito = billetesDepositados.Sum(b => b.Subtotal);
            if (totalDeposito <= 0) throw new Exception("El monto a depositar debe ser mayor a 0.");

            // 1. Sumar al saldo del usuario
            usuario.SaldoActual += totalDeposito;
            _dal.ActualizarSaldo(numeroTarjeta, usuario.SaldoActual);

            // 2. Sumar los billetes a la bóveda
            var inventario = _dal.ObtenerInventarioBilletes();
            foreach (var billeteDep in billetesDepositados)
            {
                var billeteBoveda = inventario.FirstOrDefault(b => b.Denominacion == billeteDep.Denominacion);
                if (billeteBoveda != null)
                {
                    billeteBoveda.Cantidad += billeteDep.Cantidad;
                }
            }
            _dal.GuardarInventarioBilletes(inventario);

            // 3. Registrar la transacción
            var transaccion = new Transaccion(numeroTarjeta, DateTime.Now, "Depósito", totalDeposito);
            return _dal.RegistrarTransaccion(transaccion);
        }

        /// <summary>
        /// Lógica de retiro. Valida saldos, límites diarios y calcula el desglose de billetes.
        /// </summary>
        public List<Billete> Retirar(string numeroTarjeta, string pin, decimal montoSolicitado)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);
            if (usuario == null || usuario.PIN != pin) throw new Exception("Credenciales incorrectas.");

            if (montoSolicitado <= 0) throw new Exception("El monto debe ser mayor a 0.");
            if (usuario.SaldoActual < montoSolicitado) throw new Exception("Saldo insuficiente.");

            // Validar límite diario
            var transaccionesHoy = _dal.ObtenerHistorial(numeroTarjeta)
                .Where(t => t.FechaHora.Date == DateTime.Today && t.TipoMovimiento == "Retiro")
                .Sum(t => t.Monto);

            if (transaccionesHoy + montoSolicitado > usuario.LimiteDiario)
                throw new Exception($"El retiro excede el límite diario. Disponible hoy: {(usuario.LimiteDiario - transaccionesHoy):C2}");

            // Algoritmo para entregar billetes (Desglose)
            var inventario = _dal.ObtenerInventarioBilletes().OrderByDescending(b => b.Denominacion).ToList();
            var billetesAEntregar = new List<Billete>();
            decimal montoRestante = montoSolicitado;

            foreach (var billete in inventario)
            {
                if (montoRestante == 0) break;

                int billetesNecesarios = (int)(montoRestante / billete.Denominacion);
                int billetesATomar = Math.Min(billetesNecesarios, billete.Cantidad);

                if (billetesATomar > 0)
                {
                    billetesAEntregar.Add(new Billete(billete.Denominacion, billetesATomar));
                    montoRestante -= billetesATomar * billete.Denominacion;
                    billete.Cantidad -= billetesATomar; // Restamos de la bóveda temporalmente
                }
            }

            if (montoRestante > 0)
                throw new Exception("El cajero no cuenta con la denominación de billetes exacta para este monto.");

            // Si llegamos aquí, la transacción es válida. Guardamos todo.
            usuario.SaldoActual -= montoSolicitado;
            _dal.ActualizarSaldo(numeroTarjeta, usuario.SaldoActual);
            _dal.GuardarInventarioBilletes(inventario);

            var transaccion = new Transaccion(numeroTarjeta, DateTime.Now, "Retiro", montoSolicitado);
            _dal.RegistrarTransaccion(transaccion);

            return billetesAEntregar;
        }

        /// <summary>
        /// Obtiene las últimas 5 transacciones.
        /// </summary>
        public List<Transaccion> VerUltimasTransacciones(string numeroTarjeta)
        {
            return _dal.ObtenerHistorial(numeroTarjeta)
                       .OrderByDescending(t => t.FechaHora)
                       .Take(5)
                       .ToList();
        }

        /// <summary>
        /// Retorna el saldo y el disponible diario.
        /// </summary>
        public (decimal Saldo, decimal DisponibleDiario) VerSaldo(string numeroTarjeta)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);
            if (usuario == null) throw new Exception("Usuario no encontrado.");

            var transaccionesHoy = _dal.ObtenerHistorial(numeroTarjeta)
                .Where(t => t.FechaHora.Date == DateTime.Today && t.TipoMovimiento == "Retiro")
                .Sum(t => t.Monto);

            return (usuario.SaldoActual, usuario.LimiteDiario - transaccionesHoy);
        }

        /// <summary>
        /// Lógica de retiro manual donde el usuario elige cuántos billetes de cada denominación quiere.
        /// </summary>
        public bool RetirarConDesgloseManual(string numeroTarjeta, string pin, decimal montoSolicitado, List<Billete> desgloseSolicitado)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);
            if (usuario == null || usuario.PIN != pin) throw new Exception("Credenciales incorrectas.");

            if (montoSolicitado <= 0) throw new Exception("El monto debe ser mayor a 0.");
            if (usuario.SaldoActual < montoSolicitado) throw new Exception("Saldo insuficiente.");

            // 1. Validar que el desglose coincida con el total
            decimal sumaDesglose = desgloseSolicitado.Sum(b => b.Subtotal);
            if (sumaDesglose != montoSolicitado)
                throw new Exception("La suma de los billetes solicitados no coincide con el monto total a retirar.");

            // 2. Validar límite diario
            var transaccionesHoy = _dal.ObtenerHistorial(numeroTarjeta)
                .Where(t => t.FechaHora.Date == DateTime.Today && t.TipoMovimiento == "Retiro")
                .Sum(t => t.Monto);

            if (transaccionesHoy + montoSolicitado > usuario.LimiteDiario)
                throw new Exception($"El retiro excede el límite diario. Disponible hoy: {(usuario.LimiteDiario - transaccionesHoy):C2}");

            // 3. Validar que la bóveda tenga esos billetes específicos
            var inventario = _dal.ObtenerInventarioBilletes();
            foreach (var billetePedido in desgloseSolicitado)
            {
                var billeteBoveda = inventario.FirstOrDefault(b => b.Denominacion == billetePedido.Denominacion);

                if (billeteBoveda == null || billeteBoveda.Cantidad < billetePedido.Cantidad)
                    throw new Exception($"No hay suficientes billetes de Q{billetePedido.Denominacion} en el cajero.");

                // Si hay, los vamos restando temporalmente
                billeteBoveda.Cantidad -= billetePedido.Cantidad;
            }

            // 4. Si pasamos todas las validaciones, confirmamos los cambios
            usuario.SaldoActual -= montoSolicitado;
            _dal.ActualizarSaldo(numeroTarjeta, usuario.SaldoActual);
            _dal.GuardarInventarioBilletes(inventario);

            var transaccion = new Transaccion(numeroTarjeta, DateTime.Now, "Retiro", montoSolicitado);
            return _dal.RegistrarTransaccion(transaccion);
        }
    }
}