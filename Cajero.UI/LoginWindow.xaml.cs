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

            // 1. Instanciamos el Gestor de Archivos (DAL)
            var dal = new Cajerro.DAL.GestorArchivosCSV();

            // 2. Lógica para el Administrador (Quemada por ahora para que puedas entrar)
            if (rfidToken.ToUpper().Contains("ADM") && pin == "0000") // Ponle el PIN que quieras al admin
            {
                MainWindow mainAdmin = new MainWindow("Administrador", rfidToken);
                mainAdmin.Show();
                this.Close();
                return;
            }

            // 3. Lógica para Usuarios Reales
            var usuario = dal.ObtenerUsuarioPorTarjeta(rfidToken);

            // Verificamos si el usuario no existe o si el PIN es incorrecto
            if (usuario == null || usuario.PIN != pin)
            {
                lblMensajeEstado.Text = "Credenciales incorrectas. Verifique su tarjeta y PIN.";
                lblMensajeEstado.Visibility = Visibility.Visible;
                pwdPin.Clear();
                return;
            }

            lblMensajeEstado.Visibility = Visibility.Collapsed;

            // Si todo está correcto, lo dejamos pasar como Cliente
            MainWindow main = new MainWindow("Cliente", rfidToken);
            main.Show();
            this.Close();
        }
    }
}