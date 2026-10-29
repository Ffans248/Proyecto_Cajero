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

            Cajerro.DAL.DescubridorRedBancaria.AsegurarNodoEnRedActivo(new Cajerro.DAL.GestorArchivosCSV());
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
            }
            else
            {
                MostrarVistaCliente();
                btnNavAdmin.Visibility = Visibility.Collapsed;
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

        private void BtnNavTransferencias_Click(object sender, RoutedEventArgs e)
        {
            var trfWin = new TransferenciaWindow(_rfidToken, false)
            {
                Owner = this
            };
            trfWin.ShowDialog();
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
                    DepositoWindow depositoWin = new DepositoWindow(_rfidToken);
                    depositoWin.Owner = this;
                    depositoWin.ShowDialog();
                }
                else if (operacion == "TransferenciaInterna")
                {
                    TransferenciaWindow trfWin = new TransferenciaWindow(_rfidToken, false)
                    {
                        Owner = this
                    };
                    trfWin.ShowDialog();
                }
                else if (operacion == "TransferenciaInterbancaria")
                {
                    TransferenciaWindow trfWin = new TransferenciaWindow(_rfidToken, true)
                    {
                        Owner = this
                    };
                    trfWin.ShowDialog();
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
                else if (operacion == "UltimasTransacciones")
                {
                    HistorialTransaccionesWindow histWin = new HistorialTransaccionesWindow(_rfidToken)
                    {
                        Owner = this
                    };
                    histWin.ShowDialog();
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
