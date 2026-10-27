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
    }

    public static class TiposComando
    {
        public const string FueraDeServicio = "FUERA_DE_SERVICIO";
        public const string Habilitar = "HABILITAR";
        public const string Mensaje = "MENSAJE";
        public const string ActualizarInventario = "ACTUALIZAR_INVENTARIO";
    }
}
