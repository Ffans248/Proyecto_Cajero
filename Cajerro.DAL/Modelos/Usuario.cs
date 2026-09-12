namespace Cajerro.DAL.Modelos
{
    /// <summary>
    /// Representa a un usuario del sistema bancario del cajero automático.
    /// </summary>
    public class Usuario
    {
        /// <summary>
        /// Número de tarjeta de débito/crédito (16 dígitos numéricos).
        /// Actúa como identificador único del usuario.
        /// </summary>
        public string NumeroTarjeta { get; set; } = string.Empty;

        /// <summary>
        /// Código PIN de seguridad (4 dígitos numéricos).
        /// </summary>
        public string PIN { get; set; } = string.Empty;

        /// <summary>
        /// Nombre completo del titular de la cuenta.
        /// </summary>
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Saldo disponible actual en la cuenta del usuario.
        /// </summary>
        public decimal SaldoActual { get; set; }

        /// <summary>
        /// Monto máximo permitido para retiros diarios.
        /// </summary>
        public decimal LimiteDiario { get; set; }

        /// <summary>
        /// Constructor vacío para inicialización por propiedades.
        /// </summary>
        public Usuario()
        {
        }

        /// <summary>
        /// Constructor parametrizado para instanciar un usuario con sus datos completos.
        /// </summary>
        /// <param name="numeroTarjeta">Número de tarjeta (16 posiciones).</param>
        /// <param name="pin">PIN de 4 dígitos.</param>
        /// <param name="nombre">Nombre completo del usuario.</param>
        /// <param name="saldoActual">Saldo actual en cuenta.</param>
        /// <param name="limiteDiario">Monto máximo de retiro diario permitido.</param>
        public Usuario(string numeroTarjeta, string pin, string nombre, decimal saldoActual, decimal limiteDiario)
        {
            NumeroTarjeta = numeroTarjeta;
            PIN = pin;
            Nombre = nombre;
            SaldoActual = saldoActual;
            LimiteDiario = limiteDiario;
        }

        public override string ToString()
        {
            return $"{Nombre} - Tarjeta: {NumeroTarjeta} - Saldo: {SaldoActual:C2}";
        }
    }
}
