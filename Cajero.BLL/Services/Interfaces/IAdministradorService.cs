using Cajero.BLL.DTOs;

namespace Cajero.BLL.Services.Interfaces
{
    /// <summary>
    /// Contrato de operaciones de negocio exclusivas para el Módulo Administrativo del Cajero Automático.
    /// </summary>
    public interface IAdministradorService
    {
        /// <summary>
        /// Registra un nuevo usuario en el sistema validando formato, unicidad y encriptando el PIN con SHA-1.
        /// </summary>
        ResultadoOperacion CrearUsuario(CrearUsuarioDto dto);

        /// <summary>
        /// Define o actualiza el límite diario de retiro permitido para una tarjeta específica.
        /// </summary>
        ResultadoOperacion ModificarLimiteDiario(string numeroTarjeta, decimal nuevoLimite);

        /// <summary>
        /// Inicializa la bóveda con un inventario inicial que no debe exceder Q10,000.00.
        /// </summary>
        ResultadoOperacion InicializarBoveda(List<LoteBilletesDto> lotesIniciales);

        /// <summary>
        /// Agrega un lote de billetes a la bóveda existente, con un tope de hasta Q30,000.00 por lote.
        /// </summary>
        ResultadoOperacion AgregarLoteBoveda(List<LoteBilletesDto> lotesRecarga);

        /// <summary>
        /// Obtiene el arqueo físico y el saldo total de efectivo disponible en el cajero.
        /// </summary>
        ResultadoOperacion<EstadoBovedaDto> ConsultarEstadoBoveda();

        /// <summary>
        /// Retorna la lista de todos los usuarios registrados para gestión administrativa.
        /// </summary>
        ResultadoOperacion<List<UsuarioDto>> ListarUsuarios();

        /// <summary>
        /// Busca y retorna la información administrativa de un usuario a partir de su número de tarjeta.
        /// </summary>
        ResultadoOperacion<UsuarioDto> ConsultarUsuarioPorTarjeta(string numeroTarjeta);

        /// <summary>
        /// Busca a un usuario por su token RFID o por su número de tarjeta, retornando sus datos no sensibles.
        /// </summary>
        ResultadoOperacion<UsuarioDto> BuscarUsuario(string identificador);

        /// <summary>
        /// Genera el reporte y resumen básico de control de los movimientos y transacciones de un día específico (por defecto la fecha actual).
        /// </summary>
        ResultadoOperacion<ReporteDiarioDto> ObtenerReporteDiario(DateTime? fecha = null);
    }
}
