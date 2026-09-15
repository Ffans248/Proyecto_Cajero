using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace Cajero.UI
{
    public partial class AdminPanel : Window
    {
        private readonly ObservableCollection<TransaccionItem> _transacciones = new();

        public AdminPanel()
        {
            InitializeComponent();
            dgTransacciones.ItemsSource = _transacciones;
            ActualizarTotalTransacciones();
        }

        private void BtnRegistrarUsuario_Click(object sender, RoutedEventArgs e)
        {
            string nombre = txtNombre.Text.Trim();
            string pin = pwdNuevoPin.Password;
            string limiteText = txtLimite.Text.Trim();

            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(limiteText))
            {
                lblEstadoRegistro.Visibility = Visibility.Visible;
                lblEstadoRegistro.Text = "Por favor complete todos los campos obligatorios.";
                return;
            }

            if (!decimal.TryParse(limiteText, out decimal limite) || limite <= 0)
            {
                lblEstadoRegistro.Visibility = Visibility.Visible;
                lblEstadoRegistro.Text = "Ingrese un límite diario válido.";
                return;
            }

            lblEstadoRegistro.Visibility = Visibility.Visible;
            lblEstadoRegistro.Text = $"Usuario '{nombre}' registrado exitosamente.";

            MessageBox.Show($"El usuario '{nombre}' ha sido registrado correctamente en el sistema.",
                            "NEXUSBANK - Registro",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

            LimpiarFormularioRegistro();
        }

        private void BtnLimpiarCampos_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormularioRegistro();
        }

        private void LimpiarFormularioRegistro()
        {
            txtNombre.Clear();
            pwdNuevoPin.Clear();
            txtLimite.Clear();
            lblEstadoRegistro.Visibility = Visibility.Collapsed;
            txtNombre.Focus();
        }

        public void CargarTransacciones(IEnumerable<TransaccionItem> transacciones)
        {
            _transacciones.Clear();
            if (transacciones != null)
            {
                foreach (var item in transacciones)
                {
                    _transacciones.Add(item);
                }
            }
            ActualizarTotalTransacciones();
        }

        private void BtnActualizarTransacciones_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTotalTransacciones();
        }

        private void ActualizarTotalTransacciones()
        {
            lblTotalTransacciones.Text = $"Total de registros: {_transacciones.Count}";
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class TransaccionItem
    {
        public string Id { get; set; }
        public string FechaHora { get; set; }
        public string Usuario { get; set; }
        public string TipoOperacion { get; set; }
        public string MontoFormateado { get; set; }
        public string Estado { get; set; }

        public TransaccionItem(string id, string fechaHora, string usuario, string tipoOperacion, string montoFormateado, string estado)
        {
            Id = id;
            FechaHora = fechaHora;
            Usuario = usuario;
            TipoOperacion = tipoOperacion;
            MontoFormateado = montoFormateado;
            Estado = estado;
        }
    }
}