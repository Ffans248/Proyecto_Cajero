using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Cajero.BLL;
using Cajerro.DAL;

namespace Cajero.UI
{
    public partial class DepositoWindow : Window
    {
        private readonly string _rfidToken;
        private readonly UsuarioLogica _usuarioLogica;

        public DepositoWindow(string rfidToken)
        {
            InitializeComponent();
            _rfidToken = rfidToken;

            var dal = new GestorArchivosCSV();
            _usuarioLogica = new UsuarioLogica(dal);
        }

        private void CalcularTotal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;

            decimal total = 0;
            total += ObtenerCantidad(txtQ200) * 200;
            total += ObtenerCantidad(txtQ100) * 100;
            total += ObtenerCantidad(txtQ50) * 50;
            total += ObtenerCantidad(txtQ20) * 20;
            total += ObtenerCantidad(txtQ10) * 10;
            total += ObtenerCantidad(txtQ5) * 5;
            total += ObtenerCantidad(txtQ1) * 1;

            if (lblTotal != null)
                lblTotal.Text = $"Q {total:N2}";
        }

        private int ObtenerCantidad(TextBox textBox)
        {
            if (textBox != null && int.TryParse(textBox.Text, out int cantidad) && cantidad >= 0)
                return cantidad;
            return 0;
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            var billetesDepositados = new List<Cajerro.DAL.Modelos.Billete>();

            AgregarBilleteSiExiste(billetesDepositados, 200, txtQ200);
            AgregarBilleteSiExiste(billetesDepositados, 100, txtQ100);
            AgregarBilleteSiExiste(billetesDepositados, 50, txtQ50);
            AgregarBilleteSiExiste(billetesDepositados, 20, txtQ20);
            AgregarBilleteSiExiste(billetesDepositados, 10, txtQ10);
            AgregarBilleteSiExiste(billetesDepositados, 5, txtQ5);
            AgregarBilleteSiExiste(billetesDepositados, 1, txtQ1);

            try
            {
                bool exito = _usuarioLogica.Depositar(_rfidToken, billetesDepositados);

                if (exito)
                {
                    MessageBox.Show("El depósito se ha realizado y acreditado a su cuenta con éxito.", "Transacción Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error en Transacción", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AgregarBilleteSiExiste(List<Cajerro.DAL.Modelos.Billete> lista, int denominacion, TextBox textBox)
        {
            int cantidad = ObtenerCantidad(textBox);
            if (cantidad > 0)
            {
                lista.Add(new Cajerro.DAL.Modelos.Billete(denominacion, cantidad));
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}   