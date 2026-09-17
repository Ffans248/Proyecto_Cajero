using System;
using System.Windows;
using Cajero.BLL;
using Cajero.Hardware;

namespace Cajero.UI
{
    public partial class LoginWindow : Window
    {
        private LectorRFID _lectorRFID;
        private ServicioAutenticacion _servicioAutenticacion;

        public LoginWindow()
        {
            InitializeComponent();
            _servicioAutenticacion = new ServicioAutenticacion();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Inicializamos el lector en el COM15 a 9600
                _lectorRFID = new LectorRFID("COM15", 9600);
                
                // Suscribimos nuestro método al evento
                _lectorRFID.TarjetaLeida += LectorRFID_TarjetaLeida;
                
                // Arrancamos el hilo secundario para escuchar al ESP32
                _lectorRFID.IniciarEscucha();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.Message, "Error de Hardware", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LectorRFID_TarjetaLeida(string uid)
        {
            // Este evento viene de un hilo secundario (Cross-Thread).
            // Usamos Dispatcher.Invoke para mandarlo al hilo principal de WPF.
            Dispatcher.Invoke(() =>
            {
                txtRfidToken.Text = LectorRFID.ConvertirNfcA16Digitos(uid);
            });
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string rfidToken = txtRfidToken.Text.Trim();
            string pin = pwdPin.Password;

            if (string.IsNullOrEmpty(rfidToken))
            {
                lblMensajeEstado.Text = "Por favor, pase su tarjeta por el lector.";
                lblMensajeEstado.Visibility = Visibility.Visible;
                return;
            }

            // Llamada a la BLL (Separación de capas)
            string rolAsignado = _servicioAutenticacion.ValidarAcceso(rfidToken, pin);

            if (rolAsignado == "DENEGADO")
            {
                lblMensajeEstado.Text = "Credenciales incorrectas o UID no registrado.";
                lblMensajeEstado.Visibility = Visibility.Visible;
                pwdPin.Clear();
                return;
            }

            lblMensajeEstado.Visibility = Visibility.Collapsed;

            // Mostrar el MessageBox con el módulo a abrir (según la prueba física de hoy)
            CustomMessageBox.Show($"Bienvenido. Cuenta: {rfidToken}\nAbriendo módulo de: {rolAsignado}", "Acceso Concedido", MessageBoxButton.OK, MessageBoxImage.Information);

            // Abriendo el Formulario Principal correcto según el Rol
            MainWindow main = new MainWindow(rolAsignado, rfidToken);
            main.Show();
            this.Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_lectorRFID != null)
            {
                _lectorRFID.TarjetaLeida -= LectorRFID_TarjetaLeida;
                _lectorRFID.Dispose();
            }
        }
    }
}




