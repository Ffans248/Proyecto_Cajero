namespace Cajero.Protocolo
{
    public static class CodigosRespuesta
    {
        public const string Aprobado = "00";
        public const string SaldoInsuficiente = "51";
        public const string ExcedeLimiteDiario = "61";
        public const string PinIncorrecto = "55";
        public const string TokenInvalidoOExpirado = "56";
        public const string TarjetaBloqueadaOInactiva = "75";
        public const string BilletesInsuficientes = "92";
        public const string ExcedeCapacidadCajero = "93";
        public const string CajeroFueraDeServicioONoInicializado = "91";
        public const string ErrorDelSistema = "96";

        // Códigos de Transferencias (internas e interbancarias)
        public const string TransaccionInvalida = "12";
        public const string CuentaInexistente = "14";
        public const string TokenNoEncontrado = "25";
        public const string BancoNoAutorizado = "58";
        public const string ExcedeCupoInterbancario = "65";
        public const string BancoDestinoSinRespuesta = "68";
        public const string TransaccionDuplicada = "94";

        public static string ObtenerDescripcion(string codigo) => codigo switch
        {
            Aprobado => "Operación aprobada",
            SaldoInsuficiente => "Saldo insuficiente",
            ExcedeLimiteDiario => "Excede el límite diario disponible",
            PinIncorrecto => "PIN incorrecto",
            TokenInvalidoOExpirado => "Token inválido o expirado",
            TarjetaBloqueadaOInactiva => "Tarjeta bloqueada (3 intentos fallidos) o inactiva",
            BilletesInsuficientes => "El cajero no tiene los billetes necesarios (se sugiere desglose alternativo)",
            ExcedeCapacidadCajero => "Excede la capacidad de efectivo del cajero (Q 30,000.00)",
            CajeroFueraDeServicioONoInicializado => "Cajero fuera de servicio o no inicializado",
            ErrorDelSistema => "Error del sistema; la operación no se aplicó",
            TransaccionInvalida => "Transacción inválida",
            CuentaInexistente => "La cuenta no existe o está inactiva",
            TokenNoEncontrado => "No se encontró una transferencia con ese token",
            BancoNoAutorizado => "Banco no registrado o no autorizado",
            ExcedeCupoInterbancario => "Excede el cupo diario interbancario de los bancos",
            BancoDestinoSinRespuesta => "El banco destino no respondió o rechazó la conexión",
            TransaccionDuplicada => "Token de transferencia duplicado",
            _ => "Código desconocido"
        };
    }

    public static class TiposMensaje
    {
        public const string RegistrarCajero = "REGISTRAR_CAJERO";
        public const string Heartbeat = "HEARTBEAT";
        public const string Login = "LOGIN";
        public const string ValidarToken = "VALIDAR_TOKEN";
        public const string Retiro = "RETIRO";
        public const string Deposito = "DEPOSITO";
        public const string CambioPin = "CAMBIO_PIN";
        public const string ConsultaSaldo = "CONSULTA_SALDO";
        public const string UltimasTransacciones = "ULTIMAS_TRANSACCIONES";
        public const string Logout = "LOGOUT";
        public const string Respuesta = "RESPUESTA";
        public const string Comando = "COMANDO";

        // Transferencias (Cajero -> Servidor)
        public const string ListarBancos = "LISTAR_BANCOS";
        public const string VerificarCuenta = "VERIFICAR_CUENTA";
        public const string Transferencia = "TRANSFERENCIA";
    }

    /// <summary>
    /// Mensajes del protocolo común Banco ↔ Banco (TCP JSON por línea).
    /// </summary>
    public static class TiposMensajeInterbancario
    {
        public const string Ping = "IB_PING";
        public const string VerificarCuenta = "IB_VERIFICAR_CUENTA";
        public const string Transferir = "IB_TRANSFERIR";
        public const string Consultar = "IB_CONSULTAR";
        public const string Revertir = "IB_REVERTIR";
    }

    public static class EstadosTransferencia
    {
        public const string Pendiente = "PENDIENTE";
        public const string Completada = "COMPLETADA";
        public const string Rechazada = "RECHAZADA";
        public const string Revertida = "REVERTIDA";
        public const string Anulada = "ANULADA";
    }

    public static class TiposComando
    {
        public const string FueraDeServicio = "FUERA_DE_SERVICIO";
        public const string Habilitar = "HABILITAR";
        public const string Mensaje = "MENSAJE";
        public const string ActualizarInventario = "ACTUALIZAR_INVENTARIO";
    }
}
