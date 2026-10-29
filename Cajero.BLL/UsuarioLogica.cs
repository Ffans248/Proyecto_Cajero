using System;
using System.Collections.Generic;
using System.Linq;
using Cajerro.DAL;
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

        /// <summary>
        /// Genera un token único de registro para transferencias entre usuarios e interbancarias (TRF-YYYYMMDD-XXXXXX).
        /// </summary>
        public static string GenerarTokenTransferencia()
        {
            return $"TRF-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";
        }

        /// <summary>
        /// Verifica si existe el usuario o número de cuenta en nuestro banco (transferencia interna)
        /// o en el banco seleccionado (transferencia interbancaria), devolviendo también los límites disponibles de ambos bancos.
        /// </summary>
        public (bool Existe, string CuentaNormalizada, string NombreTitular, string CodigoBanco, string NombreBanco, decimal CupoBancoOrigen, decimal CupoBancoDestino, string Mensaje) VerificarCuentaDestino(
            bool esInterbancaria,
            string codigoBancoDestino,
            string cuentaDestino,
            string tarjetaOrigen)
        {
            if (string.IsNullOrWhiteSpace(cuentaDestino))
            {
                return (false, "", "", "", "", 0m, 0m, "Debe ingresar un número de cuenta o tarjeta de destino.");
            }

            string ctaBuscada = cuentaDestino.Trim();
            var bancos = _dal.ObtenerBancosRed();
            var bancoPropio = bancos.FirstOrDefault(b => b.EsPropio) ?? new BancoRedInfo
            {
                CodigoBanco = "BANC01",
                NombreBanco = "NEXUSBANK (Nuestro Banco)",
                EsPropio = true,
                LimiteDiario = 25000m
            };

            if (!esInterbancaria || string.IsNullOrWhiteSpace(codigoBancoDestino) || codigoBancoDestino.Equals(bancoPropio.CodigoBanco, StringComparison.OrdinalIgnoreCase))
            {
                var destLocal = _dal.ObtenerUsuarioPorTarjeta(ctaBuscada);
                if (destLocal == null)
                {
                    return (false, ctaBuscada, "", bancoPropio.CodigoBanco, bancoPropio.NombreBanco, bancoPropio.DisponibleHoy, bancoPropio.DisponibleHoy,
                        $"No existe ningún usuario con la cuenta o tarjeta '{ctaBuscada}' en {bancoPropio.NombreBanco}.");
                }

                if (destLocal.NumeroTarjeta.Equals(tarjetaOrigen?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return (false, destLocal.NumeroTarjeta, destLocal.Nombre, bancoPropio.CodigoBanco, bancoPropio.NombreBanco, bancoPropio.DisponibleHoy, bancoPropio.DisponibleHoy,
                        "No puede realizar una transferencia hacia su misma cuenta.");
                }

                return (true, destLocal.NumeroTarjeta, destLocal.Nombre, bancoPropio.CodigoBanco, bancoPropio.NombreBanco, bancoPropio.DisponibleHoy, bancoPropio.DisponibleHoy,
                    $"Usuario verificado en {bancoPropio.NombreBanco}: {destLocal.Nombre}");
            }
            else
            {
                var bancoExt = bancos.FirstOrDefault(b => b.CodigoBanco.Equals(codigoBancoDestino.Trim(), StringComparison.OrdinalIgnoreCase));
                if (bancoExt == null)
                {
                    return (false, ctaBuscada, "", codigoBancoDestino, "Banco Desconocido", bancoPropio.DisponibleHoy, 0m,
                        $"El banco seleccionado ({codigoBancoDestino}) no existe en la red interbancaria.");
                }

                // 1. Si el banco externo está detectado en red (IP real != 127.0.0.1), consultar en vivo por TCP 5000
                if (bancoExt.EnLinea && !string.IsNullOrWhiteSpace(bancoExt.IP) && bancoExt.IP != "127.0.0.1")
                {
                    var rem = DescubridorRedBancaria.VerificarCuentaEnBancoRemotoTcp(bancoExt, bancoPropio.CodigoBanco, ctaBuscada);
                    if (rem.respondioRed)
                    {
                        decimal cupoRem = rem.cupoRemoto ?? bancoExt.DisponibleHoy;
                        if (!rem.existe)
                        {
                            return (false, ctaBuscada, "", bancoExt.CodigoBanco, bancoExt.NombreBanco, bancoPropio.DisponibleHoy, cupoRem,
                                $"El banco {bancoExt.NombreBanco} ({bancoExt.IP}) respondió que la cuenta '{ctaBuscada}' NO existe.");
                        }

                        return (true, rem.numeroCuenta, rem.nombreTitular, bancoExt.CodigoBanco, bancoExt.NombreBanco,
                            bancoPropio.DisponibleHoy, cupoRem,
                            $"Cuenta verificada en red con {bancoExt.NombreBanco} ({bancoExt.IP}): {rem.nombreTitular}");
                    }
                }

                // 2. Respaldo en padrón de cuentas externas (CuentasExternas.csv)
                var cuentasBanco = _dal.ObtenerCuentasExternas(bancoExt.CodigoBanco);
                var ctaExterna = cuentasBanco.FirstOrDefault(c => c.Activa && c.NumeroCuenta.Equals(ctaBuscada, StringComparison.OrdinalIgnoreCase));

                if (ctaExterna == null)
                {
                    return (false, ctaBuscada, "", bancoExt.CodigoBanco, bancoExt.NombreBanco, bancoPropio.DisponibleHoy, bancoExt.DisponibleHoy,
                        $"No existe el usuario con número de cuenta '{ctaBuscada}' en {bancoExt.NombreBanco}.");
                }

                return (true, ctaExterna.NumeroCuenta, ctaExterna.NombreTitular, bancoExt.CodigoBanco, bancoExt.NombreBanco,
                    bancoPropio.DisponibleHoy, bancoExt.DisponibleHoy,
                    $"Cuenta verificada en {bancoExt.NombreBanco}: {ctaExterna.NombreTitular}");
            }
        }

        /// <summary>
        /// Ejecuta una transferencia entre usuarios del mismo banco o una transferencia interbancaria.
        /// - Genera un TokenTransferencia único que se guarda en: Nuestro Banco, El Otro Banco y el Historial del Usuario.
        /// - En transferencias interbancarias, resta el monto al límite diario de los dos bancos coordinándose.
        /// </summary>
        public RegistroTransferenciaBancaria RealizarTransferencia(
            string tarjetaOrigen,
            string pin,
            bool esInterbancaria,
            string codigoBancoDestino,
            string cuentaDestino,
            decimal monto,
            string concepto,
            string? tokenPreGenerado = null)
        {
            var emisor = _dal.ObtenerUsuarioPorTarjeta(tarjetaOrigen);
            if (emisor == null)
                throw new Exception("Usuario de origen no encontrado.");

            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
                throw new Exception("El PIN de seguridad debe contener exactamente 4 dígitos numéricos.");

            if (emisor.PIN != pin)
                throw new Exception("El PIN de seguridad ingresado es incorrecto.");

            if (monto <= 0)
                throw new Exception("El monto a transferir debe ser mayor a Q 0.00.");

            if (emisor.SaldoActual < monto)
                throw new Exception($"Saldo insuficiente en su cuenta (Saldo actual: Q {emisor.SaldoActual:N2}).");

            // Verificar existencia de la cuenta destino en el banco correspondiente
            var verif = VerificarCuentaDestino(esInterbancaria, codigoBancoDestino, cuentaDestino, emisor.NumeroTarjeta);
            if (!verif.Existe)
            {
                throw new Exception(verif.Mensaje);
            }

            var bancos = _dal.ObtenerBancosRed();
            var bancoPropio = bancos.FirstOrDefault(b => b.EsPropio) ?? new BancoRedInfo
            {
                CodigoBanco = "BANC01",
                NombreBanco = "NEXUSBANK (Nuestro Banco)",
                EsPropio = true,
                LimiteDiario = 25000m
            };

            string tokenTransferencia = !string.IsNullOrWhiteSpace(tokenPreGenerado)
                ? tokenPreGenerado.Trim()
                : GenerarTokenTransferencia();

            string conceptoLimpio = string.IsNullOrWhiteSpace(concepto) ? "Transferencia bancaria" : concepto.Trim();

            if (!esInterbancaria || verif.CodigoBanco.Equals(bancoPropio.CodigoBanco, StringComparison.OrdinalIgnoreCase))
            {
                // ================= TRANSFERENCIA INTERNA (ENTRE USUARIOS DE NUESTRO BANCO) =================
                var receptor = _dal.ObtenerUsuarioPorTarjeta(verif.CuentaNormalizada);
                if (receptor == null)
                    throw new Exception("El usuario destinatario no existe en nuestro banco.");

                if (receptor.NumeroTarjeta.Equals(emisor.NumeroTarjeta, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("No puede transferir fondos a su propia cuenta.");

                // 1. Actualizar saldos de ambos usuarios
                emisor.SaldoActual -= monto;
                receptor.SaldoActual += monto;
                _dal.ActualizarSaldo(emisor.NumeroTarjeta, emisor.SaldoActual);
                _dal.ActualizarSaldo(receptor.NumeroTarjeta, receptor.SaldoActual);

                // 2. Guardar token en el historial del usuario emisor y del usuario receptor
                _dal.RegistrarTransaccion(new Transaccion(
                    emisor.NumeroTarjeta,
                    DateTime.Now,
                    $"Transferencia a {receptor.Nombre}",
                    monto,
                    tokenTransferencia));

                _dal.RegistrarTransaccion(new Transaccion(
                    receptor.NumeroTarjeta,
                    DateTime.Now,
                    $"Transferencia de {emisor.Nombre}",
                    monto,
                    tokenTransferencia));

                // 3. Guardar token en el registro del banco
                var reg = new RegistroTransferenciaBancaria
                {
                    TokenTransferencia = tokenTransferencia,
                    FechaHora = DateTime.Now,
                    TipoTransferencia = "INTERNA",
                    BancoOrigen = bancoPropio.CodigoBanco,
                    CuentaOrigen = emisor.NumeroTarjeta,
                    NombreOrigen = emisor.Nombre,
                    BancoDestino = bancoPropio.CodigoBanco,
                    CuentaDestino = receptor.NumeroTarjeta,
                    NombreDestino = receptor.Nombre,
                    Monto = monto,
                    Concepto = conceptoLimpio,
                    CupoRestanteBancoOrigen = bancoPropio.DisponibleHoy,
                    CupoRestanteBancoDestino = bancoPropio.DisponibleHoy,
                    Estado = "APROBADA"
                };
                _dal.RegistrarTransferenciaCompartida(reg);

                _dal.RegistrarEnBitacora(
                    "Transferencia Interna",
                    emisor.NumeroTarjeta,
                    $"Token [{tokenTransferencia}]: Transferencia de Q{monto:N2} a {receptor.Nombre} ({receptor.NumeroTarjeta}). Nuevo saldo: Q{emisor.SaldoActual:N2}.");

                return reg;
            }
            else
            {
                // ================= TRANSFERENCIA INTERBANCARIA COORDINADA (DIFERENTES BANCOS) =================
                var bancoDestino = bancos.FirstOrDefault(b => b.CodigoBanco.Equals(verif.CodigoBanco, StringComparison.OrdinalIgnoreCase));
                if (bancoDestino == null)
                    throw new Exception($"El banco destino '{verif.CodigoBanco}' no se encuentra registrado.");

                // 1. Coordinar límites de AMBOS bancos (nuestro banco y el banco destino deben tener cupo disponible)
                if (monto > bancoPropio.DisponibleHoy)
                {
                    throw new Exception($"El monto (Q {monto:N2}) excede el límite diario disponible de nuestro banco ({bancoPropio.NombreBanco}: Q {bancoPropio.DisponibleHoy:N2}).");
                }

                if (monto > bancoDestino.DisponibleHoy)
                {
                    throw new Exception($"El monto (Q {monto:N2}) excede el límite diario disponible del banco destino ({bancoDestino.NombreBanco}: Q {bancoDestino.DisponibleHoy:N2}).");
                }

                // Si el banco destino está en línea en la red local, enviarle IB_TRANSFERIR con el TokenTransferencia por TCP 5000
                if (bancoDestino.EnLinea && !string.IsNullOrWhiteSpace(bancoDestino.IP) && bancoDestino.IP != "127.0.0.1")
                {
                    var envRed = DescubridorRedBancaria.EnviarTransferenciaABancoRemotoTcp(
                        bancoDestino,
                        tokenTransferencia,
                        bancoPropio.CodigoBanco,
                        emisor.NumeroTarjeta,
                        emisor.Nombre,
                        verif.CuentaNormalizada,
                        verif.NombreTitular,
                        monto,
                        conceptoLimpio);

                    if (envRed.respondioRed && !envRed.aprobada)
                    {
                        throw new Exception($"El banco destino ({bancoDestino.NombreBanco}) rechazó la transferencia: {envRed.mensaje}");
                    }
                }

                // 2. Restar el monto al límite diario de LOS DOS BANCOS coordinadamente
                bancoPropio.TransferidoHoy += monto;
                bancoDestino.TransferidoHoy += monto;
                _dal.GuardarBancosRed(bancos);

                // 3. Debitar saldo a nuestro usuario y acreditar a la cuenta del otro banco
                emisor.SaldoActual -= monto;
                _dal.ActualizarSaldo(emisor.NumeroTarjeta, emisor.SaldoActual);

                var cuentasExt = _dal.ObtenerCuentasExternas(bancoDestino.CodigoBanco);
                var ctaExt = cuentasExt.FirstOrDefault(c => c.NumeroCuenta.Equals(verif.CuentaNormalizada, StringComparison.OrdinalIgnoreCase));
                if (ctaExt != null)
                {
                    ctaExt.SaldoActual += monto;
                    _dal.GuardarCuentaExterna(ctaExt);
                }

                // 4. Guardar el TokenTransferencia en el Usuario (Transacciones.csv)
                _dal.RegistrarTransaccion(new Transaccion(
                    emisor.NumeroTarjeta,
                    DateTime.Now,
                    $"Transf. {bancoDestino.CodigoBanco} ({verif.CuentaNormalizada})",
                    monto,
                    tokenTransferencia));

                // 5. Guardar el mismo TokenTransferencia en Nuestro Banco y en El Otro Banco
                var reg = new RegistroTransferenciaBancaria
                {
                    TokenTransferencia = tokenTransferencia,
                    FechaHora = DateTime.Now,
                    TipoTransferencia = "INTERBANCARIA",
                    BancoOrigen = bancoPropio.CodigoBanco,
                    CuentaOrigen = emisor.NumeroTarjeta,
                    NombreOrigen = emisor.Nombre,
                    BancoDestino = bancoDestino.CodigoBanco,
                    CuentaDestino = verif.CuentaNormalizada,
                    NombreDestino = verif.NombreTitular,
                    Monto = monto,
                    Concepto = conceptoLimpio,
                    CupoRestanteBancoOrigen = bancoPropio.DisponibleHoy,
                    CupoRestanteBancoDestino = bancoDestino.DisponibleHoy,
                    Estado = "APROBADA"
                };
                _dal.RegistrarTransferenciaCompartida(reg);

                _dal.RegistrarEnBitacora(
                    "Transferencia Interbancaria",
                    emisor.NumeroTarjeta,
                    $"Token [{tokenTransferencia}]: Q{monto:N2} transferidos a {verif.NombreTitular} ({bancoDestino.CodigoBanco} - Cta {verif.CuentaNormalizada}). Cupo restante {bancoPropio.CodigoBanco}: Q{bancoPropio.DisponibleHoy:N2} | Cupo restante {bancoDestino.CodigoBanco}: Q{bancoDestino.DisponibleHoy:N2}.");

                return reg;
            }
        }
    }
}
