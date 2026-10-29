using System;
using System.Collections.Generic;

namespace Cajero.Servidor.Modelos
{
    public class AdministradorSesion
    {
        public int IdAdmin { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Rol { get; set; } = "Administrador"; // 'Administrador' o 'Supervisor'
        public bool EsSupervisor => Rol.Equals("Supervisor", StringComparison.OrdinalIgnoreCase);
    }

    public class ResumenDiarioDto
    {
        public int CajerosEnLinea { get; set; }
        public int TotalCajeros { get; set; }
        public decimal EfectivoTotalRed { get; set; }
        public decimal TotalRetiradoHoy { get; set; }
        public decimal TotalDepositadoHoy { get; set; }
        public decimal PromedioDepositadoPorUsuario { get; set; }
        public int NumeroTransaccionesHoy { get; set; }
        public int CambiosPinHoy { get; set; }
        public int OperacionesRechazadasHoy { get; set; }
        public int AlertasActivas { get; set; }
    }

    public class EstadoCajeroDto
    {
        public int IdCajero { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public string IP { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty; // 'En línea', 'Fuera de servicio', 'Efectivo bajo', 'Desconectado'
        public bool Inicializado { get; set; }
        public DateTime? UltimoHeartbeat { get; set; }
        public decimal EfectivoTotal { get; set; }
        public int Billetes200 { get; set; }
        public int Billetes100 { get; set; }
        public int Billetes50 { get; set; }
        public int Billetes20 { get; set; }
        public int Billetes10 { get; set; }
        public int Billetes5 { get; set; }
        public int Billetes1 { get; set; }
        public DateTime? UltimaTransaccion { get; set; }
    }

    public class TransaccionDto
    {
        public int IdTransaccion { get; set; }
        public DateTime Fecha { get; set; }
        public string Cajero { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string TarjetaEnmascarada { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Desglose { get; set; } = string.Empty;
        public string CodigoRespuesta { get; set; } = string.Empty;
        public string? MotivoRechazo { get; set; }
        public string? TokenTransferencia { get; set; }
        public bool Aprobada => CodigoRespuesta == "00";
    }

    public class UsuarioDto
    {
        public int IdUsuario { get; set; }
        public string NumeroCuenta { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal Saldo { get; set; }
        public decimal LimiteDiario { get; set; }
        public string Estado { get; set; } = "Activo"; // 'Activo' o 'Bloqueado'
        public int IntentosFallidos { get; set; }
        public string NumeroTarjeta { get; set; } = string.Empty;
        public string TarjetaEnmascarada => NumeroTarjeta.Length >= 4 ? $"**** {NumeroTarjeta[^4..]}" : NumeroTarjeta;
        public bool TarjetaActiva { get; set; } = true;
        public decimal RetiradoHoy { get; set; }
        public decimal DisponibleHoy { get; set; }
        public DateTime FechaCreacion { get; set; }
    }

    public class BancoRedDto
    {
        public int IdBanco { get; set; }
        public string CodigoBanco { get; set; } = string.Empty;
        public string NombreBanco { get; set; } = string.Empty;
        public string IP { get; set; } = "127.0.0.1";
        public int Puerto { get; set; } = 5000;
        public bool EsPropio { get; set; }
        public decimal LimiteDiario { get; set; }
        public decimal TransferidoHoy { get; set; }
        public decimal DisponibleHoy => Math.Max(0m, LimiteDiario - TransferidoHoy);
        public DateTime FechaUltimoReset { get; set; }
        public string? CadenaConexionBD { get; set; }
        public bool Activo { get; set; } = true;
    }

    public class TransferenciaBancariaDto
    {
        public int IdTransferencia { get; set; }
        public string TokenTransferencia { get; set; } = string.Empty;
        public string TipoTransferencia { get; set; } = string.Empty; // 'INTERNA' o 'INTERBANCARIA'
        public string Direccion { get; set; } = string.Empty; // 'INTERNA', 'SALIENTE', 'ENTRANTE'
        public string BancoOrigen { get; set; } = string.Empty;
        public string CuentaOrigen { get; set; } = string.Empty;
        public string NombreOrigen { get; set; } = string.Empty;
        public string BancoDestino { get; set; } = string.Empty;
        public string CuentaDestino { get; set; } = string.Empty;
        public string NombreDestino { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Moneda { get; set; } = "GTQ";
        public string Concepto { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string CodigoRespuesta { get; set; } = string.Empty;
        public string? MensajeRespuesta { get; set; }
        public decimal? CupoRestanteBancoOrigen { get; set; }
        public decimal? CupoRestanteBancoDestino { get; set; }
        public string? IdCajero { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaConfirmacion { get; set; }
    }

    public class CuentaBancoExternoDto
    {
        public int IdCuentaExterna { get; set; }
        public string CodigoBanco { get; set; } = string.Empty;
        public string NumeroCuenta { get; set; } = string.Empty;
        public string NombreTitular { get; set; } = string.Empty;
        public decimal SaldoSimulado { get; set; }
        public bool Activa { get; set; } = true;
    }

    public class AlertaDto
    {
        public int IdAlerta { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string? Cajero { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public bool Atendida { get; set; }
        public DateTime? FechaAtendida { get; set; }
        public string? AdminAtendio { get; set; }
    }

    public class BitacoraDto
    {
        public int IdBitacora { get; set; }
        public string Origen { get; set; } = string.Empty;
        public string Accion { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
    }

    public class CambioPinDto
    {
        public int IdCambioPin { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Cajero { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public string Origen { get; set; } = string.Empty;
        public string Resultado { get; set; } = string.Empty;
    }

    public class ParametroDto
    {
        public int IdParametro { get; set; }
        public string Clave { get; set; } = string.Empty;
        public string Valor { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
    }
}
