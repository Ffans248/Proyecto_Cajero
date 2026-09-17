using System;
using System.Windows;
using Cajero.BLL;
using Cajerro.DAL;

namespace Cajero.UI
{
    public partial class CambioPinWindow : Window
    {
        private readonly string _rfidToken;
        private readonly UsuarioLogica _usuarioLogica;

        public CambioPinWindow(string rfidToken)
        {
            InitializeComponent();
            _rfidToken = rfidToken;

            // Instanciamos el DAL y se lo pasamos al BLL como te gusta trabajar
            var dal = new GestorArchivosCSV();
            _usuarioLogica = new UsuarioLogica(dal);
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            string pinActual = txtPinActual.Password;
            string pinNuevo = txtPinNuevo.Password;

            try
            {
                // Llamamos a la lógica impecable que armaste hace un rato
                bool exito = _usuarioLogica.CambiarPIN(_rfidToken, pinActual, pinNuevo);

                if (exito)
                {
                    CustomMessageBox.Show("Su PIN ha sido actualizado exitosamente.", "Operación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.Message, "Error de Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}