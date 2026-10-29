using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cajero.BLL;
using Cajerro.DAL;
using Cajerro.DAL.Modelos;

namespace Cajero.UI
{
    public partial class TransferenciaWindow : Window
    {
        private readonly string _numeroTarjetaOrigen;
        private readonly GestorArchivosCSV _dal;
        private readonly UsuarioLogica _logica;

        private bool _esInterbancaria;
        private bool _cuentaVerificada;
        private string _tokenActual = string.Empty;
        private List<BancoRedInfo> _bancosExternos = new();

        public TransferenciaWindow(string numeroTarjetaOrigen, bool modoInterbancarioInicial = false)
        {
            InitializeComponent();

            _numeroTarjetaOrigen = numeroTarjetaOrigen;
            _dal = new GestorArchivosCSV();
            _logica = new UsuarioLogica(_dal);
            _esInterbancaria = modoInterbancarioInicial;

            RegenerarToken();
            CargarDatosIniciales();
            AplicarModo(_esInterbancaria);
            _ = EscanearRedAutomaticamenteAsync(mostrarDialogo: false);
        }

        private async void BtnEscanearRed_Click(object sender, RoutedEventArgs e)
        {
            await EscanearRedAutomaticamenteAsync(mostrarDialogo: true);
        }

        private async Task EscanearRedAutomaticamenteAsync(bool mostrarDialogo)
        {
            try
            {
                btnEscanearRed.IsEnabled = false;
                btnEscanearRed.Content = "⏳ Escaneando...";

                var infoInstantanea = DescubridorRedBancaria.ObtenerInformacionRedActual();
                lblInfoRedActual.Text = $"📡 {infoInstantanea.ResumenRedCorto}";
                lblEstadoDeteccionBancos.Text = "Escaneando dispositivos y puertos bancarios (UDP 5005 / TCP 5000) en la subred...";

                var res = await DescubridorRedBancaria.EscanearRedYDetectarBancosAsync(_dal);

                lblInfoRedActual.Text = $"📡 {res.ResumenRedCorto}  |  VPN/Otras: {res.InterfacesSecundarias}";
                lblEstadoDeteccionBancos.Text = res.ResumenDeteccionBancos;
                lblEstadoDeteccionBancos.Foreground = res.HayOtrosBancosConectados
                    ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                    : new SolidColorBrush(Color.FromRgb(251, 191, 36));

                // Refrescar combo de bancos con el estado actualizado
                int selIdx = cbBancoDestino.SelectedIndex >= 0 ? cbBancoDestino.SelectedIndex : 0;
                CargarDatosIniciales();
                if (selIdx < _bancosExternos.Count)
                    cbBancoDestino.SelectedIndex = selIdx;

                if (mostrarDialogo)
                {
                    string hosts = res.HostsActivosEnLan.Count > 0
                        ? string.Join(", ", res.HostsActivosEnLan)
                        : "Solo este equipo";

                    string listaBancos = res.HayOtrosBancosConectados
                        ? string.Join("\n", res.BancosDetectados.Select(b => $"  • {b.CodigoBanco} - {b.NombreBanco} ({b.IP}:{b.Puerto}) - Cupo Disp: Q {b.DisponibleHoy:N2}"))
                        : "  • Ningún otro servidor bancario respondió en los puertos TCP 5000/5001 ni UDP 5005.";

                    string detalle =
                        $"DIAGNÓSTICO DE RED Y DETECCIÓN DE BANCOS\n" +
                        $"══════════════════════════════════════════════════\n" +
                        $"• Red Conectada (SSID): {res.NombreRedSsid} ({res.TipoInterfaz})\n" +
                        $"• Adaptador: {res.DescripcionAdaptador}\n" +
                        $"• Tu Dirección IPv4 Local: {res.IpLocal}\n" +
                        $"• Subred Escaneada: {res.SubredCidr}\n" +
                        $"• Puerta de Enlace (Gateway): {res.PuertaEnlace}\n" +
                        $"• Otras Interfaces Activas: {res.InterfacesSecundarias}\n" +
                        $"• Equipos Vistos en la Red (ARP/LAN): {hosts}\n" +
                        $"──────────────────────────────────────────────────\n" +
                        $"BANCOS EXTERNOS DETECTADOS EN LA MISMA RED ({res.BancosDetectados.Count}):\n" +
                        $"{listaBancos}";

                    CustomMessageBox.Show(detalle, "Escaneo de Red Interbancaria", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                lblEstadoDeteccionBancos.Text = "Error al escanear red: " + ex.Message;
            }
            finally
            {
                btnEscanearRed.Content = "📡 Escanear Red Local";
                btnEscanearRed.IsEnabled = true;
            }
        }

        private void RegenerarToken()
        {
            _tokenActual = UsuarioLogica.GenerarTokenTransferencia();
            lblTokenTransferencia.Text = _tokenActual;
        }

        private void BtnRegenerarToken_Click(object sender, RoutedEventArgs e)
        {
            RegenerarToken();
        }

        private void CargarDatosIniciales()
        {
            try
            {
                var u = _dal.ObtenerUsuarioPorTarjeta(_numeroTarjetaOrigen);
                if (u != null)
                {
                    lblSaldoUsuario.Text = $"Q {u.SaldoActual:N2}";
                }

                var bancos = _dal.ObtenerBancosRed();
                var propio = bancos.FirstOrDefault(b => b.EsPropio);
                if (propio != null)
                {
                    lblCupoNuestroBanco.Text = $"Q {propio.DisponibleHoy:N2}";
                }

                _bancosExternos = bancos.Where(b => !b.EsPropio).ToList();
                cbBancoDestino.ItemsSource = null;
                cbBancoDestino.ItemsSource = _bancosExternos;
                cbBancoDestino.DisplayMemberPath = "DescripcionCombo";
                cbBancoDestino.SelectedValuePath = "CodigoBanco";
                if (_bancosExternos.Count > 0 && cbBancoDestino.SelectedIndex < 0)
                {
                    cbBancoDestino.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error al cargar datos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AplicarModo(bool interbancaria)
        {
            _esInterbancaria = interbancaria;
            _cuentaVerificada = false;
            btnConfirmarTransferencia.IsEnabled = false;

            if (!_esInterbancaria)
            {
                lblTituloVentana.Text = "Transferencia entre Usuarios (Mismo Banco)";
                lblSubtituloVentana.Text = "Transfiera fondos a otra cuenta o tarjeta de nuestro banco generando token de registro.";
                btnModoInterna.Style = (Style)FindResource("PrimaryButton");
                btnModoInterbancaria.Style = (Style)FindResource("OutlineButton");

                pnlSelectorBanco.Visibility = Visibility.Collapsed;
                lblPasoCuenta.Text = "1. Ingrese Número de Cuenta o Tarjeta del Usuario en Nuestro Banco:";
                lblTituloCupoDestino.Text = "MODALIDAD:";
                lblCupoBancoDestino.Text = "Mismo Banco (Interna)";
                lblNotaCoordinacion.Text = "El token generado se guarda en el registro de nuestro banco y en el historial de ambos usuarios.";
            }
            else
            {
                lblTituloVentana.Text = "Transferencia Interbancaria (Diferentes Bancos)";
                lblSubtituloVentana.Text = "Seleccione el banco destino, verifique si existe la cuenta y coordine el límite de ambos bancos.";
                btnModoInterbancaria.Style = (Style)FindResource("PrimaryButton");
                btnModoInterna.Style = (Style)FindResource("OutlineButton");

                pnlSelectorBanco.Visibility = Visibility.Visible;
                lblPasoCuenta.Text = "2. Ingrese Número de Cuenta del Banco Seleccionado y Verifique si Existe:";
                lblTituloCupoDestino.Text = "CUPO HOY BANCO DESTINO:";
                ActualizarCupoBancoSeleccionado();
                lblNotaCoordinacion.Text = "El monto se resta coordinadamente al límite diario de nuestro banco y del banco destino, guardando el token en los tres.";
            }

            ConstruirBotonesCuentasRapidas();
            ReiniciarEstadoVerificacion();
        }

        private void ConstruirBotonesCuentasRapidas()
        {
            pnlCuentasRapidas.Children.Clear();

            if (!_esInterbancaria)
            {
                var usuarios = _dal.ObtenerTodosLosUsuarios()
                    .Where(u => !u.NumeroTarjeta.Equals(_numeroTarjetaOrigen, StringComparison.OrdinalIgnoreCase))
                    .Take(4)
                    .ToList();

                foreach (var u in usuarios)
                {
                    var btn = new Button
                    {
                        Content = $"{u.Nombre} ({u.NumeroTarjeta})",
                        Style = (Style)FindResource("OutlineButton"),
                        Height = 26,
                        Padding = new Thickness(8, 0, 8, 0),
                        Margin = new Thickness(0, 0, 6, 4),
                        FontSize = 11
                    };
                    string tarj = u.NumeroTarjeta;
                    btn.Click += (s, e) =>
                    {
                        txtCuentaDestino.Text = tarj;
                        EjecutarVerificacionCuenta();
                    };
                    pnlCuentasRapidas.Children.Add(btn);
                }
            }
            else
            {
                string codBanco = (cbBancoDestino.SelectedItem as BancoRedInfo)?.CodigoBanco ?? "BANC02";
                var cuentas = _dal.ObtenerCuentasExternas(codBanco);

                foreach (var c in cuentas)
                {
                    var btn = new Button
                    {
                        Content = $"{c.NumeroCuenta} - {c.NombreTitular}",
                        Style = (Style)FindResource("OutlineButton"),
                        Height = 26,
                        Padding = new Thickness(8, 0, 8, 0),
                        Margin = new Thickness(0, 0, 6, 4),
                        FontSize = 11
                    };
                    string numCta = c.NumeroCuenta;
                    btn.Click += (s, e) =>
                    {
                        txtCuentaDestino.Text = numCta;
                        EjecutarVerificacionCuenta();
                    };
                    pnlCuentasRapidas.Children.Add(btn);
                }
            }
        }

        private void ActualizarCupoBancoSeleccionado()
        {
            if (cbBancoDestino.SelectedItem is BancoRedInfo b)
            {
                lblCupoBancoDestino.Text = $"Q {b.DisponibleHoy:N2} ({b.CodigoBanco})";
            }
        }

        private void ReiniciarEstadoVerificacion()
        {
            _cuentaVerificada = false;
            btnConfirmarTransferencia.IsEnabled = false;
            lblEstadoVerificacion.Text = "⚠️ Presione 'Verificar si Existe Usuario' para validar el titular antes de realizar la transferencia.";
            lblEstadoVerificacion.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        }

        private void BtnModoInterna_Click(object sender, RoutedEventArgs e)
        {
            AplicarModo(false);
        }

        private void BtnModoInterbancaria_Click(object sender, RoutedEventArgs e)
        {
            AplicarModo(true);
        }

        private void CbBancoDestino_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_esInterbancaria)
            {
                ActualizarCupoBancoSeleccionado();
                ConstruirBotonesCuentasRapidas();
                ReiniciarEstadoVerificacion();
            }
        }

        private void TxtCuentaDestino_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReiniciarEstadoVerificacion();
        }

        private void BtnVerificarCuenta_Click(object sender, RoutedEventArgs e)
        {
            EjecutarVerificacionCuenta();
        }

        private void EjecutarVerificacionCuenta()
        {
            string codBanco = _esInterbancaria
                ? ((cbBancoDestino.SelectedItem as BancoRedInfo)?.CodigoBanco ?? "BANC02")
                : "BANC01";

            var res = _logica.VerificarCuentaDestino(_esInterbancaria, codBanco, txtCuentaDestino.Text, _numeroTarjetaOrigen);
            if (res.Existe)
            {
                _cuentaVerificada = true;
                btnConfirmarTransferencia.IsEnabled = true;
                lblCupoNuestroBanco.Text = $"Q {res.CupoBancoOrigen:N2}";
                if (_esInterbancaria)
                {
                    lblCupoBancoDestino.Text = $"Q {res.CupoBancoDestino:N2} ({res.CodigoBanco})";
                }

                lblEstadoVerificacion.Text =
                    $"✅ USUARIO VERIFICADO EN {res.NombreBanco.ToUpperInvariant()} ({res.CodigoBanco})\n" +
                    $"Titular: {res.NombreTitular}  |  Cuenta / Tarjeta: {res.CuentaNormalizada}\n" +
                    $"Cupo Disp. Nuestro Banco: Q {res.CupoBancoOrigen:N2}  |  Cupo Disp. Banco Destino: Q {res.CupoBancoDestino:N2}";
                lblEstadoVerificacion.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));
            }
            else
            {
                _cuentaVerificada = false;
                btnConfirmarTransferencia.IsEnabled = false;
                lblEstadoVerificacion.Text = $"❌ {res.Mensaje}";
                lblEstadoVerificacion.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }
        }

        private void BtnConfirmarTransferencia_Click(object sender, RoutedEventArgs e)
        {
            if (!_cuentaVerificada)
            {
                CustomMessageBox.Show("Primero debe verificar si existe la cuenta del usuario en el banco seleccionado.", "Verificación Requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtMonto.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto) || monto <= 0)
            {
                if (!decimal.TryParse(txtMonto.Text.Trim(), out monto) || monto <= 0)
                {
                    CustomMessageBox.Show("Ingrese un monto válido mayor a Q 0.00.", "Monto Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string codBanco = _esInterbancaria
                ? ((cbBancoDestino.SelectedItem as BancoRedInfo)?.CodigoBanco ?? "BANC02")
                : "BANC01";

            try
            {
                var reg = _logica.RealizarTransferencia(
                    _numeroTarjetaOrigen,
                    pwdPin.Password.Trim(),
                    _esInterbancaria,
                    codBanco,
                    txtCuentaDestino.Text.Trim(),
                    monto,
                    txtConcepto.Text.Trim(),
                    _tokenActual);

                CargarDatosIniciales();

                string resumen =
                    $"¡TRANSFERENCIA {reg.TipoTransferencia} APROBADA!\n" +
                    $"══════════════════════════════════════════\n" +
                    $"🔑 TOKEN DE REGISTRO: {reg.TokenTransferencia}\n" +
                    $"   ✔ Guardado en Nuestro Banco ({reg.BancoOrigen})\n" +
                    $"   ✔ Guardado en Banco Destino ({reg.BancoDestino})\n" +
                    $"   ✔ Guardado en Historial del Usuario\n" +
                    $"──────────────────────────────────────────\n" +
                    $"• Origen: {reg.NombreOrigen} ({reg.BancoOrigen} - {reg.CuentaOrigen})\n" +
                    $"• Destino: {reg.NombreDestino} ({reg.BancoDestino} - {reg.CuentaDestino})\n" +
                    $"• Monto Transferido: Q {reg.Monto:N2}\n" +
                    $"• Concepto: {reg.Concepto}\n" +
                    $"──────────────────────────────────────────\n" +
                    $"• Cupo Diario Restante {reg.BancoOrigen}: Q {reg.CupoRestanteBancoOrigen:N2}\n" +
                    $"• Cupo Diario Restante {reg.BancoDestino}: Q {reg.CupoRestanteBancoDestino:N2}";

                CustomMessageBox.Show(resumen, "Comprobante de Transferencia y Token", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.Message, "No se pudo completar la transferencia", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
