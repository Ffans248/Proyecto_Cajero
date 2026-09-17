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
                if (operacion == "Retiro")
                {
                    RetiroWindow retiroWin = new RetiroWindow(_rfidToken);
                    retiroWin.Owner = this;
                    retiroWin.ShowDialog();
                }
                else if (operacion == "CambioPin")
                {
                    CambioPinWindow pinWin = new CambioPinWindow(_rfidToken);
                    pinWin.Owner = this;
                    pinWin.ShowDialog();
                }
                else if (operacion == "Deposito")
                {
                    // Abrimos la nueva ventana de Depósitos
                    DepositoWindow depositoWin = new DepositoWindow(_rfidToken);
                    depositoWin.Owner = this;
                    depositoWin.ShowDialog();
                }
                else if (operacion == "ConsultaSaldo")
                {
                    try
                    {
                        var dal = new Cajerro.DAL.GestorArchivosCSV();
                        var usuarioLogica = new Cajero.BLL.UsuarioLogica(dal);
                        var (saldo, disponible) = usuarioLogica.VerSaldo(_rfidToken);
                        
                        ConsultaSaldoWindow saldoWin = new ConsultaSaldoWindow(saldo, disponible)
                        {
                            Owner = this
                        };
                        saldoWin.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.Show(ex.Message, "Error al consultar saldo", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    CustomMessageBox.Show($"Operación [{operacion}] en construcción...");
                }
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