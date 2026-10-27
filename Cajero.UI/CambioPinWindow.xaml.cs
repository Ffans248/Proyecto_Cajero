using System;
using System.Linq;
using System.Windows;
using Cajero.BLL;
using Cajerro.DAL;

namespace Cajero.UI
{
    public partial class CambioPinWindow : Window
    {
        private readonly string _rfidToken;
        private readonly UsuarioLogica _usuarioLogica;
        private readonly string _tokenGenerado;

        public CambioPinWindow(string rfidToken)
        {
            InitializeComponent();
            _rfidToken = rfidToken;

            var dal = new GestorArchivosCSV();
            _usuarioLogica = new UsuarioLogica(dal);

            _tokenGenerado = UsuarioLogica.GenerarToken4Digitos();
            lblTokenGenerado.Text = _tokenGenerado;
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            string pinActual = txtPinActual.Password.Trim();
            string tokenIngresado = txtToken.Text.Trim();
            string pinNuevo = txtPinNuevo.Password.Trim();
            string pinConfirmar = txtPinConfirmar.Password.Trim();

            // 1. Validar PIN actual
            if (string.IsNullOrEmpty(pinActual) || pinActual.Length != 4 || !pinActual.All(char.IsDigit))
            {
                CustomMessageBox.Show("El PIN actual debe contener exactamente 4 dígitos numéricos.", 
                    "Error de Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPinActual.Focus();
                return;
            }

            // 2. Validar Token de autorización
            if (string.IsNullOrEmpty(tokenIngresado) || tokenIngresado.Length != 4 || !tokenIngresado.All(char.IsDigit))
            {
                CustomMessageBox.Show("El Token de autorización debe contener exactamente 4 dígitos numéricos.", 
                    "Error de Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtToken.Focus();
                return;
            }

            if (!tokenIngresado.Equals(_tokenGenerado))
            {
                CustomMessageBox.Show("El Token ingresado no coincide con el Token generado para esta operación.", 
                    "Token Inválido", MessageBoxButton.OK, MessageBoxImage.Error);
                txtToken.Focus();
                return;
            }

            // 3. Validar Nuevo PIN
            if (string.IsNullOrEmpty(pinNuevo) || pinNuevo.Length != 4 || !pinNuevo.All(char.IsDigit))
            {
                CustomMessageBox.Show("El nuevo PIN debe contener exactamente 4 dígitos numéricos.", 
                    "Error de Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPinNuevo.Focus();
                return;
            }

            if (!pinNuevo.Equals(pinConfirmar))
            {
                CustomMessageBox.Show("La confirmación del nuevo PIN no coincide con el nuevo PIN ingresado.", 
                    "Error de Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPinConfirmar.Focus();
                return;
            }

            try
            {
                bool exito = _usuarioLogica.CambiarPIN(_rfidToken, pinActual, pinNuevo, _tokenGenerado);

                if (exito)
                {
                    CustomMessageBox.Show(
                        $"Su PIN ha sido actualizado exitosamente.\n\n" +
                        $"Token de autorización: {_tokenGenerado}\n" +
                        $"Utilice su nuevo código de 4 dígitos en sus próximas transacciones.", 
                        "Operación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.Message, "Error en Cambio de PIN", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
