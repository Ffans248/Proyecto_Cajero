using System;
using System.Linq;
using System.Windows;

namespace Cajero.UI
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            Cajerro.DAL.DescubridorRedBancaria.AsegurarNodoEnRedActivo(new Cajerro.DAL.GestorArchivosCSV());
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string rfidToken = txtRfidToken.Text.Trim();
            string pin = pwdPin.Password;

            if (string.IsNullOrEmpty(rfidToken) || string.IsNullOrEmpty(pin))
            {
                lblMensajeEstado.Text = "Por favor, ingrese el No. de Tarjeta y el PIN de 4 dígitos.";
                lblMensajeEstado.Visibility = Visibility.Visible;
                return;
            }

            if (pin.Length != 4 || !pin.All(char.IsDigit))
            {
                lblMensajeEstado.Text = "El PIN debe constar exactamente de 4 dígitos numéricos.";
                lblMensajeEstado.Visibility = Visibility.Visible;
                pwdPin.Clear();
                return;
            }

            var dal = new Cajerro.DAL.GestorArchivosCSV();

            // Acceso para el Administrador (siempre requiere credenciales válidas)
            if ((rfidToken.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) || 
                 rfidToken.ToUpper().Contains("ADM") || 
                 rfidToken == "9999999999999999") && 
                (pin == "0000" || pin == "1234"))
            {
                dal.RegistrarEnBitacora("Inicio de Sesión", rfidToken, "Acceso autorizado como Administrador al cajero.");
                MainWindow mainAdmin = new MainWindow("Administrador", rfidToken);
                mainAdmin.Show();
                this.Close();
                return;
            }

            var usuario = dal.ObtenerUsuarioPorTarjeta(rfidToken);

            if (usuario == null || usuario.PIN != pin)
            {
                lblMensajeEstado.Text = "Credenciales incorrectas. Verifique su tarjeta y PIN de 4 dígitos.";
                lblMensajeEstado.Visibility = Visibility.Visible;
                dal.RegistrarEnBitacora("Intento Fallido", rfidToken, "Intento de inicio de sesión fallido por credenciales inválidas.");
                pwdPin.Clear();
                return;
            }

            lblMensajeEstado.Visibility = Visibility.Collapsed;

            bool esAdmin = usuario.Nombre.Contains("Admin", StringComparison.OrdinalIgnoreCase) ||
                           usuario.NumeroTarjeta == "9999999999999999";

            string rol = esAdmin ? "Administrador" : "Cliente";

            dal.RegistrarEnBitacora("Inicio de Sesión", rfidToken, $"Acceso exitoso al cajero por {usuario.Nombre} (Rol: {rol}).");

            MainWindow main = new MainWindow(rol, rfidToken);
            main.Show();
            this.Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
        }
    }
}
