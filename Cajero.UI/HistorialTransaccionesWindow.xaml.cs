using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Cajero.BLL;
using Cajerro.DAL;

namespace Cajero.UI
{
    public partial class HistorialTransaccionesWindow : Window
    {
        private readonly string _rfidToken;

        public HistorialTransaccionesWindow(string rfidToken)
        {
            InitializeComponent();
            _rfidToken = rfidToken;

            CargarHistorial();
        }

        private void CargarHistorial()
        {
            try
            {
                var dal = new GestorArchivosCSV();
                var usuarioLogica = new UsuarioLogica(dal);

                // Obtener las últimas 5 transacciones del usuario
                var transacciones = usuarioLogica.VerUltimasTransacciones(_rfidToken);

                var items = transacciones.Select(t => new MovimientoItem
                {
                    FechaHora = t.FechaHora.ToString("dd/MM/yyyy HH:mm:ss"),
                    TipoMovimiento = t.TipoMovimiento,
                    MontoFormateado = $"Q {t.Monto:N2}",
                    Token = string.IsNullOrWhiteSpace(t.Token) ? "N/A" : t.Token,
                    Estado = "Completado"
                }).ToList();

                dgHistorial.ItemsSource = items;
                lblTotalMovimientos.Text = $"Últimas {items.Count} transacciones registradas";
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Error al cargar el historial: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class MovimientoItem
    {
        public string FechaHora { get; set; } = string.Empty;
        public string TipoMovimiento { get; set; } = string.Empty;
        public string MontoFormateado { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
    }
}
