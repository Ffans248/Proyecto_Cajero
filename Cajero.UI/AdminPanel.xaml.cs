using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Cajero.BLL;
using Cajerro.DAL;
using Cajerro.DAL.Modelos;

namespace Cajero.UI
{
    public partial class AdminPanel : Window
    {
        private readonly ObservableCollection<TransaccionItem> _transacciones = new();
        private readonly ObservableCollection<BilleteItem> _stockBoveda = new();
        private readonly ObservableCollection<RegistroBitacora> _bitacora = new();
        private readonly ObservableCollection<BancoRedVistaItem> _bancosRed = new();
        private readonly ObservableCollection<TransferenciaBancariaVistaItem> _transferenciasBancarias = new();
        private Usuario? _usuarioModificacionActual;
        private readonly GestorArchivosCSV _dal;
        private readonly UsuarioLogica _usuarioLogica;

        public AdminPanel()
        {
            InitializeComponent();

            _dal = new GestorArchivosCSV();
            _usuarioLogica = new UsuarioLogica(_dal);

            dgTransacciones.ItemsSource = _transacciones;
            lvStockBoveda.ItemsSource = _stockBoveda;
            dgBitacora.ItemsSource = _bitacora;
            dgBancosRed.ItemsSource = _bancosRed;
            dgTransferenciasBancarias.ItemsSource = _transferenciasBancarias;

            CargarStockBoveda();
            CargarTransacciones();
            CargarBitacora();
            CargarRedInterbancaria();
            CargarDashboard();
            CargarValoresSugeridosInit();
        }

        #region =================== 1. DASHBOARD Y MÉTRICAS ===================

        private async void BtnRefrescarDashboard_Click(object sender, RoutedEventArgs e)
        {
            await EjecutarFeedbackActualizacionAsync(btnRefrescarDashboard, pnlContenedorDashboard, "Actualizar Métricas", () =>
            {
                CargarDashboard();
                CargarStockBoveda();
                CargarTransacciones();
                CargarBitacora();
            });
        }

        private async Task EjecutarFeedbackActualizacionAsync(Button btn, UIElement elementoAParpadear, string textoOriginal, Action accionCarga)
        {
            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "⏳ Actualizando...";
            }

            // 1. Animación de parpadeo visual en el contenedor/menú
            if (elementoAParpadear != null)
            {
                var anim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.20,
                    Duration = TimeSpan.FromMilliseconds(130),
                    AutoReverse = true,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                elementoAParpadear.BeginAnimation(UIElement.OpacityProperty, anim);
            }

            // Pequeña pausa para hacer visible el parpadeo
            await Task.Delay(160);

            // 2. Ejecutar la recarga de datos
            accionCarga();

            // 3. Feedback visual de éxito
            if (btn != null)
            {
                btn.Content = "✓ ¡Actualizado!";
                await Task.Delay(650);

                // 4. Restaurar texto original y reactivar
                btn.Content = textoOriginal;
                btn.IsEnabled = true;
            }
        }

        private void CargarDashboard()
        {
            try
            {
                var transacciones = _dal.ObtenerTodasLasTransacciones();
                var usuarios = _dal.ObtenerTodosLosUsuarios();
                var bitacora = _dal.ObtenerBitacora();
                var boveda = _dal.ObtenerInventarioBilletes();

                // 1. Total Retirado Hoy
                var retirosHoy = transacciones
                    .Where(t => t.FechaHora.Date == DateTime.Today && t.TipoMovimiento.Equals("Retiro", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                decimal totalRetiradoHoy = retirosHoy.Sum(t => t.Monto);
                lblKpiTotalRetiradoHoy.Text = $"Q {totalRetiradoHoy:N2}";

                // 2. Promedio de Depósitos por todos los usuarios
                var depositos = transacciones
                    .Where(t => t.TipoMovimiento.Equals("Depósito", StringComparison.OrdinalIgnoreCase) || t.TipoMovimiento.Equals("Deposito", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                decimal promedioDepositos = depositos.Count > 0 ? depositos.Average(t => t.Monto) : 0m;
                lblKpiPromedioDepositos.Text = $"Q {promedioDepositos:N2}";

                // 3. Promedio de Retiros por todos los usuarios
                var todosLosRetiros = transacciones
                    .Where(t => t.TipoMovimiento.Equals("Retiro", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                decimal promedioRetiros = todosLosRetiros.Count > 0 ? todosLosRetiros.Average(t => t.Monto) : 0m;
                lblKpiPromedioRetiros.Text = $"Q {promedioRetiros:N2}";

                // 4. Total en Bóveda
                decimal totalBoveda = boveda.Sum(b => b.Subtotal);
                lblKpiTotalBoveda.Text = $"Q {totalBoveda:N2}";

                bool estaInit = _dal.EstaCajeroInicializado();
                lblKpiEstadoBoveda.Text = estaInit ? "Estado: Inicializado (Operativo)" : "Estado: Pendiente de inicializar";
                lblEstadoInicializadoBadge.Text = estaInit ? "Inicializado hoy" : "Pendiente de inicializar";
                lblEstadoInicializadoBadge.Foreground = (System.Windows.Media.Brush)FindResource(estaInit ? "AccentSuccessBrush" : "AccentDangerBrush");

                // 5. Total Depositado Hoy
                var depositosHoy = depositos.Where(t => t.FechaHora.Date == DateTime.Today).ToList();
                decimal totalDepositadoHoy = depositosHoy.Sum(t => t.Monto);
                lblKpiTotalDepositadoHoy.Text = $"Q {totalDepositadoHoy:N2}";

                // 6. Total Transacciones
                lblKpiTotalTransacciones.Text = transacciones.Count.ToString();
                int totalHoy = transacciones.Count(t => t.FechaHora.Date == DateTime.Today);
                lblKpiTransaccionesHoy.Text = $"{totalHoy} movimientos hoy";

                // 7. Usuarios Activos
                lblKpiUsuariosActivos.Text = usuarios.Count.ToString();

                // 8. Usuarios que han hecho cambio de PIN
                var cambiosPinEventos = bitacora
                    .Where(b => b.TipoEvento.Equals("Cambio de PIN", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var listaUsuariosPin = new List<string>();
                var usuariosPinUnicos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var evt in cambiosPinEventos)
                {
                    string info = $"{evt.FechaHora:dd/MM/yyyy HH:mm} - Tarjeta: {evt.UsuarioOTarjeta}";
                    listaUsuariosPin.Add(info);
                    usuariosPinUnicos.Add(evt.UsuarioOTarjeta);
                }

                lbUsuariosCambioPin.ItemsSource = listaUsuariosPin.Take(20).ToList();
                lblKpiCambiosPin.Text = usuariosPinUnicos.Count.ToString();

                // 9. Último usuario que accedió al cajero y fecha completa
                var ultimoAcceso = _dal.ObtenerUltimoAccesoGeneral();
                if (ultimoAcceso != null)
                {
                    var u = usuarios.FirstOrDefault(usr => usr.NumeroTarjeta.Equals(ultimoAcceso.UsuarioOTarjeta, StringComparison.OrdinalIgnoreCase));
                    string nombreUsuario = u != null ? $"{u.Nombre} ({ultimoAcceso.UsuarioOTarjeta})" : ultimoAcceso.UsuarioOTarjeta;
                    lblUltimoUsuarioAcceso.Text = nombreUsuario;
                    lblUltimaFechaAcceso.Text = ultimoAcceso.FechaHora.ToString("dddd, dd 'de' MMMM 'de' yyyy - HH:mm:ss", new CultureInfo("es-GT"));
                }
                else
                {
                    lblUltimoUsuarioAcceso.Text = "Ningún acceso registrado aún";
                    lblUltimaFechaAcceso.Text = "Sin registros en bitácora";
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Error al calcular métricas del Dashboard: {ex.Message}", "Error Dashboard", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region =================== 2. GESTIÓN Y CONSULTA DE USUARIOS ===================

        private void BtnRegistrarUsuario_Click(object sender, RoutedEventArgs e)
        {
            string tarjeta = txtTarjeta.Text.Trim();
            string nombre = txtNombre.Text.Trim();
            string pin = pwdNuevoPin.Password.Trim();
            string saldoText = txtSaldoInicial.Text.Trim();
            string limiteText = txtLimite.Text.Trim();

            if (string.IsNullOrWhiteSpace(tarjeta) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(limiteText))
            {
                MostrarEstadoRegistro("Todos los campos con asterisco son requeridos.", false);
                return;
            }

            if (tarjeta.Length != 16 || !tarjeta.All(char.IsDigit))
            {
                MostrarEstadoRegistro("El número de tarjeta debe tener exactamente 16 dígitos numéricos.", false);
                return;
            }

            if (pin.Length != 4 || !pin.All(char.IsDigit))
            {
                MostrarEstadoRegistro("El PIN de seguridad debe contener exactamente 4 dígitos numéricos.", false);
                return;
            }

            if (!decimal.TryParse(saldoText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal saldo) &&
                !decimal.TryParse(saldoText, out saldo) || saldo < 0)
            {
                MostrarEstadoRegistro("El saldo inicial no puede ser negativo.", false);
                return;
            }

            if (!decimal.TryParse(limiteText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal limite) &&
                !decimal.TryParse(limiteText, out limite) || limite <= 0)
            {
                MostrarEstadoRegistro("Ingrese un límite diario de retiro válido mayor a cero.", false);
                return;
            }

            if (_dal.ObtenerUsuarioPorTarjeta(tarjeta) != null)
            {
                MostrarEstadoRegistro($"La tarjeta '{tarjeta}' ya se encuentra registrada en el sistema.", false);
                return;
            }

            var nuevoUsuario = new Usuario
            {
                NumeroTarjeta = tarjeta,
                Nombre = nombre,
                PIN = pin,
                SaldoActual = saldo,
                LimiteDiario = limite
            };

            if (_dal.CrearNuevoUsuario(nuevoUsuario))
            {
                _dal.RegistrarEnBitacora("Registro Usuario", tarjeta, $"Alta de usuario exitosa: {nombre}. Límite: Q{limite:N2}, Saldo: Q{saldo:N2}.");
                CustomMessageBox.Show($"El usuario '{nombre}' ha sido registrado exitosamente.", "Registro Exitoso", MessageBoxButton.OK, MessageBoxImage.Information);
                LimpiarFormularioRegistro();
                CargarDashboard();
            }
            else
            {
                MostrarEstadoRegistro("Error al escribir el registro en Usuarios.csv.", false);
            }
        }

        private void MostrarEstadoRegistro(string mensaje, bool exito)
        {
            lblEstadoRegistro.Text = mensaje;
            lblEstadoRegistro.Foreground = (System.Windows.Media.Brush)FindResource(exito ? "AccentSuccessBrush" : "AccentDangerBrush");
            lblEstadoRegistro.Visibility = Visibility.Visible;
        }

        private void BtnLimpiarCampos_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormularioRegistro();
        }

        private void LimpiarFormularioRegistro()
        {
            txtTarjeta.Clear();
            txtNombre.Clear();
            pwdNuevoPin.Clear();
            txtSaldoInicial.Text = "0.00";
            txtLimite.Clear();
            lblEstadoRegistro.Visibility = Visibility.Collapsed;
            txtTarjeta.Focus();
        }

        private void BtnBuscarUsuarioMod_Click(object sender, RoutedEventArgs e)
        {
            string tarjeta = txtBuscarTarjetaMod.Text.Trim();
            if (string.IsNullOrWhiteSpace(tarjeta)) return;

            _usuarioModificacionActual = _dal.ObtenerUsuarioPorTarjeta(tarjeta);

            if (_usuarioModificacionActual != null)
            {
                panelModificarUsuario.Visibility = Visibility.Visible;
                lblNombreUsuarioMod.Text = $"Titular: {_usuarioModificacionActual.Nombre} (Tarjeta: {_usuarioModificacionActual.NumeroTarjeta})";
                txtLimiteMod.Text = _usuarioModificacionActual.LimiteDiario.ToString("0.00");
                txtNuevaTarjetaMod.Clear();
                pwdPinMod.Clear();
                lblEstadoModificacion.Visibility = Visibility.Collapsed;
            }
            else
            {
                panelModificarUsuario.Visibility = Visibility.Collapsed;
                CustomMessageBox.Show("No se encontró ningún usuario con ese número de tarjeta.", "Usuario No Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnGuardarCambios_Click(object sender, RoutedEventArgs e)
        {
            if (_usuarioModificacionActual == null) return;

            string tarjetaActual = _usuarioModificacionActual.NumeroTarjeta;
            string nuevoPin = pwdPinMod.Password.Trim();
            string limiteText = txtLimiteMod.Text.Trim();
            string nuevaTarjeta = txtNuevaTarjetaMod.Text.Trim();

            // 1. Modificación de Límite Diario
            if (!string.IsNullOrWhiteSpace(limiteText))
            {
                if ((!decimal.TryParse(limiteText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal nuevoLimite) &&
                     !decimal.TryParse(limiteText, out nuevoLimite)) || nuevoLimite <= 0)
                {
                    MostrarEstadoModificacion("El límite diario debe ser un monto numérico positivo.", false);
                    return;
                }

                if (nuevoLimite != _usuarioModificacionActual.LimiteDiario)
                {
                    decimal limiteViejo = _usuarioModificacionActual.LimiteDiario;
                    _usuarioModificacionActual.LimiteDiario = nuevoLimite;
                    _dal.RegistrarEnBitacora("Modificación de Límite", tarjetaActual, $"Límite diario cambiado de Q{limiteViejo:N2} a Q{nuevoLimite:N2}.");
                }
            }

            // 2. Modificación de PIN (4 dígitos)
            if (!string.IsNullOrWhiteSpace(nuevoPin))
            {
                if (nuevoPin.Length != 4 || !nuevoPin.All(char.IsDigit))
                {
                    MostrarEstadoModificacion("El nuevo PIN debe tener exactamente 4 dígitos numéricos.", false);
                    return;
                }
                _usuarioModificacionActual.PIN = nuevoPin;
                _dal.RegistrarEnBitacora("Cambio de PIN", tarjetaActual, $"PIN modificado desde la consola de administración para {_usuarioModificacionActual.Nombre}.");
            }

            // 3. Modificación de Tarjeta (con récord histórico para control de límite diario)
            if (!string.IsNullOrWhiteSpace(nuevaTarjeta) && !nuevaTarjeta.Equals(tarjetaActual, StringComparison.OrdinalIgnoreCase))
            {
                if (nuevaTarjeta.Length != 16 || !nuevaTarjeta.All(char.IsDigit))
                {
                    MostrarEstadoModificacion("El nuevo número de tarjeta debe tener exactamente 16 dígitos numéricos.", false);
                    return;
                }

                try
                {
                    _dal.ModificarNumeroTarjeta(tarjetaActual, nuevaTarjeta);
                    _usuarioModificacionActual.NumeroTarjeta = nuevaTarjeta;
                }
                catch (Exception ex)
                {
                    MostrarEstadoModificacion(ex.Message, false);
                    return;
                }
            }

            bool exito = _dal.ActualizarUsuario(_usuarioModificacionActual);

            if (exito)
            {
                MostrarEstadoModificacion("Modificaciones guardadas exitosamente en el sistema.", true);
                pwdPinMod.Clear();
                txtNuevaTarjetaMod.Clear();
                lblNombreUsuarioMod.Text = $"Titular: {_usuarioModificacionActual.Nombre} (Tarjeta: {_usuarioModificacionActual.NumeroTarjeta})";
                CargarDashboard();
            }
            else
            {
                MostrarEstadoModificacion("Error al persistir los cambios del usuario.", false);
            }
        }

        private void MostrarEstadoModificacion(string mensaje, bool exito)
        {
            lblEstadoModificacion.Text = mensaje;
            lblEstadoModificacion.Foreground = (System.Windows.Media.Brush)FindResource(exito ? "AccentSuccessBrush" : "AccentDangerBrush");
            lblEstadoModificacion.Visibility = Visibility.Visible;
        }

        private void BtnConsultarUsuario_Click(object sender, RoutedEventArgs e)
        {
            string tarjeta = txtTarjetaConsulta.Text.Trim();
            if (string.IsNullOrWhiteSpace(tarjeta)) return;

            var usuario = _dal.ObtenerUsuarioPorTarjeta(tarjeta);
            if (usuario == null)
            {
                panelResultadoConsulta.Visibility = Visibility.Collapsed;
                CustomMessageBox.Show($"No se encontró ningún usuario con la tarjeta '{tarjeta}'.", "Consulta de Usuario", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Calcular saldo, retiros acumulados de hoy (incluyendo tarjetas vinculadas) y último acceso
            var (saldo, disponible) = _usuarioLogica.VerSaldo(tarjeta);
            decimal retiradoHoy = _usuarioLogica.ObtenerRetirosAcumuladosHoy(tarjeta);
            DateTime? ultimoAcceso = _dal.ObtenerUltimoAcceso(tarjeta);

            panelResultadoConsulta.Visibility = Visibility.Visible;
            lblConsultaNombre.Text = $"{usuario.Nombre} ({usuario.NumeroTarjeta})";
            lblConsultaSaldo.Text = $"Q {saldo:N2}";
            lblConsultaLimite.Text = $"Q {usuario.LimiteDiario:N2}";
            lblConsultaRetiradoHoy.Text = $"Q {retiradoHoy:N2}";
            lblConsultaDisponibleRestante.Text = $"Q {disponible:N2}";
            lblConsultaUltimoAcceso.Text = ultimoAcceso.HasValue 
                ? ultimoAcceso.Value.ToString("dd/MM/yyyy HH:mm:ss") 
                : "Sin accesos registrados en bitácora";
        }

        #endregion

        #region =================== 3. BÓVEDA Y CONTROL DE EFECTIVO ===================

        private void CargarStockBoveda()
        {
            var inventario = _dal.ObtenerInventarioBilletes();
            _stockBoveda.Clear();

            decimal total = 0m;
            if (inventario != null)
            {
                foreach (var billete in inventario.OrderByDescending(b => b.Denominacion))
                {
                    _stockBoveda.Add(new BilleteItem(billete.Denominacion, billete.Cantidad));
                    total += billete.Subtotal;
                }
            }

            lblTotalBoveda.Text = $"Q {total:N2}";
            lblKpiTotalBoveda.Text = $"Q {total:N2}";

            bool estaInit = _dal.EstaCajeroInicializado();
            ActualizarEstadoVisualInicializacion(estaInit);
        }

        private void ActualizarEstadoVisualInicializacion(bool estaInit)
        {
            lblEstadoInicializadoBadge.Text = estaInit ? "Inicializado Hoy (Operativo)" : "Pendiente de inicializar";
            lblEstadoInicializadoBadge.Foreground = (System.Windows.Media.Brush)FindResource(estaInit ? "AccentSuccessBrush" : "AccentDangerBrush");

            lblKpiEstadoBoveda.Text = estaInit ? "Estado: Inicializado (Operativo)" : "Estado: Pendiente de inicializar";

            // Regla estricta: Solo se puede inicializar UNA VEZ AL DÍA
            btnInicializarCajero.IsEnabled = !estaInit;
            btnCargarSugeridos.IsEnabled = !estaInit;
            btnLimpiarInit.IsEnabled = !estaInit;

            txtInit200.IsEnabled = !estaInit;
            txtInit100.IsEnabled = !estaInit;
            txtInit50.IsEnabled = !estaInit;
            txtInit20.IsEnabled = !estaInit;
            txtInit10.IsEnabled = !estaInit;
            txtInit5.IsEnabled = !estaInit;
            txtInit1.IsEnabled = !estaInit;

            if (estaInit)
            {
                btnInicializarCajero.Content = "Cajero Inicializado Hoy (Solo 1 vez al día)";
                lblEstadoInit.Visibility = Visibility.Visible;
                lblEstadoInit.Foreground = (System.Windows.Media.Brush)FindResource("AccentSuccessBrush");
                lblEstadoInit.Text = "✓ Apertura completada. Solo se puede inicializar una vez al día. Para añadir fondos use 'Agregar Efectivo'.";

                // Agregar efectivo queda habilitado
                btnProcesarRecarga.IsEnabled = true;
                lblEstadoRecarga.Visibility = Visibility.Collapsed;
            }
            else
            {
                btnInicializarCajero.Content = "Inicializar Cajero (Máx Q10,000)";
                lblEstadoInit.Visibility = Visibility.Collapsed;

                // Agregar efectivo no se permite antes de inicializar
                btnProcesarRecarga.IsEnabled = false;
                lblEstadoRecarga.Visibility = Visibility.Visible;
                lblEstadoRecarga.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoRecarga.Text = "Debe inicializar el cajero primero antes de poder agregar lotes de efectivo.";
            }
        }

        private void BtnReiniciarApertura_Click(object sender, RoutedEventArgs e)
        {
            bool ok = _dal.ReiniciarInicializacionDia();
            if (ok)
            {
                CargarStockBoveda();
                CargarBitacora();
                CargarDashboard();
                CargarValoresSugeridosInit();
                CustomMessageBox.Show("Se ha restablecido el estado del cajero a 'Pendiente de Inicializar'. Ahora puede probar nuevamente la inicialización del día.", "Reinicio de Apertura Exitoso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CalcularTotalInit_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (lblTotalInit == null) return;
            decimal total = CalcularTotalInitInputs();
            lblTotalInit.Text = $"Q {total:N2}";
            lblTotalInit.Foreground = (System.Windows.Media.Brush)FindResource(total > 10000m ? "AccentDangerBrush" : "AccentPrimaryBrush");
        }

        private decimal CalcularTotalInitInputs()
        {
            decimal total = 0;
            total += ParseInt(txtInit200) * 200;
            total += ParseInt(txtInit100) * 100;
            total += ParseInt(txtInit50) * 50;
            total += ParseInt(txtInit20) * 20;
            total += ParseInt(txtInit10) * 10;
            total += ParseInt(txtInit5) * 5;
            total += ParseInt(txtInit1) * 1;
            return total;
        }

        private void BtnCargarSugeridos_Click(object sender, RoutedEventArgs e)
        {
            CargarValoresSugeridosInit();
        }

        private void BtnLimpiarInit_Click(object sender, RoutedEventArgs e)
        {
            txtInit200.Text = "0";
            txtInit100.Text = "0";
            txtInit50.Text = "0";
            txtInit20.Text = "0";
            txtInit10.Text = "0";
            txtInit5.Text = "0";
            txtInit1.Text = "0";
            CalcularTotalInit_TextChanged(this, null!);
        }

        private void CargarValoresSugeridosInit()
        {
            // Distribución sugerida de apertura que suma exactamente Q10,000.00 en las 7 denominaciones
            txtInit200.Text = "20";  // Q 4,000.00
            txtInit100.Text = "30";  // Q 3,000.00
            txtInit50.Text = "20";   // Q 1,000.00
            txtInit20.Text = "40";   // Q   800.00
            txtInit10.Text = "60";   // Q   600.00
            txtInit5.Text = "80";    // Q   400.00
            txtInit1.Text = "200";   // Q   200.00
            CalcularTotalInit_TextChanged(this, null!);
        }

        private void BtnInicializarCajero_Click(object sender, RoutedEventArgs e)
        {
            // Validación 1: Solo se puede inicializar UNA VEZ AL DÍA
            if (_dal.EstaCajeroInicializado())
            {
                CustomMessageBox.Show(
                    "El cajero ya ha sido inicializado para el día de hoy.\n\n" +
                    "Por normativa del proyecto, la apertura e inicialización solo se puede realizar una única vez al día.\n\n" +
                    "Si desea ingresar más efectivo durante el día, utilice la sección '2. Agregar Efectivo (Lote de Billetes)' (hasta Q30,000).", 
                    "Inicialización Ya Realizada", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal total = CalcularTotalInitInputs();

            // Validación 2: Límite máximo Q10,000.00
            if (total > 10000m)
            {
                lblEstadoInit.Visibility = Visibility.Visible;
                lblEstadoInit.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoInit.Text = $"Error: El monto de inicialización no puede ser mayor a Q10,000.00 (Monto actual: Q{total:N2}).";
                CustomMessageBox.Show($"El monto total de inicialización no puede ser mayor a Q10,000.00 (Monto ingresado: Q{total:N2}).", "Límite Excedido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (total <= 0)
            {
                lblEstadoInit.Visibility = Visibility.Visible;
                lblEstadoInit.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoInit.Text = "Error: Debe ingresar una cantidad de billetes válida mayor a cero.";
                return;
            }

            var billetesIniciales = new List<Billete>
            {
                new Billete(200, ParseInt(txtInit200)),
                new Billete(100, ParseInt(txtInit100)),
                new Billete(50, ParseInt(txtInit50)),
                new Billete(20, ParseInt(txtInit20)),
                new Billete(10, ParseInt(txtInit10)),
                new Billete(5, ParseInt(txtInit5)),
                new Billete(1, ParseInt(txtInit1))
            };

            try
            {
                bool ok = _dal.InicializarCajero(billetesIniciales);
                if (ok)
                {
                    CargarStockBoveda();
                    CargarBitacora();
                    CargarDashboard();
                    CustomMessageBox.Show(
                        $"¡Cajero inicializado exitosamente para el día de hoy!\n\n" +
                        $"Monto de apertura: Q{total:N2}\n" +
                        $"El cajero se encuentra operativo para retiros y depósitos.\n" +
                        $"Cualquier recarga adicional de efectivo debe realizarse con 'Agregar Efectivo'.", 
                        "Apertura del Día Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                lblEstadoInit.Visibility = Visibility.Visible;
                lblEstadoInit.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoInit.Text = ex.Message;
                CustomMessageBox.Show(ex.Message, "Error en Inicialización", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CalcularTotalRecarga_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (lblTotalRecarga == null) return;
            decimal total = CalcularTotalRecargaInputs();
            lblTotalRecarga.Text = $"Q {total:N2}";
        }

        private decimal CalcularTotalRecargaInputs()
        {
            decimal total = 0;
            total += ParseInt(txtRecarga200) * 200;
            total += ParseInt(txtRecarga100) * 100;
            total += ParseInt(txtRecarga50) * 50;
            total += ParseInt(txtRecarga20) * 20;
            total += ParseInt(txtRecarga10) * 10;
            total += ParseInt(txtRecarga5) * 5;
            total += ParseInt(txtRecarga1) * 1;
            return total;
        }

        private void BtnProcesarRecarga_Click(object sender, RoutedEventArgs e)
        {
            if (!_dal.EstaCajeroInicializado())
            {
                lblEstadoRecarga.Visibility = Visibility.Visible;
                lblEstadoRecarga.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoRecarga.Text = "El cajero debe ser inicializado antes de poder agregar lotes de efectivo.";
                return;
            }

            decimal totalLote = CalcularTotalRecargaInputs();
            if (totalLote <= 0)
            {
                lblEstadoRecarga.Visibility = Visibility.Visible;
                lblEstadoRecarga.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoRecarga.Text = "Debe ingresar al menos un billete para procesar la recarga.";
                return;
            }

            var billetesRecarga = new List<Billete>
            {
                new Billete(200, ParseInt(txtRecarga200)),
                new Billete(100, ParseInt(txtRecarga100)),
                new Billete(50, ParseInt(txtRecarga50)),
                new Billete(20, ParseInt(txtRecarga20)),
                new Billete(10, ParseInt(txtRecarga10)),
                new Billete(5, ParseInt(txtRecarga5)),
                new Billete(1, ParseInt(txtRecarga1))
            };

            try
            {
                bool ok = _dal.AgregarEfectivoBoveda(billetesRecarga);
                if (ok)
                {
                    lblEstadoRecarga.Visibility = Visibility.Visible;
                    lblEstadoRecarga.Foreground = (System.Windows.Media.Brush)FindResource("AccentSuccessBrush");
                    lblEstadoRecarga.Text = $"Recarga exitosa por Q{totalLote:N2}.";

                    txtRecarga200.Text = "0"; txtRecarga100.Text = "0"; txtRecarga50.Text = "0";
                    txtRecarga20.Text = "0"; txtRecarga10.Text = "0"; txtRecarga5.Text = "0"; txtRecarga1.Text = "0";

                    CargarStockBoveda();
                    CargarBitacora();
                    CargarDashboard();
                    CustomMessageBox.Show($"Se ha agregado el nuevo lote de efectivo por Q{totalLote:N2} a la bóveda.", "Recarga Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                lblEstadoRecarga.Visibility = Visibility.Visible;
                lblEstadoRecarga.Foreground = (System.Windows.Media.Brush)FindResource("AccentDangerBrush");
                lblEstadoRecarga.Text = ex.Message;
            }
        }

        private static int ParseInt(TextBox tb)
        {
            if (tb != null && int.TryParse(tb.Text.Trim(), out int val) && val > 0)
                return val;
            return 0;
        }

        #endregion

        #region =================== 4. AUDITORÍA DE TRANSACCIONES ===================

        private void BtnActualizarTransacciones_Click(object sender, RoutedEventArgs e)
        {
            CargarTransacciones();
        }

        public void CargarTransacciones()
        {
            var transaccionesCSV = _dal.ObtenerTodasLasTransacciones();

            _transacciones.Clear();

            int contador = 1;
            foreach (var t in transaccionesCSV.OrderByDescending(tx => tx.FechaHora))
            {
                _transacciones.Add(new TransaccionItem(
                    $"TX-{contador++:D3}",
                    t.FechaHora.ToString("dd/MM/yyyy HH:mm:ss"),
                    t.NumeroTarjeta,
                    t.TipoMovimiento,
                    $"Q {t.Monto:N2}",
                    string.IsNullOrWhiteSpace(t.Token) ? "N/A" : t.Token,
                    "Completado"
                ));
            }

            lblTotalTransacciones.Text = $"Total de registros: {_transacciones.Count}";
        }

        #endregion

        #region =================== 5. BITÁCORA DEL SISTEMA ===================

        private void BtnActualizarBitacora_Click(object sender, RoutedEventArgs e)
        {
            CargarBitacora();
        }

        public void CargarBitacora()
        {
            var registros = _dal.ObtenerBitacora();
            _bitacora.Clear();

            foreach (var reg in registros)
            {
                _bitacora.Add(reg);
            }

            lblTotalBitacora.Text = $"Total de eventos: {_bitacora.Count}";
        }

        #endregion

        #region =================== 6. RED INTERBANCARIA Y TOKENS ===================

        private async void BtnEscanearRedAdmin_Click(object sender, RoutedEventArgs e)
        {
            await EjecutarEscaneoRedAdminAsync(mostrarDialogo: true);
        }

        private async Task EjecutarEscaneoRedAdminAsync(bool mostrarDialogo)
        {
            try
            {
                if (btnEscanearRedAdmin != null)
                {
                    btnEscanearRedAdmin.IsEnabled = false;
                    btnEscanearRedAdmin.Content = "⏳ Escaneando...";
                }

                var res = await DescubridorRedBancaria.EscanearRedYDetectarBancosAsync(_dal);
                if (lblInfoRedAdmin != null)
                    lblInfoRedAdmin.Text = $"📡 {res.ResumenRedCorto}  |  VPN/Otras: {res.InterfacesSecundarias}";
                if (lblEstadoDeteccionAdmin != null)
                    lblEstadoDeteccionAdmin.Text = res.ResumenDeteccionBancos;

                CargarRedInterbancaria();

                if (mostrarDialogo)
                {
                    string listaBancos = res.HayOtrosBancosConectados
                        ? string.Join("\n", res.BancosDetectados.Select(b => $"  • {b.CodigoBanco} - {b.NombreBanco} ({b.IP}:{b.Puerto})"))
                        : "  • Ningún otro banco activo fue detectado en esta red.";

                    CustomMessageBox.Show(
                        $"Red Conectada: {res.NombreRedSsid} ({res.TipoInterfaz})\n" +
                        $"IP Local: {res.IpLocal} | Subred: {res.SubredCidr} | Gateway: {res.PuertaEnlace}\n" +
                        $"Otras Interfaces: {res.InterfacesSecundarias}\n" +
                        $"Hosts en LAN: {string.Join(", ", res.HostsActivosEnLan)}\n\n" +
                        $"Bancos Detectados ({res.BancosDetectados.Count}):\n{listaBancos}",
                        "Resultado de Escaneo de Red",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch { }
            finally
            {
                if (btnEscanearRedAdmin != null)
                {
                    btnEscanearRedAdmin.Content = "📡 Escanear Red Local";
                    btnEscanearRedAdmin.IsEnabled = true;
                }
            }
        }

        private void BtnActualizarInterbancario_Click(object sender, RoutedEventArgs e)
        {
            CargarRedInterbancaria();
            CargarTransacciones();
            CargarBitacora();
        }

        private void BtnReiniciarCuposBancos_Click(object sender, RoutedEventArgs e)
        {
            _dal.ReiniciarCuposDiariosBancos();
            CargarRedInterbancaria();
            CargarBitacora();
            CustomMessageBox.Show(
                "Los límites diarios de todos los bancos en la red han sido reiniciados a Q 0.00 transferidos hoy.",
                "Cupos Interbancarios Reiniciados",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        public void CargarRedInterbancaria()
        {
            var infoRed = DescubridorRedBancaria.ObtenerInformacionRedActual();
            if (lblInfoRedAdmin != null && lblInfoRedAdmin.Text.Contains("Detectando"))
            {
                lblInfoRedAdmin.Text = $"📡 {infoRed.ResumenRedCorto}  |  VPN/Otras: {infoRed.InterfacesSecundarias}";
            }

            _bancosRed.Clear();
            foreach (var b in _dal.ObtenerBancosRed())
            {
                _bancosRed.Add(new BancoRedVistaItem(b));
            }

            _transferenciasBancarias.Clear();
            foreach (var t in _dal.ObtenerTransferenciasCompartidas())
            {
                _transferenciasBancarias.Add(new TransferenciaBancariaVistaItem(t));
            }
        }

        #endregion

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = EjecutarEscaneoRedAdminAsync(mostrarDialogo: false);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Evento al cerrar la ventana
        }
    }

    public class BancoRedVistaItem
    {
        public string CodigoBanco { get; set; }
        public string NombreBanco { get; set; }
        public string EndpointTexto { get; set; }
        public string EstadoRedTexto { get; set; }
        public string RolTexto { get; set; }
        public string LimiteFormat { get; set; }
        public string TransferidoFormat { get; set; }
        public string DisponibleFormat { get; set; }

        public BancoRedVistaItem(BancoRedInfo b)
        {
            CodigoBanco = b.CodigoBanco;
            NombreBanco = b.NombreBanco;
            EndpointTexto = b.EndpointTexto;
            EstadoRedTexto = b.EstadoRedTexto;
            RolTexto = b.RolTexto;
            LimiteFormat = $"Q {b.LimiteDiario:N2}";
            TransferidoFormat = $"Q {b.TransferidoHoy:N2}";
            DisponibleFormat = $"Q {b.DisponibleHoy:N2}";
        }
    }

    public class TransferenciaBancariaVistaItem
    {
        public string TokenTransferencia { get; set; }
        public string FechaFormat { get; set; }
        public string TipoTransferencia { get; set; }
        public string OrigenFormat { get; set; }
        public string DestinoFormat { get; set; }
        public string MontoFormat { get; set; }
        public string CuposFormat { get; set; }

        public TransferenciaBancariaVistaItem(RegistroTransferenciaBancaria r)
        {
            TokenTransferencia = r.TokenTransferencia;
            FechaFormat = r.FechaHora.ToString("dd/MM/yyyy HH:mm:ss");
            TipoTransferencia = r.TipoTransferencia;
            OrigenFormat = $"{r.BancoOrigen} | {r.CuentaOrigen} ({r.NombreOrigen})";
            DestinoFormat = $"{r.BancoDestino} | {r.CuentaDestino} ({r.NombreDestino})";
            MontoFormat = $"Q {r.Monto:N2}";
            CuposFormat = $"Nuestro: Q {r.CupoRestanteBancoOrigen:N2} | Destino: Q {r.CupoRestanteBancoDestino:N2}";
        }
    }

    public class TransaccionItem
    {
        public string Id { get; set; }
        public string FechaHora { get; set; }
        public string Usuario { get; set; }
        public string TipoOperacion { get; set; }
        public string MontoFormateado { get; set; }
        public string Token { get; set; }
        public string Estado { get; set; }

        public TransaccionItem(string id, string fechaHora, string usuario, string tipoOperacion, string montoFormateado, string token, string estado)
        {
            Id = id;
            FechaHora = fechaHora;
            Usuario = usuario;
            TipoOperacion = tipoOperacion;
            MontoFormateado = montoFormateado;
            Token = token;
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