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

        // Recibimos la interfaz de Martín. Así BLL no sabe si es CSV, SQL o TXT.
        public UsuarioLogica(IGestorArchivos dal)
        {
            _dal = dal;
        }

        /// <summary>
        /// Cambiar PIN. Requiere que el PIN actual sea correcto.
        /// </summary>
        public bool CambiarPIN(string numeroTarjeta, string pinActual, string nuevoPin)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta); //[cite: 2]

            if (usuario == null || usuario.PIN != pinActual)
                throw new Exception("Credenciales incorrectas.");

            if (string.IsNullOrWhiteSpace(nuevoPin) || nuevoPin.Length != 4 || !nuevoPin.All(char.IsDigit))
                throw new Exception("El nuevo PIN debe tener exactamente 4 dígitos numéricos.");

            usuario.PIN = nuevoPin;
            return _dal.ActualizarUsuario(usuario); //[cite: 2]
        }

        /// <summary>
        /// Realiza un depósito detallando el desglose de denominaciones.
        /// </summary>
        public bool Depositar(string numeroTarjeta, List<Billete> billetesDepositados)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta); //[cite: 2]
            if (usuario == null) throw new Exception("Usuario no encontrado.");

            decimal totalDeposito = billetesDepositados.Sum(b => b.Subtotal); //[cite: 5]
            if (totalDeposito <= 0) throw new Exception("El monto a depositar debe ser mayor a 0.");

            // 1. Sumar al saldo del usuario
            usuario.SaldoActual += totalDeposito; //[cite: 4]
            _dal.ActualizarSaldo(numeroTarjeta, usuario.SaldoActual); //[cite: 2]

            // 2. Sumar los billetes a la bóveda
            var inventario = _dal.ObtenerInventarioBilletes(); //[cite: 2]
            foreach (var billeteDep in billetesDepositados)
            {
                var billeteBoveda = inventario.FirstOrDefault(b => b.Denominacion == billeteDep.Denominacion); //[cite: 5]
                if (billeteBoveda != null)
                {
                    billeteBoveda.Cantidad += billeteDep.Cantidad; //[cite: 5]
                }
            }
            _dal.GuardarInventarioBilletes(inventario); //[cite: 2]

            // 3. Registrar la transacción
            var transaccion = new Transaccion(numeroTarjeta, DateTime.Now, "Depósito", totalDeposito); //[cite: 3]
            return _dal.RegistrarTransaccion(transaccion); //[cite: 2]
        }

        /// <summary>
        /// Lógica de retiro. Valida saldos, límites diarios y calcula el desglose de billetes.
        /// </summary>
        public List<Billete> Retirar(string numeroTarjeta, string pin, decimal montoSolicitado)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta); //[cite: 2]
            if (usuario == null || usuario.PIN != pin) throw new Exception("Credenciales incorrectas.");

            if (montoSolicitado <= 0) throw new Exception("El monto debe ser mayor a 0.");
            if (usuario.SaldoActual < montoSolicitado) throw new Exception("Saldo insuficiente."); //[cite: 4]

            // Validar límite diario
            var transaccionesHoy = _dal.ObtenerHistorial(numeroTarjeta) //[cite: 2]
                .Where(t => t.FechaHora.Date == DateTime.Today && t.TipoMovimiento == "Retiro") //[cite: 3]
                .Sum(t => t.Monto); //[cite: 3]

            if (transaccionesHoy + montoSolicitado > usuario.LimiteDiario) //[cite: 4]
                throw new Exception($"El retiro excede el límite diario. Disponible hoy: {(usuario.LimiteDiario - transaccionesHoy):C2}");

            // Algoritmo para entregar billetes (Desglose)
            var inventario = _dal.ObtenerInventarioBilletes().OrderByDescending(b => b.Denominacion).ToList(); //[cite: 2, 5]
            var billetesAEntregar = new List<Billete>();
            decimal montoRestante = montoSolicitado;

            foreach (var billete in inventario)
            {
                if (montoRestante == 0) break;

                int billetesNecesarios = (int)(montoRestante / billete.Denominacion); //[cite: 5]
                int billetesATomar = Math.Min(billetesNecesarios, billete.Cantidad); //[cite: 5]

                if (billetesATomar > 0)
                {
                    billetesAEntregar.Add(new Billete(billete.Denominacion, billetesATomar)); //[cite: 5]
                    montoRestante -= billetesATomar * billete.Denominacion; //[cite: 5]
                    billete.Cantidad -= billetesATomar; // Restamos de la bóveda temporalmente[cite: 5]
                }
            }

            if (montoRestante > 0)
                throw new Exception("El cajero no cuenta con la denominación de billetes exacta para este monto.");

            // Si llegamos aquí, la transacción es válida. Guardamos todo.
            usuario.SaldoActual -= montoSolicitado; //[cite: 4]
            _dal.ActualizarSaldo(numeroTarjeta, usuario.SaldoActual); //[cite: 2]
            _dal.GuardarInventarioBilletes(inventario); //[cite: 2]

            var transaccion = new Transaccion(numeroTarjeta, DateTime.Now, "Retiro", montoSolicitado); //[cite: 3]
            _dal.RegistrarTransaccion(transaccion); //[cite: 2]

            return billetesAEntregar; // Retornamos el desglose para que la UI (Dev 5) lo muestre en pantalla.
        }

        /// <summary>
        /// Obtiene las últimas 5 transacciones.
        /// </summary>
        public List<Transaccion> VerUltimasTransacciones(string numeroTarjeta)
        {
            return _dal.ObtenerHistorial(numeroTarjeta) //[cite: 2]
                       .OrderByDescending(t => t.FechaHora) //[cite: 3]
                       .Take(5)
                       .ToList();
        }

        /// <summary>
        /// Retorna el saldo y el disponible diario.
        /// </summary>
        public (decimal Saldo, decimal DisponibleDiario) VerSaldo(string numeroTarjeta)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta); //[cite: 2]
            if (usuario == null) throw new Exception("Usuario no encontrado.");

            var transaccionesHoy = _dal.ObtenerHistorial(numeroTarjeta) //[cite: 2]
                .Where(t => t.FechaHora.Date == DateTime.Today && t.TipoMovimiento == "Retiro") //[cite: 3]
                .Sum(t => t.Monto); //[cite: 3]

            return (usuario.SaldoActual, usuario.LimiteDiario - transaccionesHoy); //[cite: 4]
        }
        /// <summary>
        /// Lógica de retiro manual donde el usuario elige cuántos billetes de cada denominación quiere[cite: 1].
        /// </summary>
        public bool RetirarConDesgloseManual(string numeroTarjeta, string pin, decimal montoSolicitado, List<Billete> desgloseSolicitado)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta); //[cite: 2]
            if (usuario == null || usuario.PIN != pin) throw new Exception("Credenciales incorrectas.");

            if (montoSolicitado <= 0) throw new Exception("El monto debe ser mayor a 0.");
            if (usuario.SaldoActual < montoSolicitado) throw new Exception("Saldo insuficiente."); //[cite: 4]

            // 1. Validar que el desglose coincida con el total
            decimal sumaDesglose = desgloseSolicitado.Sum(b => b.Subtotal); //[cite: 5]
            if (sumaDesglose != montoSolicitado)
                throw new Exception("La suma de los billetes solicitados no coincide con el monto total a retirar.");

            // 2. Validar límite diario
            var transaccionesHoy = _dal.ObtenerHistorial(numeroTarjeta) //[cite: 2]
                .Where(t => t.FechaHora.Date == DateTime.Today && t.TipoMovimiento == "Retiro") //[cite: 3]
                .Sum(t => t.Monto); //[cite: 3]

            if (transaccionesHoy + montoSolicitado > usuario.LimiteDiario) //[cite: 4]
                throw new Exception($"El retiro excede el límite diario. Disponible hoy: {(usuario.LimiteDiario - transaccionesHoy):C2}");

            // 3. Validar que la bóveda tenga esos billetes específicos
            var inventario = _dal.ObtenerInventarioBilletes(); //[cite: 2]
            foreach (var billetePedido in desgloseSolicitado)
            {
                var billeteBoveda = inventario.FirstOrDefault(b => b.Denominacion == billetePedido.Denominacion); //[cite: 5]

                if (billeteBoveda == null || billeteBoveda.Cantidad < billetePedido.Cantidad) //[cite: 5]
                    throw new Exception($"No hay suficientes billetes de Q{billetePedido.Denominacion} en el cajero.");

                // Si hay, los vamos restando temporalmente
                billeteBoveda.Cantidad -= billetePedido.Cantidad; //[cite: 5]
            }

            // 4. Si pasamos todas las validaciones, confirmamos los cambios
            usuario.SaldoActual -= montoSolicitado; //[cite: 4]
            _dal.ActualizarSaldo(numeroTarjeta, usuario.SaldoActual); //[cite: 2]
            _dal.GuardarInventarioBilletes(inventario); //[cite: 2]

            var transaccion = new Transaccion(numeroTarjeta, DateTime.Now, "Retiro", montoSolicitado); //[cite: 3]
            return _dal.RegistrarTransaccion(transaccion); //[cite: 2]
        }
    }
}