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
        /// Cambiar PIN. Requiere que el PIN actual sea correcto de 4 dígitos y token de validación.
        /// </summary>
        public bool CambiarPIN(string numeroTarjeta, string pinActual, string nuevoPin, string token = "")
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);

            if (usuario == null)
                throw new Exception("Usuario no encontrado.");

            if (string.IsNullOrWhiteSpace(pinActual) || pinActual.Length != 4 || !pinActual.All(char.IsDigit))
                throw new Exception("El PIN actual debe constar exactamente de 4 dígitos numéricos.");

            if (usuario.PIN != pinActual)
                throw new Exception("El PIN actual ingresado es incorrecto.");

            if (!string.IsNullOrEmpty(token) && (token.Length != 4 || !token.All(char.IsDigit)))
                throw new Exception("El token de seguridad debe tener exactamente 4 dígitos.");

            if (string.IsNullOrWhiteSpace(nuevoPin) || nuevoPin.Length != 4 || !nuevoPin.All(char.IsDigit))
                throw new Exception("El nuevo PIN debe tener exactamente 4 dígitos numéricos.");

            if (pinActual == nuevoPin)
                throw new Exception("El nuevo PIN no puede ser idéntico al PIN anterior.");

            usuario.PIN = nuevoPin;
            bool exito = _dal.ActualizarUsuario(usuario);

            if (exito)
            {
                _dal.RegistrarEnBitacora("Cambio de PIN", numeroTarjeta, $"Cambio de PIN realizado exitosamente para el usuario {usuario.Nombre}.");
            }

            return exito;
        }

        /// <summary>
        /// Realiza un depósito detallando el desglose de denominaciones y registrando el token de seguridad.
        /// </summary>
        public bool Depositar(string numeroTarjeta, List<Billete> billetesDepositados, string token = "")
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);
            if (usuario == null) throw new Exception("Usuario no encontrado.");

            if (!string.IsNullOrEmpty(token) && (token.Length != 4 || !token.All(char.IsDigit)))
                throw new Exception("El token de seguridad debe tener exactamente 4 dígitos numéricos.");

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

            // 3. Registrar la transacción con su token
            var transaccion = new Transaccion(numeroTarjeta, DateTime.Now, "Depósito", totalDeposito, token);
            bool reg = _dal.RegistrarTransaccion(transaccion);

            // 4. Registrar en bitácora administrativa
            _dal.RegistrarEnBitacora("Depósito", numeroTarjeta, $"Depósito acreditado por Q{totalDeposito:N2}. Saldo resultante: Q{usuario.SaldoActual:N2}. Token: {token}");

            return reg;
        }

        /// <summary>
        /// Lógica de retiro. Valida saldos, límites diarios (incluyendo tarjetas previas) y calcula el desglose de billetes.
        /// </summary>
        public List<Billete> Retirar(string numeroTarjeta, string pin, decimal montoSolicitado, string token = "")
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);
            if (usuario == null) throw new Exception("Usuario no encontrado.");

            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
                throw new Exception("El PIN debe constar de 4 dígitos numéricos.");

            if (usuario.PIN != pin) throw new Exception("Credenciales de PIN incorrectas.");

            if (!string.IsNullOrEmpty(token) && (token.Length != 4 || !token.All(char.IsDigit)))
                throw new Exception("El token debe tener 4 dígitos.");

            if (montoSolicitado <= 0) throw new Exception("El monto debe ser mayor a 0.");
            if (usuario.SaldoActual < montoSolicitado) throw new Exception("Saldo insuficiente en su cuenta.");

            // Validar límite diario acumulado considerando tarjetas anteriores
            decimal transaccionesHoy = ObtenerRetirosAcumuladosHoy(numeroTarjeta);

            if (transaccionesHoy + montoSolicitado > usuario.LimiteDiario)
                throw new Exception($"El retiro excede su límite diario de Q{usuario.LimiteDiario:N2}. Ya ha retirado Q{transaccionesHoy:N2} hoy. Disponible restante: Q{(usuario.LimiteDiario - transaccionesHoy):N2}");

            // Algoritmo Greedy para entregar billetes (Desglose)
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
                    billete.Cantidad -= billetesATomar;
                }
            }

            if (montoRestante > 0)
                throw new Exception("El cajero no cuenta con la denominación de billetes exacta para dispensar este monto.");

            // Si llegamos aquí, confirmamos
            usuario.SaldoActual -= montoSolicitado;
            _dal.ActualizarSaldo(numeroTarjeta, usuario.SaldoActual);
            _dal.GuardarInventarioBilletes(inventario);

            var transaccion = new Transaccion(numeroTarjeta, DateTime.Now, "Retiro", montoSolicitado, token);
            _dal.RegistrarTransaccion(transaccion);

            _dal.RegistrarEnBitacora("Retiro", numeroTarjeta, $"Retiro exitoso por Q{montoSolicitado:N2}. Saldo restante: Q{usuario.SaldoActual:N2}. Token: {token}");

            return billetesAEntregar;
        }

        /// <summary>
        /// Obtiene las últimas transacciones (hasta 5) de un usuario según especificación.
        /// </summary>
        public List<Transaccion> VerUltimasTransacciones(string numeroTarjeta)
        {
            var tarjetas = _dal.ObtenerTarjetasVinculadas(numeroTarjeta);
            var historialTotal = new List<Transaccion>();

            foreach (var tarj in tarjetas)
            {
                historialTotal.AddRange(_dal.ObtenerHistorial(tarj));
            }

            return historialTotal
                       .OrderByDescending(t => t.FechaHora)
                       .Take(5)
                       .ToList();
        }

        /// <summary>
        /// Retorna el saldo actual y el límite disponible restante para el día de hoy,
        /// considerando el acumulado histórico de tarjetas vinculadas.
        /// </summary>
        public (decimal Saldo, decimal DisponibleDiario) VerSaldo(string numeroTarjeta)
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);
            if (usuario == null) throw new Exception("Usuario no encontrado.");

            decimal transaccionesHoy = ObtenerRetirosAcumuladosHoy(numeroTarjeta);
            decimal disponible = Math.Max(0m, usuario.LimiteDiario - transaccionesHoy);

            return (usuario.SaldoActual, disponible);
        }

        /// <summary>
        /// Lógica de retiro manual donde el usuario elige cuántos billetes de cada denominación quiere.
        /// </summary>
        public bool RetirarConDesgloseManual(string numeroTarjeta, string pin, decimal montoSolicitado, List<Billete> desgloseSolicitado, string token = "")
        {
            var usuario = _dal.ObtenerUsuarioPorTarjeta(numeroTarjeta);
            if (usuario == null) throw new Exception("Usuario no encontrado.");

            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
                throw new Exception("El PIN de seguridad debe contener exactamente 4 dígitos.");

            if (usuario.PIN != pin) throw new Exception("PIN de seguridad incorrecto.");

            if (!string.IsNullOrEmpty(token) && (token.Length != 4 || !token.All(char.IsDigit)))
                throw new Exception("El token de confirmación debe contener exactamente 4 dígitos.");

            if (montoSolicitado <= 0) throw new Exception("El monto a retirar debe ser mayor a 0.");
            if (usuario.SaldoActual < montoSolicitado) throw new Exception("Saldo insuficiente en su cuenta.");

            // 1. Validar que el desglose coincida exactamente con el total
            decimal sumaDesglose = desgloseSolicitado.Sum(b => b.Subtotal);
            if (sumaDesglose != montoSolicitado)
                throw new Exception($"La suma de los billetes digitados (Q{sumaDesglose:N2}) no coincide con el total solicitado (Q{montoSolicitado:N2}).");

            // 2. Validar límite diario acumulado considerando tarjetas cambiadas previamente
            decimal transaccionesHoy = ObtenerRetirosAcumuladosHoy(numeroTarjeta);

            if (transaccionesHoy + montoSolicitado > usuario.LimiteDiario)
            {
                decimal disponibleRestante = Math.Max(0m, usuario.LimiteDiario - transaccionesHoy);
                throw new Exception($"El retiro excede su límite diario de Q{usuario.LimiteDiario:N2}. Retirado hoy: Q{transaccionesHoy:N2}. Disponible restante: Q{disponibleRestante:N2}");
            }

            // 3. Validar que la bóveda tenga esos billetes específicos
            var inventario = _dal.ObtenerInventarioBilletes();
            foreach (var billetePedido in desgloseSolicitado.Where(b => b.Cantidad > 0))
            {
                var billeteBoveda = inventario.FirstOrDefault(b => b.Denominacion == billetePedido.Denominacion);

                if (billeteBoveda == null || billeteBoveda.Cantidad < billetePedido.Cantidad)
                    throw new Exception($"El cajero no cuenta con suficientes billetes de Q{billetePedido.Denominacion} (Disponibles: {billeteBoveda?.Cantidad ?? 0}).");

                // Restamos de la bóveda
                billeteBoveda.Cantidad -= billetePedido.Cantidad;
            }

            // 4. Confirmar transacción
            usuario.SaldoActual -= montoSolicitado;
            _dal.ActualizarSaldo(numeroTarjeta, usuario.SaldoActual);
            _dal.GuardarInventarioBilletes(inventario);

            var transaccion = new Transaccion(numeroTarjeta, DateTime.Now, "Retiro", montoSolicitado, token);
            bool exito = _dal.RegistrarTransaccion(transaccion);

            string detalleDesglose = string.Join(", ", desgloseSolicitado.Where(b => b.Cantidad > 0).Select(b => $"{b.Cantidad} de Q{b.Denominacion}"));
            _dal.RegistrarEnBitacora("Retiro", numeroTarjeta, $"Retiro procesado por Q{montoSolicitado:N2} ({detalleDesglose}). Saldo restante: Q{usuario.SaldoActual:N2}. Token: {token}");

            return exito;
        }

        /// <summary>
        /// Calcula la sumatoria de retiros realizados hoy para la tarjeta suministrada y cualquier tarjeta anterior vinculada.
        /// </summary>
        public decimal ObtenerRetirosAcumuladosHoy(string numeroTarjeta)
        {
            var tarjetas = _dal.ObtenerTarjetasVinculadas(numeroTarjeta);
            decimal totalRetiros = 0m;

            foreach (var tarj in tarjetas)
            {
                var historial = _dal.ObtenerHistorial(tarj);
                if (historial != null)
                {
                    totalRetiros += historial
                        .Where(t => t.FechaHora.Date == DateTime.Today && t.TipoMovimiento.Equals("Retiro", StringComparison.OrdinalIgnoreCase))
                        .Sum(t => t.Monto);
                }
            }

            return totalRetiros;
        }

        /// <summary>
        /// Genera un token numérico aleatorio de exactamente 4 dígitos para autorizar una transacción.
        /// </summary>
        public static string GenerarToken4Digitos()
        {
            return Random.Shared.Next(1000, 10000).ToString();
        }
    }
}