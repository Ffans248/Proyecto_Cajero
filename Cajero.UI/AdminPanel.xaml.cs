using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Cajero.BLL;

namespace Cajero.UI
{
    public partial class AdminPanel : Window
    {
        private readonly ObservableCollection<TransaccionItem> _transacciones = new();
        private readonly ObservableCollection<BilleteItem> _stockBoveda = new();
        private Cajerro.DAL.Modelos.Usuario _usuarioModificacionActual;
        private readonly GestorTransacciones _gestorTransacciones;
        private readonly Cajerro.DAL.GestorArchivosCSV _dal;

        public AdminPanel()
        {
            InitializeComponent();
            
            _dal = new Cajerro.DAL.GestorArchivosCSV();
            _gestorTransacciones = new GestorTransacciones(new AdaptadorCajero(_dal));

            dgTransacciones.ItemsSource = _transacciones;
            lvStockBoveda.ItemsSource = _stockBoveda;
            
            ActualizarTotalTransacciones();
            CargarStockBoveda();
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

            // Validar que la tarjeta no exista ya
            if (_dal.ObtenerUsuarioPorTarjeta(tarjeta) != null)
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

            if (_dal.CrearNuevoUsuario(nuevoUsuario))
            {
                CustomMessageBox.Show($"El usuario '{nombre}' ha sido registrado correctamente en el sistema.", "NEXUSBANK - Registro", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var transaccionesCSV = _dal.ObtenerTodasLasTransacciones();

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

        // ==========================================
        // LÓGICA DE MODIFICAR USUARIO
        // ==========================================
        private void BtnBuscarUsuarioMod_Click(object sender, RoutedEventArgs e)
        {
            string tarjeta = txtBuscarTarjetaMod.Text.Trim();
            if (string.IsNullOrWhiteSpace(tarjeta)) return;

            _usuarioModificacionActual = _dal.ObtenerUsuarioPorTarjeta(tarjeta);

            if (_usuarioModificacionActual != null)
            {
                panelModificarUsuario.Visibility = Visibility.Visible;
                lblNombreUsuarioMod.Text = $"Nombre: {_usuarioModificacionActual.Nombre}";
                txtLimiteMod.Text = _usuarioModificacionActual.LimiteDiario.ToString("0.00");
                pwdPinMod.Clear();
                lblEstadoModificacion.Visibility = Visibility.Collapsed;
            }
            else
            {
                panelModificarUsuario.Visibility = Visibility.Collapsed;
                CustomMessageBox.Show("No se encontró ningún usuario con esa tarjeta.", "Usuario no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnGuardarCambios_Click(object sender, RoutedEventArgs e)
        {
            if (_usuarioModificacionActual == null) return;

            string nuevoPin = pwdPinMod.Password;
            string limiteText = txtLimiteMod.Text.Trim();

            if (!decimal.TryParse(limiteText, out decimal nuevoLimite) || nuevoLimite <= 0)
            {
                lblEstadoModificacion.Visibility = Visibility.Visible;
                lblEstadoModificacion.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoModificacion.Text = "Ingrese un límite diario válido.";
                return;
            }

            _usuarioModificacionActual.LimiteDiario = nuevoLimite;
            
            if (!string.IsNullOrWhiteSpace(nuevoPin))
            {
                if (nuevoPin.Length != 4 || !nuevoPin.All(char.IsDigit))
                {
                    lblEstadoModificacion.Visibility = Visibility.Visible;
                    lblEstadoModificacion.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                    lblEstadoModificacion.Text = "El nuevo PIN debe tener exactamente 4 dígitos.";
                    return;
                }
                _usuarioModificacionActual.PIN = nuevoPin;
            }

            bool exito = _dal.ActualizarUsuario(_usuarioModificacionActual);
            
            lblEstadoModificacion.Visibility = Visibility.Visible;
            if (exito)
            {
                lblEstadoModificacion.Foreground = (System.Windows.Media.Brush)FindResource("AccentSuccessBrush");
                lblEstadoModificacion.Text = "Usuario actualizado exitosamente.";
                pwdPinMod.Clear();
            }
            else
            {
                lblEstadoModificacion.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoModificacion.Text = "Error al actualizar el usuario.";
            }
        }

        // ==========================================
        // LÓGICA DE BÓVEDA Y STOCK
        // ==========================================
        private void CargarStockBoveda()
        {
            var inventario = _dal.ObtenerInventarioBilletes();
            _stockBoveda.Clear();
            
            decimal total = 0;
            if (inventario != null)
            {
                foreach (var billete in inventario.OrderByDescending(b => b.Denominacion))
                {
                    _stockBoveda.Add(new BilleteItem(billete.Denominacion, billete.Cantidad));
                    total += billete.Denominacion * billete.Cantidad;
                }
            }

            lblTotalBoveda.Text = $"Q {total:N2}";
        }

        private void BtnProcesarRecarga_Click(object sender, RoutedEventArgs e)
        {
            lblEstadoRecarga.Visibility = Visibility.Collapsed;

            try
            {
                var billetesRecarga = new Dictionary<int, int>
                {
                    { 200, int.Parse(string.IsNullOrWhiteSpace(txtRecarga200.Text) ? "0" : txtRecarga200.Text) },
                    { 100, int.Parse(string.IsNullOrWhiteSpace(txtRecarga100.Text) ? "0" : txtRecarga100.Text) },
                    { 50, int.Parse(string.IsNullOrWhiteSpace(txtRecarga50.Text) ? "0" : txtRecarga50.Text) },
                    { 20, int.Parse(string.IsNullOrWhiteSpace(txtRecarga20.Text) ? "0" : txtRecarga20.Text) },
                    { 10, int.Parse(string.IsNullOrWhiteSpace(txtRecarga10.Text) ? "0" : txtRecarga10.Text) },
                    { 5, int.Parse(string.IsNullOrWhiteSpace(txtRecarga5.Text) ? "0" : txtRecarga5.Text) }
                };

                if (billetesRecarga.Values.All(v => v == 0))
                {
                    lblEstadoRecarga.Visibility = Visibility.Visible;
                    lblEstadoRecarga.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                    lblEstadoRecarga.Text = "Debe ingresar al menos un billete para recargar.";
                    return;
                }

                var respuesta = _gestorTransacciones.RecargarBoveda(billetesRecarga);

                lblEstadoRecarga.Visibility = Visibility.Visible;
                if (respuesta.Exito)
                {
                    lblEstadoRecarga.Foreground = (System.Windows.Media.Brush)FindResource("AccentSuccessBrush");
                    lblEstadoRecarga.Text = respuesta.Mensaje;
                    
                    // Limpiar campos
                    txtRecarga200.Text = "0"; txtRecarga100.Text = "0"; txtRecarga50.Text = "0";
                    txtRecarga20.Text = "0"; txtRecarga10.Text = "0"; txtRecarga5.Text = "0";
                    
                    CargarStockBoveda(); // Refrescar la vista
                }
                else
                {
                    lblEstadoRecarga.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                    lblEstadoRecarga.Text = respuesta.Mensaje;
                }
            }
            catch (Exception ex)
            {
                lblEstadoRecarga.Visibility = Visibility.Visible;
                lblEstadoRecarga.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoRecarga.Text = "Error: Ingrese cantidades numéricas válidas.";
            }
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

    public class BilleteItem
    {
        public int Denominacion { get; set; }
        public int Cantidad { get; set; }

        public string DenominacionFormat => $"Q {Denominacion}";
        public string CantidadFormat => $"{Cantidad} un.";
        public string SubtotalFormat => $"Q {(Denominacion * Cantidad):N2}";

        public BilleteItem(int denominacion, int cantidad)
        {
            Denominacion = denominacion;
            Cantidad = cantidad;
        }
    }
}