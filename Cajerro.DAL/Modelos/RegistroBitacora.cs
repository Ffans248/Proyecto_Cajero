using System;

namespace Cajerro.DAL.Modelos
{
    /// <summary>
    /// Representa un registro en la bitácora administrativa de eventos relevantes del cajero:
    /// cambios de PIN, modificación de tarjetas o límites, inicialización y recarga de bóveda, accesos, etc.
    /// </summary>
    public class RegistroBitacora
    {
        public DateTime FechaHora { get; set; }
        public string TipoEvento { get; set; } = string.Empty;
        public string UsuarioOTarjeta { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;

        public RegistroBitacora()
        {
        }

        public RegistroBitacora(DateTime fechaHora, string tipoEvento, string usuarioOTarjeta, string detalle)
        {
            FechaHora = fechaHora;
            TipoEvento = tipoEvento;
            UsuarioOTarjeta = usuarioOTarjeta;
            Detalle = detalle;
        }

        public override string ToString()
        {
            return $"[{FechaHora:yyyy-MM-dd HH:mm:ss}] {TipoEvento} | {UsuarioOTarjeta} | {Detalle}";
        }
    }
}
