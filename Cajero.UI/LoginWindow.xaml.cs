using System.Windows;

namespace Cajero.UI
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string rfidToken = txtRfidToken.Text.Trim();
            string pin = pwdPin.Password;

            if (string.IsNullOrEmpty(rfidToken) || string.IsNullOrEmpty(pin))
            {
                lblMensajeEstado.Text = "Por favor, ingrese el Token RFID y el PIN.";
                lblMensajeEstado.Visibility = Visibility.Visible;
                return;
            }

            pwdPin.Clear();

            lblMensajeEstado.Visibility = Visibility.Collapsed;

            string rolSimulado = rfidToken.ToUpper().Contains("ADM") ? "Administrador" : "Cliente";

            MainWindow main = new MainWindow(rolSimulado, rfidToken);
            main.Show();

            this.Close();
        }
    }
}