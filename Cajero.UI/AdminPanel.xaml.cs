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
            string tarjeta = txtTarjeta.Text.Trim(); // El campo nuevo
            string nombre = txtNombre.Text.Trim();
            string pin = pwdNuevoPin.Password;
            string limiteText = txtLimite.Text.Trim();

            if (string.IsNullOrWhiteSpace(tarjeta) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(limiteText))
            {
                lblEstadoRegistro.Visibility = Visibility.Visible;
                lblEstadoRegistro.Text = "Complete todos los campos obligatorios.";
                return;
            }

            if (!decimal.TryParse(limiteText, out decimal limite) || limite <= 0)
            {
                lblEstadoRegistro.Visibility = Visibility.Visible;
                lblEstadoRegistro.Text = "Ingrese un límite diario válido.";
                return;
            }

            // Conectamos con el DAL para guardar el usuario
            var dal = new Cajerro.DAL.GestorArchivosCSV();

            // Validar que la tarjeta no exista ya
            if (dal.ObtenerUsuarioPorTarjeta(tarjeta) != null)
            {
                lblEstadoRegistro.Visibility = Visibility.Visible;
                lblEstadoRegistro.Text = "Este número de tarjeta ya existe en el sistema.";
                return;
            }

            var nuevoUsuario = new Cajerro.DAL.Modelos.Usuario
            {
                NumeroTarjeta = tarjeta,
                Nombre = nombre,
                PIN = pin,
                LimiteDiario = limite,
                SaldoActual = 0m // Saldo inicial en cero
            };

            if (dal.CrearNuevoUsuario(nuevoUsuario))
            {
                MessageBox.Show($"El usuario '{nombre}' ha sido registrado correctamente en el sistema.", "NEXUSBANK - Registro", MessageBoxButton.OK, MessageBoxImage.Information);
                LimpiarFormularioRegistro();
            }
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
            var dal = new Cajerro.DAL.GestorArchivosCSV();
            var transaccionesCSV = dal.ObtenerTodasLasTransacciones();

            _transacciones.Clear();

            // Llenamos el DataGrid con los datos reales del archivo
            int contador = 1;
            foreach (var t in transaccionesCSV)
            {
                _transacciones.Add(new TransaccionItem(
                    $"TX-{contador++:D3}", // ID visual generado al vuelo
                    t.FechaHora.ToString("dd/MM/yyyy HH:mm"),
                    t.NumeroTarjeta,
                    t.TipoMovimiento,
                    $"Q {t.Monto:N2}",
                    "Completado"
                ));
            }

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