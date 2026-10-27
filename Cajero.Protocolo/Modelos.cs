using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cajero.Protocolo
{
    public class MensajeBase
    {
        [JsonPropertyName("tipo")]
        public string Tipo { get; set; } = string.Empty;

        [JsonPropertyName("idSolicitud")]
        public string IdSolicitud { get; set; } = Guid.NewGuid().ToString("N")[..8];
    }

    public class SolicitudRegistroCajero : MensajeBase
    {
        [JsonPropertyName("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [JsonPropertyName("nombreEquipo")]
        public string NombreEquipo { get; set; } = string.Empty;

        [JsonPropertyName("ip")]
        public string IP { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "2.0";

        public SolicitudRegistroCajero()
        {
            Tipo = TiposMensaje.RegistrarCajero;
        }
    }

    public class SolicitudHeartbeat : MensajeBase
    {
        [JsonPropertyName("codigo")]
        public string Codigo { get; set; } = string.Empty;

        public SolicitudHeartbeat()
        {
            Tipo = TiposMensaje.Heartbeat;
        }
    }

    public class SolicitudLogin : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        [JsonPropertyName("pin")]
        public string Pin { get; set; } = string.Empty;

        public SolicitudLogin()
        {
            Tipo = TiposMensaje.Login;
        }
    }

    public class SolicitudValidarToken : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        public SolicitudValidarToken()
        {
            Tipo = TiposMensaje.ValidarToken;
        }
    }

    public class SolicitudRetiro : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        [JsonPropertyName("sesion")]
        public string Sesion { get; set; } = string.Empty;

        [JsonPropertyName("monto")]
        public decimal Monto { get; set; }

        [JsonPropertyName("desglose")]
        public Dictionary<string, int> Desglose { get; set; } = new();

        public SolicitudRetiro()
        {
            Tipo = TiposMensaje.Retiro;
        }
    }

    public class SolicitudDeposito : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        [JsonPropertyName("sesion")]
        public string Sesion { get; set; } = string.Empty;

        [JsonPropertyName("monto")]
        public decimal Monto { get; set; }

        [JsonPropertyName("desglose")]
        public Dictionary<string, int> Desglose { get; set; } = new();

        public SolicitudDeposito()
        {
            Tipo = TiposMensaje.Deposito;
        }
    }

    public class SolicitudCambioPin : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        [JsonPropertyName("pinActual")]
        public string PinActual { get; set; } = string.Empty;

        [JsonPropertyName("pinNuevo")]
        public string PinNuevo { get; set; } = string.Empty;

        public SolicitudCambioPin()
        {
            Tipo = TiposMensaje.CambioPin;
        }
    }

    public class SolicitudConsultaSaldo : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        public SolicitudConsultaSaldo()
        {
            Tipo = TiposMensaje.ConsultaSaldo;
        }
    }

    public class SolicitudUltimasTransacciones : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        [JsonPropertyName("cantidad")]
        public int Cantidad { get; set; } = 5;

        public SolicitudUltimasTransacciones()
        {
            Tipo = TiposMensaje.UltimasTransacciones;
        }
    }

    public class SolicitudLogout : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        public SolicitudLogout()
        {
            Tipo = TiposMensaje.Logout;
        }
    }

    public class MensajeRespuesta : MensajeBase
    {
        [JsonPropertyName("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [JsonPropertyName("mensaje")]
        public string Mensaje { get; set; } = string.Empty;

        [JsonPropertyName("datos")]
        public object? Datos { get; set; }

        public MensajeRespuesta()
        {
            Tipo = TiposMensaje.Respuesta;
        }
    }

    public class MensajeComando : MensajeBase
    {
        [JsonPropertyName("comando")]
        public string Comando { get; set; } = string.Empty;

        [JsonPropertyName("parametros")]
        public Dictionary<string, string> Parametros { get; set; } = new();

        public MensajeComando()
        {
            Tipo = TiposMensaje.Comando;
        }
    }

    // Payloads para el campo "datos" de MensajeRespuesta
    public class DatosLogin
    {
        [JsonPropertyName("idUsuario")]
        public int IdUsuario { get; set; }

        [JsonPropertyName("nombreUsuario")]
        public string NombreUsuario { get; set; } = string.Empty;

        [JsonPropertyName("requiereCambioPin")]
        public bool RequiereCambioPin { get; set; }
    }

    public class DatosValidarToken
    {
        [JsonPropertyName("idUsuario")]
        public int IdUsuario { get; set; }

        [JsonPropertyName("nombreUsuario")]
        public string NombreUsuario { get; set; } = string.Empty;

        [JsonPropertyName("saldo")]
        public decimal Saldo { get; set; }

        [JsonPropertyName("limiteDiario")]
        public decimal LimiteDiario { get; set; }

        [JsonPropertyName("disponibleHoy")]
        public decimal DisponibleHoy { get; set; }

        [JsonPropertyName("tokenSesion")]
        public string TokenSesion { get; set; } = string.Empty;
    }

    public class DatosRetiro
    {
        [JsonPropertyName("idTransaccion")]
        public int IdTransaccion { get; set; }

        [JsonPropertyName("saldo")]
        public decimal Saldo { get; set; }

        [JsonPropertyName("disponibleHoy")]
        public decimal DisponibleHoy { get; set; }

        [JsonPropertyName("fecha")]
        public string Fecha { get; set; } = DateTime.Now.ToString("s");

        [JsonPropertyName("desgloseAlternativo")]
        public Dictionary<string, int>? DesgloseAlternativo { get; set; }
    }

    public class DatosDeposito
    {
        [JsonPropertyName("idTransaccion")]
        public int IdTransaccion { get; set; }

        [JsonPropertyName("saldo")]
        public decimal Saldo { get; set; }

        [JsonPropertyName("fecha")]
        public string Fecha { get; set; } = DateTime.Now.ToString("s");
    }

    public class DatosConsultaSaldo
    {
        [JsonPropertyName("nombreUsuario")]
        public string NombreUsuario { get; set; } = string.Empty;

        [JsonPropertyName("saldo")]
        public decimal Saldo { get; set; }

        [JsonPropertyName("limiteDiario")]
        public decimal LimiteDiario { get; set; }

        [JsonPropertyName("disponibleHoy")]
        public decimal DisponibleHoy { get; set; }

        [JsonPropertyName("fecha")]
        public string Fecha { get; set; } = DateTime.Now.ToString("s");
    }

    public class ItemTransaccion
    {
        [JsonPropertyName("idTransaccion")]
        public int IdTransaccion { get; set; }

        [JsonPropertyName("fecha")]
        public string Fecha { get; set; } = string.Empty;

        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("tipo")]
        public string Tipo { get; set; } = string.Empty;

        [JsonPropertyName("monto")]
        public decimal Monto { get; set; }

        [JsonPropertyName("codigoRespuesta")]
        public string CodigoRespuesta { get; set; } = string.Empty;

        [JsonPropertyName("motivoRechazo")]
        public string? MotivoRechazo { get; set; }
    }

    public class DatosUltimasTransacciones
    {
        [JsonPropertyName("transacciones")]
        public List<ItemTransaccion> Transacciones { get; set; } = new();
    }
}
