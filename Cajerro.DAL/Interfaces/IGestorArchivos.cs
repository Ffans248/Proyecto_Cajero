using Cajerro.DAL.Modelos;

namespace Cajerro.DAL.Interfaces
{
    /// <summary>
    /// Contrato para la capa de acceso a datos (DAL) del cajero automático.
    /// Permite desacoplar la lógica de negocio (BLL) de la implementación de persistencia concreta (CSV).
    /// </summary>
    public interface IGestorArchivos
    {
        #region Operaciones de Usuario

        /// <summary>
        /// Obtiene la lista completa de todos los usuarios registrados en el sistema.
        /// </summary>
        /// <returns>Lista de usuarios; si no hay registros o ocurre un error, retorna lista vacía.</returns>
        List<Usuario> ObtenerTodosLosUsuarios();

        /// <summary>
        /// Busca un usuario por su número de tarjeta de 16 dígitos.
        /// </summary>
        /// <param name="tarjeta">Número de tarjeta a buscar.</param>
        /// <returns>El objeto Usuario correspondiente, o null si no existe o ocurre un error.</returns>
        Usuario? ObtenerUsuarioPorTarjeta(string tarjeta);

        /// <summary>
        /// Añade un nuevo usuario al final del archivo de usuarios.
        /// </summary>
        /// <param name="nuevo">Instancia del usuario a registrar.</param>
        /// <returns>True si el registro fue exitoso; de lo contrario, False.</returns>
        bool CrearNuevoUsuario(Usuario nuevo);

        /// <summary>
        /// Modifica el saldo actual de un usuario específico identificado por su número de tarjeta.
        /// </summary>
        /// <param name="tarjeta">Número de tarjeta del usuario.</param>
        /// <param name="nuevoSaldo">Nuevo saldo monetario.</param>
        /// <returns>True si la actualización fue exitosa; de lo contrario, False.</returns>
        bool ActualizarSaldo(string tarjeta, decimal nuevoSaldo);

        /// <summary>
        /// Actualiza todos los datos de un usuario existente (PIN, saldo, límite, etc.).
        /// Método de utilidad extendida para operaciones administrativas o cambio de PIN.
        /// </summary>
        /// <param name="usuarioActualizado">Objeto usuario con los datos actualizados.</param>
        /// <returns>True si se actualizó correctamente; de lo contrario, False.</returns>
        bool ActualizarUsuario(Usuario usuarioActualizado);

        #endregion

        #region Operaciones de Transacciones

        /// <summary>
        /// Añade una nueva transacción al final del archivo en modo Append.
        /// </summary>
        /// <param name="t">Transacción a registrar.</param>
        /// <returns>True si la transacción se registró correctamente; de lo contrario, False.</returns>
        bool RegistrarTransaccion(Transaccion t);

        /// <summary>
        /// Obtiene todas las transacciones asociadas a un número de tarjeta específico.
        /// </summary>
        /// <param name="tarjeta">Número de tarjeta a filtrar.</param>
        /// <returns>Lista de transacciones asociadas a la tarjeta; lista vacía si no hay registros o error.</returns>
        List<Transaccion> ObtenerHistorial(string tarjeta);

        /// <summary>
        /// Obtiene la totalidad de transacciones registradas en el cajero (útil para auditoría y control administrativo).
        /// </summary>
        /// <returns>Lista con todas las transacciones registradas.</returns>
        List<Transaccion> ObtenerTodasLasTransacciones();

        #endregion

        #region Operaciones de Bóveda

        /// <summary>
        /// Carga el inventario actual de billetes (denominaciones de 200, 100, 50, 20, 10, 5 y 1).
        /// </summary>
        /// <returns>Lista con los billetes disponibles por denominación.</returns>
        List<Billete> ObtenerInventarioBilletes();

        /// <summary>
        /// Sobrescribe el inventario completo de la bóveda con los nuevos valores de billetes.
        /// </summary>
        /// <param name="inventarioActualizado">Lista con las cantidades actualizadas por denominación.</param>
        /// <returns>True si se guardó correctamente; de lo contrario, False.</returns>
        bool GuardarInventarioBilletes(List<Billete> inventarioActualizado);

        #endregion
    }
}
