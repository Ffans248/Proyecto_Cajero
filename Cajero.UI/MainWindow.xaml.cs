using System;
using System.Windows;

namespace Cajero.UI
{
    public partial class MainWindow : Window
    {
        private readonly string _rolActual;
        private readonly string _rfidToken;

        public MainWindow() : this("Administrador", "RFID-ADM-001")
        {
        }

        public MainWindow(string rol, string rfidToken)
        {
            InitializeComponent();
            _rolActual = rol;
            _rfidToken = rfidToken;

            ConfigurarVistaPorRol();
        }

        private void ConfigurarVistaPorRol()
        {
            lblRolUsuario.Text = _rolActual;
            lblTokenActivo.Text = $"({_rfidToken})";

            if (_rolActual.Equals("Administrador", StringComparison.OrdinalIgnoreCase))
            {
                MostrarVistaAdmin();
                btnNavAdmin.Visibility = Visibility.Visible;
                btnAbrirVentanaAdmin.Visibility = Visibility.Visible;
            }
            else
            {
                MostrarVistaCliente();
                btnNavAdmin.Visibility = Visibility.Collapsed;
                btnAbrirVentanaAdmin.Visibility = Visibility.Collapsed;
            }
        }

        #region Enrutamiento y Navegación

        private void BtnNavAdmin_Click(object sender, RoutedEventArgs e)
        {
            MostrarVistaAdmin();
        }

        private void BtnNavCliente_Click(object sender, RoutedEventArgs e)
        {
            MostrarVistaCliente();
        }

        private void MostrarVistaAdmin()
        {
            viewAdmin.Visibility = Visibility.Visible;
            viewCliente.Visibility = Visibility.Collapsed;

            btnNavAdmin.Tag = "Active";
            btnNavCliente.Tag = "";
        }

        private void MostrarVistaCliente()
        {
            viewCliente.Visibility = Visibility.Visible;
            viewAdmin.Visibility = Visibility.Collapsed;

            btnNavCliente.Tag = "Active";
            btnNavAdmin.Tag = "";
        }

        private void BtnAbrirVentanaAdmin_Click(object sender, RoutedEventArgs e)
        {
            AdminPanel adminWindow = new AdminPanel
            {
                Owner = this
            };
            adminWindow.Show();
        }

        private void BtnOperacionCliente_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string operacion)
            {
                MessageBox.Show($"Operación seleccionada: [{operacion}].\nPunto de enlace UI listo para la capa BLL.",
                                "Cajero Automático", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnCerrarSesion_Click(object sender, RoutedEventArgs e)
        {
            LoginWindow login = new LoginWindow();
            login.Show();
            this.Close();
        }

        #endregion
    }
}