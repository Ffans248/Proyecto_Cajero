using System;
using System.Collections.Generic;

namespace Cajerro.DAL.Modelos
{
    /// <summary>
    /// Representa un banco dentro de la red interbancaria y su límite (cupo) diario coordinado.
    /// </summary>
    public class BancoRedInfo
    {
        public string CodigoBanco { get; set; } = string.Empty;
        public string NombreBanco { get; set; } = string.Empty;
        public string IP { get; set; } = "127.0.0.1";
        public int Puerto { get; set; } = 5000;
        public bool EsPropio { get; set; }
        public bool EnLinea { get; set; }
        public decimal LimiteDiario { get; set; } = 25000.00m;
        public decimal TransferidoHoy { get; set; } = 0.00m;
        public decimal DisponibleHoy => Math.Max(0m, LimiteDiario - TransferidoHoy);
        public DateTime FechaUltimoReinicio { get; set; } = DateTime.Today;

        public string EndpointTexto => $"{IP}:{Puerto}";
        public string RolTexto => EsPropio ? "NUESTRO BANCO (Local)" : (EnLinea ? "🟢 DETECTADO EN RED" : "⚪ Sin conexión LAN (Simulado)");
        public string EstadoRedTexto => EsPropio ? $"🟢 Activo ({IP}:{Puerto})" : (EnLinea ? $"🟢 En Línea ({IP}:{Puerto})" : $"⚪ No detectado en LAN");
        public string LimiteFormateado => $"Q {LimiteDiario:N2}";
        public string TransferidoFormateado => $"Q {TransferidoHoy:N2}";
        public string DisponibleFormateado => $"Q {DisponibleHoy:N2}";
        public string DescripcionCombo =>
            $"{CodigoBanco} - {NombreBanco} [{(EnLinea ? $"🟢 EN RED {IP}" : "⚪ Simulado")}] (Cupo Disp: Q {DisponibleHoy:N2})";
    }

    /// <summary>
    /// Información diagnóstica de la red actual y los bancos detectados en la misma subred.
    /// </summary>
    public class ResultadoEscaneoRed
    {
        public string NombreRedSsid { get; set; } = "Desconocida";
        public string TipoInterfaz { get; set; } = "Wi-Fi / Ethernet";
        public string DescripcionAdaptador { get; set; } = string.Empty;
        public string IpLocal { get; set; } = "127.0.0.1";
        public string SubredCidr { get; set; } = "127.0.0.0/24";
        public string PuertaEnlace { get; set; } = "N/A";
        public string InterfacesSecundarias { get; set; } = string.Empty;
        public List<string> HostsActivosEnLan { get; set; } = new();
        public List<BancoRedInfo> BancosDetectados { get; set; } = new();
        public DateTime FechaEscaneo { get; set; } = DateTime.Now;

        public bool HayOtrosBancosConectados => BancosDetectados.Count > 0;

        public string ResumenRedCorto =>
            $"Red: {NombreRedSsid} ({TipoInterfaz}) | IP Local: {IpLocal} | Subred: {SubredCidr} | Gateway: {PuertaEnlace}";

        public string ResumenDeteccionBancos =>
            HayOtrosBancosConectados
                ? $"🟢 Se detectaron {BancosDetectados.Count} banco(s) conectado(s) en la red '{NombreRedSsid}': {string.Join(", ", BancosDetectados.ConvertAll(b => $"{b.CodigoBanco} ({b.NombreBanco} en {b.IP}:{b.Puerto})"))}"
                : $"🟠 Conectado a la red '{NombreRedSsid}' (IP {IpLocal}), pero NO se detectó ningún otro banco activo en {SubredCidr} (Puertos TCP 5000 / UDP 5005).";
    }

    /// <summary>
    /// Representa una cuenta perteneciente a otro banco de la red para verificación de existencia y acreditación.
    /// </summary>
    public class CuentaExternaInfo
    {
        public string CodigoBanco { get; set; } = string.Empty;
        public string NumeroCuenta { get; set; } = string.Empty;
        public string NombreTitular { get; set; } = string.Empty;
        public decimal SaldoActual { get; set; } = 10000.00m;
        public bool Activa { get; set; } = true;
    }

    /// <summary>
    /// Registro común de transferencia almacenado tanto por nuestro banco como por el banco par con el mismo TokenTransferencia.
    /// </summary>
    public class RegistroTransferenciaBancaria
    {
        public string TokenTransferencia { get; set; } = string.Empty;
        public DateTime FechaHora { get; set; } = DateTime.Now;
        public string TipoTransferencia { get; set; } = string.Empty; // INTERNA | INTERBANCARIA
        public string BancoOrigen { get; set; } = string.Empty;
        public string CuentaOrigen { get; set; } = string.Empty;
        public string NombreOrigen { get; set; } = string.Empty;
        public string BancoDestino { get; set; } = string.Empty;
        public string CuentaDestino { get; set; } = string.Empty;
        public string NombreDestino { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public decimal CupoRestanteBancoOrigen { get; set; }
        public decimal CupoRestanteBancoDestino { get; set; }
        public string GuardadoEn { get; set; } = "NUESTRO_BANCO + OTRO_BANCO + USUARIO";
        public string Estado { get; set; } = "APROBADA";

        public string FechaFormateada => FechaHora.ToString("dd/MM/yyyy HH:mm:ss");
        public string MontoFormateado => $"Q {Monto:N2}";
        public string CupoOrigenFormateado => $"Q {CupoRestanteBancoOrigen:N2}";
        public string CupoDestinoFormateado => $"Q {CupoRestanteBancoDestino:N2}";
        public string OrigenResumen => $"{BancoOrigen} ({CuentaOrigen} - {NombreOrigen})";
        public string DestinoResumen => $"{BancoDestino} ({CuentaDestino} - {NombreDestino})";
    }
}
