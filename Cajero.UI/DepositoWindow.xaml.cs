using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Cajero.BLL;
using Cajerro.DAL;
using Cajerro.DAL.Modelos;

namespace Cajero.UI
{
    public partial class DepositoWindow : Window
    {
        private readonly string _rfidToken;
        private readonly UsuarioLogica _usuarioLogica;
        private readonly GestorArchivosCSV _dal;
        private readonly string _tokenGenerado;

        public DepositoWindow(string rfidToken)
        {
            InitializeComponent();
            _rfidToken = rfidToken;

            _dal = new GestorArchivosCSV();
            _usuarioLogica = new UsuarioLogica(_dal);

            // Generar token dinámico de 4 dígitos para esta transacción
            _tokenGenerado = UsuarioLogica.GenerarToken4Digitos();
            lblTokenGenerado.Text = _tokenGenerado;
        }

        private void CalcularTotal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || lblTotal == null) return;

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

        private int ObtenerCantidad(TextBox textBox)
        {
            if (textBox != null && int.TryParse(textBox.Text.Trim(), out int cantidad) && cantidad > 0)
                return cantidad;
            return 0;
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            string pin = pwdPin.Password.Trim();
            string tokenIngresado = txtTokenIngresado.Text.Trim();

            // 1. Verificación de PIN (4 dígitos)
            if (string.IsNullOrEmpty(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
            {
                CustomMessageBox.Show("El PIN de seguridad debe contener exactamente 4 dígitos numéricos.", 
                    "Validación de Seguridad", MessageBoxButton.OK, MessageBoxImage.Warning);
                pwdPin.Focus();
                return;
            }

            var usuario = _dal.ObtenerUsuarioPorTarjeta(_rfidToken);
            if (usuario == null || usuario.PIN != pin)
            {
                CustomMessageBox.Show("El PIN de seguridad ingresado es incorrecto.", 
                    "Credenciales Inválidas", MessageBoxButton.OK, MessageBoxImage.Error);
                pwdPin.Clear();
                pwdPin.Focus();
                return;
            }

            // 2. Verificación de Token de transacción (4 dígitos)
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

            // 3. Obtener billetes ingresados (todas las denominaciones oficiales incluyendo Q1)
            var billetesDepositados = new List<Billete>();
            AgregarBilleteSiExiste(billetesDepositados, 200, txtQ200);
            AgregarBilleteSiExiste(billetesDepositados, 100, txtQ100);
            AgregarBilleteSiExiste(billetesDepositados, 50, txtQ50);
            AgregarBilleteSiExiste(billetesDepositados, 20, txtQ20);
            AgregarBilleteSiExiste(billetesDepositados, 10, txtQ10);
            AgregarBilleteSiExiste(billetesDepositados, 5, txtQ5);
            AgregarBilleteSiExiste(billetesDepositados, 1, txtQ1);

            decimal total = billetesDepositados.Sum(b => b.Subtotal);
            if (total <= 0)
            {
                CustomMessageBox.Show("Debe ingresar al menos un billete para efectuar el depósito.", 
                    "Monto Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                bool exito = _usuarioLogica.Depositar(_rfidToken, billetesDepositados, _tokenGenerado);

                if (exito)
                {
                    CustomMessageBox.Show(
                        $"El depósito de Q{total:N2} se ha procesado y acreditado a su cuenta con éxito.\n\n" +
                        $"Token de transacción: {_tokenGenerado}\n" +
                        $"Nuevo saldo en cuenta: Q{(usuario.SaldoActual + total):N2}", 
                        "Transacción Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.Message, "Error en Transacción", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AgregarBilleteSiExiste(List<Billete> lista, int denominacion, TextBox textBox)
        {
            int cantidad = ObtenerCantidad(textBox);
            if (cantidad > 0)
            {
                lista.Add(new Billete(denominacion, cantidad));
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
