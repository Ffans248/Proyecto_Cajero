using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Cajero.BLL;

namespace Cajero.UI
{
    public partial class RetiroWindow : Window
    {
        private readonly string _rfidToken;
        private readonly UsuarioLogica _usuarioLogica; // Tu capa BLL

        public RetiroWindow(string rfidToken)
        {
            InitializeComponent();
            _rfidToken = rfidToken;

            // Instanciamos tu lógica y el DAL de Martín (que ahora ya tienes en tu rama)
            var dal = new Cajerro.DAL.GestorArchivosCSV();
            _usuarioLogica = new UsuarioLogica(dal);
        }

        private void CalcularTotal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (lblTotal == null) return; // Evita errores al inicializar la ventana

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
            if (int.TryParse(txt.Text, out int cant) && cant > 0)
                return cant;
            return 0;
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            // 1. Calculamos el monto total solicitado (como lo hacemos en el TextChanged)
            decimal totalSolicitado = 0;
            totalSolicitado += ObtenerCantidad(txtQ200) * 200;
            totalSolicitado += ObtenerCantidad(txtQ100) * 100;
            totalSolicitado += ObtenerCantidad(txtQ50) * 50;
            totalSolicitado += ObtenerCantidad(txtQ20) * 20;
            totalSolicitado += ObtenerCantidad(txtQ10) * 10;
            totalSolicitado += ObtenerCantidad(txtQ5) * 5;
            totalSolicitado += ObtenerCantidad(txtQ1) * 1;

            // 2. Armamos la Lista de Billetes que pide tu método BLL usando el modelo del DAL
            var desgloseSolicitado = new List<Cajerro.DAL.Modelos.Billete>
    {
        new Cajerro.DAL.Modelos.Billete { Denominacion = 200, Cantidad = ObtenerCantidad(txtQ200) },
        new Cajerro.DAL.Modelos.Billete { Denominacion = 100, Cantidad = ObtenerCantidad(txtQ100) },
        new Cajerro.DAL.Modelos.Billete { Denominacion = 50, Cantidad = ObtenerCantidad(txtQ50) },
        new Cajerro.DAL.Modelos.Billete { Denominacion = 20, Cantidad = ObtenerCantidad(txtQ20) },
        new Cajerro.DAL.Modelos.Billete { Denominacion = 10, Cantidad = ObtenerCantidad(txtQ10) },
        new Cajerro.DAL.Modelos.Billete { Denominacion = 5, Cantidad = ObtenerCantidad(txtQ5) },
        new Cajerro.DAL.Modelos.Billete { Denominacion = 1, Cantidad = ObtenerCantidad(txtQ1) }
    };

            try
            {
                // 3. Llamamos a TU código pasando todos los parámetros (Tarjeta, PIN, Monto, Desglose)
                // NOTA: Como la tarjeta y el PIN los ingresó en la ventana de Login, por ahora
                // asumo que el token guarda la tarjeta. Si tienes el PIN guardado, pásalo aquí.
                // Para pruebas, pasaremos strings temporales.
                string tarjetaSimulada = _rfidToken; // O ajusta según cómo manejes la sesión
                string pinSimulado = "1234";         // Ajustar con el PIN real

                bool exito = _usuarioLogica.RetirarConDesgloseManual(tarjetaSimulada, pinSimulado, totalSolicitado, desgloseSolicitado);

                if (exito)
                {
                    CustomMessageBox.Show("Retiro procesado correctamente. Por favor tome su efectivo.", 
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