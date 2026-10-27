using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Cajero.BLL;
using Cajerro.DAL.Modelos;

namespace Cajero.UI
{
    public partial class RetiroWindow : Window
    {
        private readonly string _rfidToken;
        private readonly UsuarioLogica _usuarioLogica;
        private readonly string _tokenGenerado;

        public RetiroWindow(string rfidToken)
        {
            InitializeComponent();
            _rfidToken = rfidToken;

            var dal = new Cajerro.DAL.GestorArchivosCSV();
            _usuarioLogica = new UsuarioLogica(dal);

            // Generar token dinámico de 4 dígitos para esta transacción
            _tokenGenerado = UsuarioLogica.GenerarToken4Digitos();
            lblTokenGenerado.Text = _tokenGenerado;
        }

        private void CalcularTotal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (lblTotal == null) return;

            decimal total = 0;
            total += ObtenerCantidad(txtQ200) * 200;
            total += ObtenerCantidad(txtQ100) * 100;
            total += ObtenerCantidad(txtQ50) * 50;
            total += ObtenerCantidad(txtQ20) * 20;
            total += ObtenerCantidad(txtQ10) * 10;
            total += ObtenerCantidad(txtQ5) * 5;
            total += ObtenerCantidad(txtQ1) * 1;

            lblTotal.Text = $"Q {total:N2}";
        }

        private int ObtenerCantidad(TextBox txt)
        {
            if (txt != null && int.TryParse(txt.Text.Trim(), out int cant) && cant > 0)
                return cant;
            return 0;
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            string pin = pwdPin.Password.Trim();
            string tokenIngresado = txtTokenIngresado.Text.Trim();

            // 1. Verificación de PIN (únicamente 4 dígitos)
            if (string.IsNullOrEmpty(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
            {
                CustomMessageBox.Show("El PIN de seguridad debe contener exactamente 4 dígitos numéricos.", 
                    "Validación de Seguridad", MessageBoxButton.OK, MessageBoxImage.Warning);
                pwdPin.Focus();
                return;
            }

            // 2. Verificación de Token de transacción (únicamente 4 dígitos)
            if (string.IsNullOrEmpty(tokenIngresado) || tokenIngresado.Length != 4 || !tokenIngresado.All(char.IsDigit))
            {
                CustomMessageBox.Show("El Token de autorización debe contener exactamente 4 dígitos numéricos.", 
                    "Validación de Seguridad", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtTokenIngresado.Focus();
                return;
            }

            if (!tokenIngresado.Equals(_tokenGenerado))
            {
                CustomMessageBox.Show("El Token ingresado no coincide con el Token generado para esta transacción.", 
                    "Token Inválido", MessageBoxButton.OK, MessageBoxImage.Error);
                txtTokenIngresado.Focus();
                return;
            }

            // 3. Monto total solicitado
            decimal totalSolicitado = 0;
            totalSolicitado += ObtenerCantidad(txtQ200) * 200;
            totalSolicitado += ObtenerCantidad(txtQ100) * 100;
            totalSolicitado += ObtenerCantidad(txtQ50) * 50;
            totalSolicitado += ObtenerCantidad(txtQ20) * 20;
            totalSolicitado += ObtenerCantidad(txtQ10) * 10;
            totalSolicitado += ObtenerCantidad(txtQ5) * 5;
            totalSolicitado += ObtenerCantidad(txtQ1) * 1;

            if (totalSolicitado <= 0)
            {
                CustomMessageBox.Show("Debe seleccionar al menos un billete para realizar el retiro.", 
                    "Monto Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 4. Desglose con las 7 denominaciones oficiales (incluyendo la de Q1)
            var desgloseSolicitado = new List<Billete>
            {
                new Billete(200, ObtenerCantidad(txtQ200)),
                new Billete(100, ObtenerCantidad(txtQ100)),
                new Billete(50, ObtenerCantidad(txtQ50)),
                new Billete(20, ObtenerCantidad(txtQ20)),
                new Billete(10, ObtenerCantidad(txtQ10)),
                new Billete(5, ObtenerCantidad(txtQ5)),
                new Billete(1, ObtenerCantidad(txtQ1))
            };

            try
            {
                bool exito = _usuarioLogica.RetirarConDesgloseManual(
                    _rfidToken, 
                    pin, 
                    totalSolicitado, 
                    desgloseSolicitado, 
                    _tokenGenerado
                );

                if (exito)
                {
                    CustomMessageBox.Show(
                        $"Retiro procesado correctamente por Q{totalSolicitado:N2}.\n\n" +
                        $"Token de autorización: {_tokenGenerado}\n" +
                        $"Por favor, retire su dinero en efectivo de la gaveta.", 
                        "Transacción Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.Message, "Error en Transacción", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
