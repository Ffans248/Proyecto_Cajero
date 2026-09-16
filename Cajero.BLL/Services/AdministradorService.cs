using Cajero.BLL.DTOs;
using Cajero.BLL.Services.Interfaces;
using Cajero.DAL.Helpers;
using Cajero.DAL.Models;
using Cajero.DAL.Models.Enums;
using Cajero.DAL.Repositories;
using Cajero.DAL.Repositories.Interfaces;

namespace Cajero.BLL.Services
{
    /// <summary>
    /// Implementación de las reglas de negocio para el Módulo Administrativo (Parte 1).
    /// Maneja la creación de usuarios, gestión de límites diarios y control de efectivo en la bóveda
    /// conforme a las restricciones reglamentarias (hasta Q10,000 iniciales y lotes de hasta Q30,000).
    /// </summary>
    public class AdministradorService : IAdministradorService
    {
        private readonly IUsuarioRepository _usuarioRepo;
        private readonly IBovedaRepository _bovedaRepo;
        private readonly ITransaccionRepository _transaccionRepo;

        // Denominaciones oficiales del simulador
        private static readonly HashSet<int> DenominacionesOficiales = new() { 200, 100, 50, 20, 10, 5, 1 };

        public AdministradorService(
            IUsuarioRepository? usuarioRepo = null,
            IBovedaRepository? bovedaRepo = null,
            ITransaccionRepository? transaccionRepo = null)
        {
            _usuarioRepo = usuarioRepo ?? new UsuarioRepository();
            _bovedaRepo = bovedaRepo ?? new BovedaRepository();
            _transaccionRepo = transaccionRepo ?? new TransaccionRepository();
        }

        #region 1. Gestión de Usuarios

        public ResultadoOperacion CrearUsuario(CrearUsuarioDto dto)
        {
            if (dto == null)
            {
                return ResultadoOperacion.Fallo("Los datos del usuario son requeridos.");
            }

            // Validaciones de formato
            string tarjeta = dto.NumeroTarjeta.Trim();
            if (string.IsNullOrWhiteSpace(tarjeta) || !tarjeta.All(char.IsDigit))
            {
                return ResultadoOperacion.Fallo("El número de tarjeta es obligatorio y debe contener solo dígitos.");
            }

            if (tarjeta.Length < 4 || tarjeta.Length > 16)
            {
                return ResultadoOperacion.Fallo("El número de tarjeta debe tener entre 4 y 16 dígitos.");
            }

            string pin = dto.Pin.Trim();
            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
            {
                return ResultadoOperacion.Fallo("El PIN debe constar exactamente de 4 dígitos numéricos.");
            }

            string nombre = dto.NombreCompleto.Trim();
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return ResultadoOperacion.Fallo("El nombre completo es obligatorio.");
            }

            if (dto.SaldoInicial < 0)
            {
                return ResultadoOperacion.Fallo("El saldo inicial no puede ser negativo.");
            }

            if (dto.LimiteDiarioRetiro < 0)
            {
                return ResultadoOperacion.Fallo("El límite diario de retiro no puede ser negativo.");
            }

            // Validación de unicidad
            if (_usuarioRepo.ExisteTarjeta(tarjeta))
            {
                return ResultadoOperacion.Fallo($"El número de tarjeta '{tarjeta}' ya se encuentra registrado.");
            }

            string rfid = dto.RfidUID.Trim();
            if (!string.IsNullOrWhiteSpace(rfid) && _usuarioRepo.ExisteRfid(rfid))
            {
                return ResultadoOperacion.Fallo($"El token RFID '{rfid}' ya está asignado a otro usuario.");
            }

            // Cifrado/Hash del PIN con el algoritmo acordado (SHA-1)
            string pinHash = PinSecurityHelper.EncriptarPin(pin);

            var nuevoUsuario = new Usuario(
                numeroTarjeta: tarjeta,
                pinHash: pinHash,
                nombreCompleto: nombre,
                rfidUID: rfid,
                saldo: dto.SaldoInicial,
                limiteDiarioRetiro: dto.LimiteDiarioRetiro,
                montoRetiradoHoy: 0m,
                ultimaFechaRetiro: DateTime.Today,
                rol: dto.EsAdministrador ? RolUsuario.Administrador : RolUsuario.Cliente,
                estado: EstadoUsuario.Activo
            );

            try
            {
                _usuarioRepo.Insertar(nuevoUsuario);
                return ResultadoOperacion.Ok($"Usuario '{nombre}' registrado exitosamente con la tarjeta {tarjeta}.");
            }
            catch (Exception ex)
            {
                return ResultadoOperacion.Fallo($"Error al guardar el usuario en los registros: {ex.Message}");
            }
        }

        public ResultadoOperacion ModificarLimiteDiario(string numeroTarjeta, decimal nuevoLimite)
        {
            if (string.IsNullOrWhiteSpace(numeroTarjeta))
            {
                return ResultadoOperacion.Fallo("Debe proporcionar un número de tarjeta válido.");
            }

            if (nuevoLimite < 0)
            {
                return ResultadoOperacion.Fallo("El nuevo límite diario de retiro no puede ser negativo.");
            }

            var usuario = _usuarioRepo.ObtenerPorTarjeta(numeroTarjeta.Trim());
            if (usuario == null)
            {
                return ResultadoOperacion.Fallo($"No se encontró ningún usuario asociado a la tarjeta '{numeroTarjeta}'.");
            }

            decimal limiteAnterior = usuario.LimiteDiarioRetiro;
            usuario.LimiteDiarioRetiro = nuevoLimite;

            try
            {
                _usuarioRepo.Actualizar(usuario);
                return ResultadoOperacion.Ok($"Límite diario de retiro actualizado de Q{limiteAnterior:N2} a Q{nuevoLimite:N2} para la tarjeta {numeroTarjeta}.");
            }
            catch (Exception ex)
            {
                return ResultadoOperacion.Fallo($"Error al actualizar el límite en el registro: {ex.Message}");
            }
        }

        public ResultadoOperacion<List<UsuarioDto>> ListarUsuarios()
        {
            try
            {
                var usuarios = _usuarioRepo.ObtenerTodos()
                    .Select(MapearAUsuarioDto)
                    .ToList();

                return ResultadoOperacion<List<UsuarioDto>>.Ok(usuarios);
            }
            catch (Exception ex)
            {
                return ResultadoOperacion<List<UsuarioDto>>.Fallo($"Error al consultar usuarios: {ex.Message}");
            }
        }

        public ResultadoOperacion<UsuarioDto> ConsultarUsuarioPorTarjeta(string numeroTarjeta)
        {
            if (string.IsNullOrWhiteSpace(numeroTarjeta))
            {
                return ResultadoOperacion<UsuarioDto>.Fallo("El número de tarjeta es requerido.");
            }

            var usuario = _usuarioRepo.ObtenerPorTarjeta(numeroTarjeta.Trim());
            if (usuario == null)
            {
                return ResultadoOperacion<UsuarioDto>.Fallo($"Usuario con tarjeta '{numeroTarjeta}' no encontrado.");
            }

            return ResultadoOperacion<UsuarioDto>.Ok(MapearAUsuarioDto(usuario));
        }

        public ResultadoOperacion<UsuarioDto> BuscarUsuario(string identificador)
        {
            if (string.IsNullOrWhiteSpace(identificador))
            {
                return ResultadoOperacion<UsuarioDto>.Fallo("Debe ingresar un número de tarjeta o token RFID para la búsqueda.");
            }

            string valorLimpio = identificador.Trim();

            // 1. Intentar buscar primero por número de tarjeta
            var usuario = _usuarioRepo.ObtenerPorTarjeta(valorLimpio);

            // 2. Si no se encuentra, intentar buscar por token RFID
            usuario ??= _usuarioRepo.ObtenerPorRfid(valorLimpio);

            if (usuario == null)
            {
                return ResultadoOperacion<UsuarioDto>.Fallo($"No se encontró ningún usuario con la tarjeta o token RFID '{identificador}'.");
            }

            return ResultadoOperacion<UsuarioDto>.Ok(MapearAUsuarioDto(usuario));
        }

        #endregion

        #region 2. Gestión Administrativa de la Bóveda

        public ResultadoOperacion InicializarBoveda(List<LoteBilletesDto> lotesIniciales)
        {
            if (lotesIniciales == null || lotesIniciales.Count == 0)
            {
                return ResultadoOperacion.Fallo("Debe proporcionar al menos un lote de billetes para inicializar la bóveda.");
            }

            // Validar que todas las denominaciones sean válidas
            foreach (var lote in lotesIniciales)
            {
                if (!DenominacionesOficiales.Contains(lote.Denominacion))
                {
                    return ResultadoOperacion.Fallo($"La denominación Q{lote.Denominacion} no es válida en este cajero.");
                }

                if (lote.Cantidad < 0)
                {
                    return ResultadoOperacion.Fallo($"La cantidad para billetes de Q{lote.Denominacion} no puede ser negativa.");
                }
            }

            decimal total = lotesIniciales.Sum(l => l.Subtotal);

            // Regla de Negocio: Inicializar hasta Q10,000.00
            if (total > 10000m)
            {
                return ResultadoOperacion.Fallo($"El monto total de inicialización no debe exceder Q10,000.00. Monto actual ingresado: Q{total:N2}.");
            }

            if (total <= 0m)
            {
                return ResultadoOperacion.Fallo("El monto de inicialización debe ser mayor a Q0.00.");
            }

            try
            {
                // Asegurar las 7 denominaciones en el inventario
                var inventarioCompleto = new List<DenominacionBoveda>();
                foreach (int denom in DenominacionesOficiales.OrderByDescending(d => d))
                {
                    var lote = lotesIniciales.FirstOrDefault(l => l.Denominacion == denom);
                    int cantidad = lote?.Cantidad ?? 0;
                    inventarioCompleto.Add(new DenominacionBoveda(denom, cantidad));
                }

                _bovedaRepo.GuardarBoveda(inventarioCompleto);

                // Auditoría en Transacciones.csv
                _transaccionRepo.Registrar(new Transaccion(
                    idTransaccion: Guid.NewGuid().ToString("N"),
                    numeroTarjeta: "ADMIN-BOVEDA",
                    tipo: TipoTransaccion.CargaBoveda,
                    monto: total,
                    fechaHora: DateTime.Now,
                    detalleDesglose: $"Inicialización de Bóveda: Q{total:N2}",
                    saldoPosterior: total
                ));

                return ResultadoOperacion.Ok($"Bóveda inicializada exitosamente con Q{total:N2}.");
            }
            catch (Exception ex)
            {
                return ResultadoOperacion.Fallo($"Error al inicializar la bóveda: {ex.Message}");
            }
        }

        public ResultadoOperacion AgregarLoteBoveda(List<LoteBilletesDto> lotesRecarga)
        {
            if (lotesRecarga == null || lotesRecarga.Count == 0)
            {
                return ResultadoOperacion.Fallo("Debe especificar al menos un lote de billetes para recargar la bóveda.");
            }

            foreach (var lote in lotesRecarga)
            {
                if (!DenominacionesOficiales.Contains(lote.Denominacion))
                {
                    return ResultadoOperacion.Fallo($"La denominación Q{lote.Denominacion} no es válida.");
                }

                if (lote.Cantidad < 0)
                {
                    return ResultadoOperacion.Fallo($"La cantidad para billetes de Q{lote.Denominacion} no puede ser negativa.");
                }
            }

            decimal totalLote = lotesRecarga.Sum(l => l.Subtotal);

            // Regla de Negocio: Agregar lotes hasta Q30,000.00
            if (totalLote > 30000m)
            {
                return ResultadoOperacion.Fallo($"El lote de recarga no puede exceder el límite máximo de Q30,000.00. Monto del lote: Q{totalLote:N2}.");
            }

            if (totalLote <= 0m)
            {
                return ResultadoOperacion.Fallo("El lote de recarga debe tener un valor acumulado mayor a Q0.00.");
            }

            try
            {
                var inventarioActual = _bovedaRepo.ObtenerBoveda();

                // Sumar las cantidades al inventario existente
                foreach (var lote in lotesRecarga)
                {
                    var existente = inventarioActual.FirstOrDefault(d => d.Denominacion == lote.Denominacion);
                    if (existente != null)
                    {
                        existente.Cantidad += lote.Cantidad;
                    }
                    else
                    {
                        inventarioActual.Add(new DenominacionBoveda(lote.Denominacion, lote.Cantidad));
                    }
                }

                _bovedaRepo.GuardarBoveda(inventarioActual);

                decimal nuevoSaldoTotal = inventarioActual.Sum(d => d.Subtotal);

                // Auditoría en Transacciones.csv
                _transaccionRepo.Registrar(new Transaccion(
                    idTransaccion: Guid.NewGuid().ToString("N"),
                    numeroTarjeta: "ADMIN-BOVEDA",
                    tipo: TipoTransaccion.CargaBoveda,
                    monto: totalLote,
                    fechaHora: DateTime.Now,
                    detalleDesglose: $"Recarga de Bóveda por lote: Q{totalLote:N2}",
                    saldoPosterior: nuevoSaldoTotal
                ));

                return ResultadoOperacion.Ok($"Lote de recarga por Q{totalLote:N2} añadido correctamente. Nuevo saldo en bóveda: Q{nuevoSaldoTotal:N2}.");
            }
            catch (Exception ex)
            {
                return ResultadoOperacion.Fallo($"Error al agregar lote a la bóveda: {ex.Message}");
            }
        }

        public ResultadoOperacion<EstadoBovedaDto> ConsultarEstadoBoveda()
        {
            try
            {
                var boveda = _bovedaRepo.ObtenerBoveda();
                decimal total = boveda.Sum(d => d.Subtotal);

                var listaDto = boveda
                    .OrderByDescending(d => d.Denominacion)
                    .Select(d => new LoteBilletesDto(d.Denominacion, d.Cantidad))
                    .ToList();

                var estado = new EstadoBovedaDto(total, listaDto);
                return ResultadoOperacion<EstadoBovedaDto>.Ok(estado);
            }
            catch (Exception ex)
            {
                return ResultadoOperacion<EstadoBovedaDto>.Fallo($"Error al consultar el estado de la bóveda: {ex.Message}");
            }
        }

        #endregion

        #region 3. Control Diario de Movimientos

        public ResultadoOperacion<ReporteDiarioDto> ObtenerReporteDiario(DateTime? fecha = null)
        {
            try
            {
                DateTime fechaObjetivo = (fecha ?? DateTime.Today).Date;
                var todasTransacciones = _transaccionRepo.ObtenerTodas();

                var delDia = todasTransacciones
                    .Where(t => t.FechaHora.Date == fechaObjetivo)
                    .OrderByDescending(t => t.FechaHora)
                    .ToList();

                decimal totalRetiros = delDia
                    .Where(t => t.Tipo == TipoTransaccion.Retiro)
                    .Sum(t => t.Monto);

                decimal totalDepositos = delDia
                    .Where(t => t.Tipo == TipoTransaccion.Deposito)
                    .Sum(t => t.Monto);

                decimal totalCargasBoveda = delDia
                    .Where(t => t.Tipo == TipoTransaccion.CargaBoveda)
                    .Sum(t => t.Monto);

                var reporte = new ReporteDiarioDto(
                    fecha: fechaObjetivo,
                    totalTransacciones: delDia.Count,
                    totalRetiros: totalRetiros,
                    totalDepositos: totalDepositos,
                    totalCargasBoveda: totalCargasBoveda,
                    movimientos: delDia
                );

                return ResultadoOperacion<ReporteDiarioDto>.Ok(reporte);
            }
            catch (Exception ex)
            {
                return ResultadoOperacion<ReporteDiarioDto>.Fallo($"Error al generar el reporte diario: {ex.Message}");
            }
        }

        #endregion

        #region Helpers Internos

        private static UsuarioDto MapearAUsuarioDto(Usuario u)
        {
            return new UsuarioDto
            {
                NumeroTarjeta = u.NumeroTarjeta,
                NombreCompleto = u.NombreCompleto,
                RfidUID = u.RfidUID,
                Saldo = u.Saldo,
                LimiteDiarioRetiro = u.LimiteDiarioRetiro,
                MontoRetiradoHoy = u.MontoRetiradoHoy,
                UltimaFechaRetiro = u.UltimaFechaRetiro,
                Rol = u.Rol,
                Estado = u.Estado
            };
        }

        #endregion
    }
}
