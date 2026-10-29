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

        [JsonPropertyName("tokenGenerado")]
        public string TokenGenerado { get; set; } = string.Empty;
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

        [JsonPropertyName("numeroCuenta")]
        public string NumeroCuenta { get; set; } = string.Empty;

        [JsonPropertyName("codigoBanco")]
        public string CodigoBanco { get; set; } = string.Empty;

        [JsonPropertyName("nombreBanco")]
        public string NombreBanco { get; set; } = string.Empty;

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

        [JsonPropertyName("numeroCuenta")]
        public string NumeroCuenta { get; set; } = string.Empty;

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

        [JsonPropertyName("tokenTransferencia")]
        public string? TokenTransferencia { get; set; }
    }

    public class DatosUltimasTransacciones
    {
        [JsonPropertyName("transacciones")]
        public List<ItemTransaccion> Transacciones { get; set; } = new();
    }

    // =========================================================================
    // TRANSFERENCIAS (CAJERO <-> SERVIDOR)
    // =========================================================================

    public class SolicitudListarBancos : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        public SolicitudListarBancos()
        {
            Tipo = TiposMensaje.ListarBancos;
        }
    }

    public class ItemBancoProtocolo
    {
        [JsonPropertyName("codigoBanco")]
        public string CodigoBanco { get; set; } = string.Empty;

        [JsonPropertyName("nombreBanco")]
        public string NombreBanco { get; set; } = string.Empty;

        [JsonPropertyName("esPropio")]
        public bool EsPropio { get; set; }

        [JsonPropertyName("limiteDiario")]
        public decimal LimiteDiario { get; set; }

        [JsonPropertyName("disponibleHoy")]
        public decimal DisponibleHoy { get; set; }

        [JsonPropertyName("activo")]
        public bool Activo { get; set; } = true;
    }

    public class DatosListarBancos
    {
        [JsonPropertyName("bancos")]
        public List<ItemBancoProtocolo> Bancos { get; set; } = new();

        [JsonPropertyName("codigoBancoPropio")]
        public string CodigoBancoPropio { get; set; } = string.Empty;

        [JsonPropertyName("nombreBancoPropio")]
        public string NombreBancoPropio { get; set; } = string.Empty;
    }

    public class SolicitudVerificarCuenta : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        [JsonPropertyName("bancoDestino")]
        public string BancoDestino { get; set; } = string.Empty;

        [JsonPropertyName("cuentaDestino")]
        public string CuentaDestino { get; set; } = string.Empty;

        public SolicitudVerificarCuenta()
        {
            Tipo = TiposMensaje.VerificarCuenta;
        }
    }

    public class DatosVerificarCuenta
    {
        [JsonPropertyName("existe")]
        public bool Existe { get; set; }

        [JsonPropertyName("cuentaDestino")]
        public string CuentaDestino { get; set; } = string.Empty;

        [JsonPropertyName("nombreTitular")]
        public string NombreTitular { get; set; } = string.Empty;

        [JsonPropertyName("bancoDestino")]
        public string BancoDestino { get; set; } = string.Empty;

        [JsonPropertyName("nombreBancoDestino")]
        public string NombreBancoDestino { get; set; } = string.Empty;

        [JsonPropertyName("cupoDisponibleOrigen")]
        public decimal CupoDisponibleOrigen { get; set; }

        [JsonPropertyName("cupoDisponibleDestino")]
        public decimal CupoDisponibleDestino { get; set; }
    }

    public class SolicitudTransferencia : MensajeBase
    {
        [JsonPropertyName("cajero")]
        public string Cajero { get; set; } = string.Empty;

        [JsonPropertyName("numeroTarjeta")]
        public string NumeroTarjeta { get; set; } = string.Empty;

        [JsonPropertyName("sesion")]
        public string Sesion { get; set; } = string.Empty;

        [JsonPropertyName("esInterbancaria")]
        public bool EsInterbancaria { get; set; }

        [JsonPropertyName("bancoDestino")]
        public string BancoDestino { get; set; } = string.Empty;

        [JsonPropertyName("cuentaDestino")]
        public string CuentaDestino { get; set; } = string.Empty;

        [JsonPropertyName("monto")]
        public decimal Monto { get; set; }

        [JsonPropertyName("concepto")]
        public string Concepto { get; set; } = string.Empty;

        public SolicitudTransferencia()
        {
            Tipo = TiposMensaje.Transferencia;
        }
    }

    public class DatosTransferencia
    {
        [JsonPropertyName("idTransaccion")]
        public int IdTransaccion { get; set; }

        [JsonPropertyName("tokenTransferencia")]
        public string TokenTransferencia { get; set; } = string.Empty;

        [JsonPropertyName("esInterbancaria")]
        public bool EsInterbancaria { get; set; }

        [JsonPropertyName("bancoOrigen")]
        public string BancoOrigen { get; set; } = string.Empty;

        [JsonPropertyName("nombreBancoOrigen")]
        public string NombreBancoOrigen { get; set; } = string.Empty;

        [JsonPropertyName("cuentaOrigen")]
        public string CuentaOrigen { get; set; } = string.Empty;

        [JsonPropertyName("nombreOrigen")]
        public string NombreOrigen { get; set; } = string.Empty;

        [JsonPropertyName("bancoDestino")]
        public string BancoDestino { get; set; } = string.Empty;

        [JsonPropertyName("nombreBancoDestino")]
        public string NombreBancoDestino { get; set; } = string.Empty;

        [JsonPropertyName("cuentaDestino")]
        public string CuentaDestino { get; set; } = string.Empty;

        [JsonPropertyName("nombreDestino")]
        public string NombreDestino { get; set; } = string.Empty;

        [JsonPropertyName("monto")]
        public decimal Monto { get; set; }

        [JsonPropertyName("concepto")]
        public string Concepto { get; set; } = string.Empty;

        [JsonPropertyName("saldo")]
        public decimal Saldo { get; set; }

        [JsonPropertyName("cupoRestanteBancoOrigen")]
        public decimal CupoRestanteBancoOrigen { get; set; }

        [JsonPropertyName("cupoRestanteBancoDestino")]
        public decimal CupoRestanteBancoDestino { get; set; }

        [JsonPropertyName("fecha")]
        public string Fecha { get; set; } = DateTime.Now.ToString("s");
    }

    // =========================================================================
    // PROTOCOLO INTERBANCARIO ESTÁNDAR (BANCO <-> BANCO)
    // =========================================================================

    public class SolicitudIbPing : MensajeBase
    {
        [JsonPropertyName("bancoOrigen")]
        public string BancoOrigen { get; set; } = string.Empty;

        public SolicitudIbPing()
        {
            Tipo = TiposMensajeInterbancario.Ping;
        }
    }

    public class SolicitudIbVerificarCuenta : MensajeBase
    {
        [JsonPropertyName("bancoOrigen")]
        public string BancoOrigen { get; set; } = string.Empty;

        [JsonPropertyName("bancoDestino")]
        public string BancoDestino { get; set; } = string.Empty;

        [JsonPropertyName("numeroCuenta")]
        public string NumeroCuenta { get; set; } = string.Empty;

        public SolicitudIbVerificarCuenta()
        {
            Tipo = TiposMensajeInterbancario.VerificarCuenta;
        }
    }

    public class DatosIbVerificarCuenta
    {
        [JsonPropertyName("existe")]
        public bool Existe { get; set; }

        [JsonPropertyName("numeroCuenta")]
        public string NumeroCuenta { get; set; } = string.Empty;

        [JsonPropertyName("nombreTitular")]
        public string NombreTitular { get; set; } = string.Empty;

        [JsonPropertyName("banco")]
        public string Banco { get; set; } = string.Empty;

        [JsonPropertyName("nombreBanco")]
        public string NombreBanco { get; set; } = string.Empty;

        [JsonPropertyName("moneda")]
        public string Moneda { get; set; } = "GTQ";

        [JsonPropertyName("cupoDisponibleBanco")]
        public decimal CupoDisponibleBanco { get; set; }
    }

    public class SolicitudIbTransferir : MensajeBase
    {
        [JsonPropertyName("tokenTransferencia")]
        public string TokenTransferencia { get; set; } = string.Empty;

        [JsonPropertyName("bancoOrigen")]
        public string BancoOrigen { get; set; } = string.Empty;

        [JsonPropertyName("cuentaOrigen")]
        public string CuentaOrigen { get; set; } = string.Empty;

        [JsonPropertyName("nombreOrigen")]
        public string NombreOrigen { get; set; } = string.Empty;

        [JsonPropertyName("bancoDestino")]
        public string BancoDestino { get; set; } = string.Empty;

        [JsonPropertyName("cuentaDestino")]
        public string CuentaDestino { get; set; } = string.Empty;

        [JsonPropertyName("nombreDestino")]
        public string NombreDestino { get; set; } = string.Empty;

        [JsonPropertyName("monto")]
        public decimal Monto { get; set; }

        [JsonPropertyName("moneda")]
        public string Moneda { get; set; } = "GTQ";

        [JsonPropertyName("concepto")]
        public string Concepto { get; set; } = string.Empty;

        [JsonPropertyName("fechaEnvio")]
        public string FechaEnvio { get; set; } = DateTime.Now.ToString("s");

        public SolicitudIbTransferir()
        {
            Tipo = TiposMensajeInterbancario.Transferir;
        }
    }

    public class DatosIbTransferir
    {
        [JsonPropertyName("tokenTransferencia")]
        public string TokenTransferencia { get; set; } = string.Empty;

        [JsonPropertyName("estado")]
        public string Estado { get; set; } = EstadosTransferencia.Completada;

        [JsonPropertyName("nombreDestino")]
        public string NombreDestino { get; set; } = string.Empty;

        [JsonPropertyName("cupoRestanteBancoDestino")]
        public decimal CupoRestanteBancoDestino { get; set; }

        [JsonPropertyName("fechaConfirmacion")]
        public string FechaConfirmacion { get; set; } = DateTime.Now.ToString("s");
    }

    public class SolicitudIbConsultar : MensajeBase
    {
        [JsonPropertyName("tokenTransferencia")]
        public string TokenTransferencia { get; set; } = string.Empty;

        [JsonPropertyName("bancoOrigen")]
        public string BancoOrigen { get; set; } = string.Empty;

        public SolicitudIbConsultar()
        {
            Tipo = TiposMensajeInterbancario.Consultar;
        }
    }

    public class SolicitudIbRevertir : MensajeBase
    {
        [JsonPropertyName("tokenTransferencia")]
        public string TokenTransferencia { get; set; } = string.Empty;

        [JsonPropertyName("bancoOrigen")]
        public string BancoOrigen { get; set; } = string.Empty;

        [JsonPropertyName("motivo")]
        public string Motivo { get; set; } = string.Empty;

        public SolicitudIbRevertir()
        {
            Tipo = TiposMensajeInterbancario.Revertir;
        }
    }
}
