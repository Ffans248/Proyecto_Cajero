using System;

namespace Cajerro.DAL.Modelos
{
    /// <summary>
    /// Representa un movimiento o transacción monetaria realizada en el cajero automático.
    /// </summary>
    public class Transaccion
    {
        /// <summary>
        /// Número de tarjeta asociada a la transacción.
        /// </summary>
        public string NumeroTarjeta { get; set; } = string.Empty;

        /// <summary>
        /// Fecha y hora exacta en la que se realizó la operación.
        /// </summary>
        public DateTime FechaHora { get; set; }

        /// <summary>
        /// Naturaleza del movimiento (por ejemplo: "Retiro", "Depósito", etc.).
        /// </summary>
        public string TipoMovimiento { get; set; } = string.Empty;

        /// <summary>
        /// Importe monetario involucrado en la transacción.
        /// </summary>
        public decimal Monto { get; set; }

        /// <summary>
        /// Token de seguridad de 4 dígitos generado y utilizado para validar la transacción.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Constructor vacío para inicialización por propiedades.
        /// </summary>
        public Transaccion()
        {
        }

        /// <summary>
        /// Constructor parametrizado para instanciar una transacción con sus datos completos.
        /// </summary>
        /// <param name="numeroTarjeta">Número de tarjeta vinculada.</param>
        /// <param name="fechaHora">Marca temporal de la transacción.</param>
        /// <param name="tipoMovimiento">Tipo de operación (Retiro, Depósito, etc.).</param>
        /// <param name="monto">Monto de la transacción.</param>
        /// <param name="token">Token de autorización de la transacción.</param>
        public Transaccion(string numeroTarjeta, DateTime fechaHora, string tipoMovimiento, decimal monto, string token = "")
        {
            NumeroTarjeta = numeroTarjeta;
            FechaHora = fechaHora;
            TipoMovimiento = tipoMovimiento;
            Monto = monto;
            Token = token;
        }

        public override string ToString()
        {
            return $"[{FechaHora:yyyy-MM-dd HH:mm:ss}] {TipoMovimiento} - {NumeroTarjeta} - Monto: {Monto:C2} (Token: {Token})";
        }
    }
}
