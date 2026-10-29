using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cajero.Hardware;
using Cajero.Protocolo;
using Cajero.Cliente.Comunicaciones;

namespace Cajero.Cliente.UI
{
    public class FormCajeroATM : Form
    {
        private readonly ConfiguracionCliente _config;
        private readonly ClienteTcp _clienteTcp;
        private LectorRFID? _lectorRfid;

        // Estado de Sesión del Usuario
        private string? _tarjetaActual;
        private string? _numeroCuentaActual;
        private string? _codigoBancoActual;
        private string? _nombreBancoActual;
        private string? _nombreUsuarioActual;
        private decimal _saldoActual;
        private decimal _limiteDiarioActual;
        private decimal _disponibleHoyActual;
        private string? _tokenSesion;

        // Temporizador de inactividad de 60 segundos
        private System.Windows.Forms.Timer timerInactividad = null!;
        private int _segundosInactividadRestantes = 60;
        private ProgressBar pbInactividad = null!;
        private Label lblContadorInactividad = null!;

        // Controles de Encabezado
        private Panel pnlHeader = null!;
        private Label lblTituloCajero = null!;
        private Label lblEstadoConexion = null!;
        private Label lblHoraActual = null!;
        private System.Windows.Forms.Timer timerReloj = null!;

        // Contenedor principal de pantallas
        private Panel pnlContenedorPantallas = null!;

        // Pantallas
        private Panel pnlBienvenida = null!;
        private Panel pnlFueraServicioOverlay = null!;
        private Panel pnlPin = null!;
        private Panel pnlToken = null!;
        private Panel pnlMenu = null!;
        private Panel pnlRetiro = null!;
        private Panel pnlDeposito = null!;
        private Panel pnlTransferenciaInterna = null!;
        private Panel pnlTransferenciaInterbancaria = null!;
        private Panel pnlCambioPin = null!;
        private Panel pnlConsultaSaldo = null!;
        private Panel pnlUltimasTx = null!;
        private Panel pnlComprobante = null!;

        // Controles de entrada
        private TextBox txtTarjetaManual = null!;
        private TextBox txtPinEntrada = null!;
        private TextBox txtTokenEntrada = null!;

        // Retiro
        private NumericUpDown numMontoRetiro = null!;
        private readonly Dictionary<int, NumericUpDown> _inputsRetiroDesglose = new();
        private Label lblTotalDesgloseRetiro = null!;

        // Depósito
        private readonly Dictionary<int, NumericUpDown> _inputsDepositoDesglose = new();
        private Label lblTotalDesgloseDeposito = null!;

        // Transferencia Interna (Entre Usuarios del mismo banco)
        private TextBox txtCuentaDestinoInterna = null!;
        private Label lblEstadoVerificacionInterna = null!;
        private NumericUpDown numMontoTransferenciaInterna = null!;
        private TextBox txtConceptoTransferenciaInterna = null!;
        private Button btnConfirmarTransferenciaInterna = null!;
        private bool _cuentaInternaVerificada = false;
        private string _titularVerificadoInterno = "";
        private string _cuentaVerificadaInterna = "";

        // Transferencia Interbancaria (Entre Diferentes Bancos)
        private ComboBox cbBancoDestino = null!;
        private Label lblCuposCoordinadosBancos = null!;
        private TextBox txtCuentaDestinoInterbancaria = null!;
        private Label lblEstadoVerificacionInterbancaria = null!;
        private NumericUpDown numMontoTransferenciaInterbancaria = null!;
        private TextBox txtConceptoTransferenciaInterbancaria = null!;
        private Button btnConfirmarTransferenciaInterbancaria = null!;
        private readonly List<ItemBancoProtocolo> _bancosRed = new();
        private bool _cuentaInterbancariaVerificada = false;
        private string _titularVerificadoInterbancario = "";
        private string _cuentaVerificadaInterbancaria = "";
        private string _codigoBancoVerificado = "";
        private string _nombreBancoVerificado = "";

        // Cambio PIN
        private TextBox txtPinActual = null!;
        private TextBox txtPinNuevo = null!;
        private TextBox txtPinConfirmar = null!;

        // Consulta Saldo
        private Label lblInfoSaldo = null!;

        // Últimas Transacciones
        private DataGridView dgvUltimasTx = null!;

        // Comprobante
        private Label lblTextoComprobante = null!;

        public FormCajeroATM()
        {
            _config = ConfiguracionCliente.Cargar();
            _clienteTcp = new ClienteTcp(_config);

            InicializarComponentes();
            InicializarLectorRfid();
            ConectarEventosTcp();

            _clienteTcp.Iniciar();
        }

        private void InicializarLectorRfid()
        {
            try
            {
                _lectorRfid = new LectorRFID(_config.PuertoCOM, _config.BaudRate);
                _lectorRfid.TarjetaLeida += (uid) =>
                {
                    if (this.InvokeRequired)
                    {
                        this.BeginInvoke(new Action(() => ProcesarLecturaRfid(uid)));
                    }
                    else
                    {
                        ProcesarLecturaRfid(uid);
                    }
                };
                _lectorRfid.IniciarEscucha();
            }
            catch { }
        }

        private void ProcesarLecturaRfid(string uid)
        {
            if (pnlBienvenida.Visible && !pnlFueraServicioOverlay.Visible)
            {
                txtTarjetaManual.Text = uid.Trim();
                // Avanzar automáticamente a pedir PIN
                AvanzarAPantallaPin(uid.Trim());
            }
        }

        private void ConectarEventosTcp()
        {
            _clienteTcp.EstadoConexionCambiado += (conectado) =>
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => ActualizarEstadoConexionVisual(conectado)));
                }
                else
                {
                    ActualizarEstadoConexionVisual(conectado);
                }
            };

            _clienteTcp.ComandoRecibido += (comando, parametros) =>
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => ProcesarComandoRemoto(comando, parametros)));
                }
                else
                {
                    ProcesarComandoRemoto(comando, parametros);
                }
            };
        }

        private void ActualizarEstadoConexionVisual(bool conectado)
        {
            if (conectado && _clienteTcp.EstaHabilitado)
            {
                lblEstadoConexion.Text = "🟢 EN LÍNEA";
                lblEstadoConexion.ForeColor = Color.FromArgb(46, 213, 115);
                pnlFueraServicioOverlay.Visible = false;
            }
            else
            {
                lblEstadoConexion.Text = "🔴 FUERA DE SERVICIO";
                lblEstadoConexion.ForeColor = Color.FromArgb(255, 71, 87);
                pnlFueraServicioOverlay.Visible = true;
                pnlFueraServicioOverlay.BringToFront();
            }
        }

        private void ProcesarComandoRemoto(string comando, Dictionary<string, string> parametros)
        {
            if (comando == TiposComando.FueraDeServicio)
            {
                ActualizarEstadoConexionVisual(false);
            }
            else if (comando == TiposComando.Habilitar)
            {
                ActualizarEstadoConexionVisual(true);
            }
            else if (comando == TiposComando.Mensaje)
            {
                string texto = parametros.TryGetValue("texto", out var t) ? t : "Aviso administrativo";
                MessageBox.Show(texto, "📢 Mensaje del Banco", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void InicializarComponentes()
        {
            this.Text = $"Cajero Automático - {_config.CodigoCajero}";
            this.Size = new Size(1000, 750);
            this.MinimumSize = new Size(950, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(16, 20, 28);
            this.ForeColor = Color.White;

            // ================= ENCABEZADO ATM =================
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(24, 30, 42)
            };

            lblTituloCajero = new Label
            {
                Text = $"🏦 BANCO NACIONAL  |  {_config.CodigoCajero}",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Left = 25,
                Top = 18,
                Width = 420
            };
            pnlHeader.Controls.Add(lblTituloCajero);

            lblEstadoConexion = new Label
            {
                Text = "🟡 CONECTANDO...",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 177, 66),
                Left = 470,
                Top = 22,
                Width = 240
            };
            pnlHeader.Controls.Add(lblEstadoConexion);

            lblHoraActual = new Label
            {
                Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                Font = new Font("Consolas", 11f),
                ForeColor = Color.FromArgb(200, 215, 230),
                Left = 740,
                Top = 22,
                Width = 220,
                TextAlign = ContentAlignment.MiddleRight
            };
            pnlHeader.Controls.Add(lblHoraActual);

            this.Controls.Add(pnlHeader);

            timerReloj = new System.Windows.Forms.Timer { Interval = 1000 };
            timerReloj.Tick += (s, e) => lblHoraActual.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            timerReloj.Start();

            // Barra inferior de Inactividad (60 segundos)
            var pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 35, BackColor = Color.FromArgb(20, 24, 34) };
            lblContadorInactividad = new Label
            {
                Text = "Sesión activa (60s)",
                Left = 20,
                Top = 8,
                Width = 180,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.Gray,
                Visible = false
            };
            pbInactividad = new ProgressBar
            {
                Left = 210,
                Top = 10,
                Width = 750,
                Height = 12,
                Maximum = 60,
                Value = 60,
                Visible = false
            };
            pnlFooter.Controls.Add(lblContadorInactividad);
            pnlFooter.Controls.Add(pbInactividad);
            this.Controls.Add(pnlFooter);

            timerInactividad = new System.Windows.Forms.Timer { Interval = 1000 };
            timerInactividad.Tick += TimerInactividad_Tick;

            // ================= CONTENEDOR =================
            pnlContenedorPantallas = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 20, 28)
            };
            this.Controls.Add(pnlContenedorPantallas);
            pnlContenedorPantallas.BringToFront();

            // Construir Pantallas
            ConstruirPantallaFueraServicio();
            ConstruirPantallaBienvenida();
            ConstruirPantallaPin();
            ConstruirPantallaToken();
            ConstruirPantallaMenuPrincipal();
            ConstruirPantallaRetiro();
            ConstruirPantallaDeposito();
            ConstruirPantallaTransferenciaInterna();
            ConstruirPantallaTransferenciaInterbancaria();
            ConstruirPantallaCambioPin();
            ConstruirPantallaConsultaSaldo();
            ConstruirPantallaUltimasTx();
            ConstruirPantallaComprobante();

            MostrarPantalla(pnlBienvenida);
        }

        private void MostrarPantalla(Panel pantalla)
        {
            pnlContenedorPantallas.SuspendLayout();
            pnlBienvenida.Visible = false;
            pnlPin.Visible = false;
            pnlToken.Visible = false;
            pnlMenu.Visible = false;
            pnlRetiro.Visible = false;
            pnlDeposito.Visible = false;
            pnlTransferenciaInterna.Visible = false;
            pnlTransferenciaInterbancaria.Visible = false;
            pnlCambioPin.Visible = false;
            pnlConsultaSaldo.Visible = false;
            pnlUltimasTx.Visible = false;
            pnlComprobante.Visible = false;

            pantalla.Visible = true;
            pantalla.BringToFront();
            pnlContenedorPantallas.ResumeLayout();

            ReiniciarInactividad();
        }

        private void ReiniciarInactividad()
        {
            _segundosInactividadRestantes = 60;
            pbInactividad.Value = 60;
            if (_tarjetaActual != null)
            {
                lblContadorInactividad.Visible = true;
                pbInactividad.Visible = true;
                timerInactividad.Start();
            }
            else
            {
                lblContadorInactividad.Visible = false;
                pbInactividad.Visible = false;
                timerInactividad.Stop();
            }
        }

        private void TimerInactividad_Tick(object? sender, EventArgs e)
        {
            _segundosInactividadRestantes--;
            if (_segundosInactividadRestantes <= 0)
            {
                timerInactividad.Stop();
                CerrarSesion("Sesión cerrada automáticamente por 60 segundos de inactividad.");
            }
            else
            {
                lblContadorInactividad.Text = $"Inactividad: {_segundosInactividadRestantes}s";
                pbInactividad.Value = Math.Clamp(_segundosInactividadRestantes, 0, 60);
            }
        }

        private void CerrarSesion(string? motivo = null)
        {
            if (_tarjetaActual != null)
            {
                _ = _clienteTcp.EnviarSinEsperarAsync(new SolicitudLogout
                {
                    Cajero = _config.CodigoCajero,
                    NumeroTarjeta = _tarjetaActual
                });
            }

            _tarjetaActual = null;
            _numeroCuentaActual = null;
            _nombreUsuarioActual = null;
            _saldoActual = 0;
            _tokenSesion = null;

            ReiniciarInactividad();
            txtTarjetaManual.Text = "";
            txtPinEntrada.Text = "";
            txtTokenEntrada.Text = "";

            MostrarPantalla(pnlBienvenida);

            if (!string.IsNullOrEmpty(motivo))
            {
                MessageBox.Show(motivo, "Sesión Finalizada", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        #region Pantalla: Fuera de Servicio Overlay

        private void ConstruirPantallaFueraServicio()
        {
            pnlFueraServicioOverlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(220, 20, 24, 34),
                Visible = false
            };

            var lblIcon = new Label
            {
                Text = "⛔",
                Font = new Font("Segoe UI", 60f),
                ForeColor = Color.FromArgb(255, 71, 87),
                Top = 150,
                Left = 0,
                Width = 1000,
                Height = 110,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblMsg1 = new Label
            {
                Text = "CAJERO FUERA DE SERVICIO",
                Font = new Font("Segoe UI", 24f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 71, 87),
                Top = 270,
                Left = 0,
                Width = 1000,
                Height = 50,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblMsg2 = new Label
            {
                Text = "En este momento no hay conexión con el servidor central.\nReintentando enlace automáticamente cada 5 segundos...",
                Font = new Font("Segoe UI", 12f),
                ForeColor = Color.FromArgb(190, 205, 225),
                Top = 330,
                Left = 0,
                Width = 1000,
                Height = 60,
                TextAlign = ContentAlignment.MiddleCenter
            };

            pnlFueraServicioOverlay.Controls.AddRange(new Control[] { lblIcon, lblMsg1, lblMsg2 });
            pnlContenedorPantallas.Controls.Add(pnlFueraServicioOverlay);
        }

        #endregion

        #region 1. Pantalla: Bienvenida / Lectura Tarjeta

        private void ConstruirPantallaBienvenida()
        {
            pnlBienvenida = new Panel { Dock = DockStyle.Fill };

            var lblBienv = new Label
            {
                Text = "¡BIENVENIDO!",
                Font = new Font("Segoe UI", 26f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Top = 60,
                Left = 0,
                Width = 1000,
                Height = 55,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblInst = new Label
            {
                Text = "Acerque su tarjeta o llavero RFID al lector USB (ESP32 en " + _config.PuertoCOM + ")\no ingrese manualmente los 16 dígitos:",
                Font = new Font("Segoe UI", 12f),
                ForeColor = Color.FromArgb(210, 220, 235),
                Top = 130,
                Left = 0,
                Width = 1000,
                Height = 55,
                TextAlign = ContentAlignment.MiddleCenter
            };

            txtTarjetaManual = new TextBox
            {
                Font = new Font("Consolas", 18f, FontStyle.Bold),
                BackColor = Color.FromArgb(32, 40, 56),
                ForeColor = Color.FromArgb(46, 213, 115),
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 16,
                Left = 320,
                Top = 210,
                Width = 360,
                Text = "1111222233334444"
            };

            var btnContinuarManual = new Button
            {
                Text = "Ingresar con Tarjeta ➡️",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 350,
                Top = 275,
                Width = 300,
                Height = 48,
                Cursor = Cursors.Hand
            };
            btnContinuarManual.FlatAppearance.BorderSize = 0;
            btnContinuarManual.Click += (s, e) =>
            {
                if (txtTarjetaManual.Text.Trim().Length == 0) return;
                AvanzarAPantallaPin(txtTarjetaManual.Text.Trim());
            };

            // Botones de prueba rápida (1-click para demostración)
            var pnlDemo = new GroupBox
            {
                Text = "🚀 Accesos Rápidos de Prueba (Tarjetas del Sistema)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 177, 66),
                Left = 150,
                Top = 360,
                Width = 700,
                Height = 160
            };

            string[,] tarjetasDemo = {
                { "Carlos Méndez (1111222233334444)", "1111222233334444" },
                { "Ana López (2222333344445555)", "2222333344445555" },
                { "Juan Pérez (3333444455556666)", "3333444455556666" },
                { "María Gómez (4444555566667777)", "4444555566667777" },
                { "Llavero RFID Azul (99A90BA4)", "99A90BA4" },
                { "Tarjeta Blanca RFID (A77B2F63)", "A77B2F63" }
            };

            for (int i = 0; i < 6; i++)
            {
                int col = i % 2;
                int row = i / 2;
                string label = tarjetasDemo[i, 0];
                string val = tarjetasDemo[i, 1];

                var btnD = new Button
                {
                    Text = label,
                    Font = new Font("Segoe UI", 8.5f),
                    BackColor = Color.FromArgb(40, 50, 70),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Left = 20 + (col * 340),
                    Top = 28 + (row * 40),
                    Width = 320,
                    Height = 32,
                    Cursor = Cursors.Hand
                };
                btnD.FlatAppearance.BorderSize = 0;
                btnD.Click += (s, e) =>
                {
                    txtTarjetaManual.Text = val;
                    AvanzarAPantallaPin(val);
                };
                pnlDemo.Controls.Add(btnD);
            }

            pnlBienvenida.Controls.AddRange(new Control[] { lblBienv, lblInst, txtTarjetaManual, btnContinuarManual, pnlDemo });
            pnlContenedorPantallas.Controls.Add(pnlBienvenida);
        }

        private void AvanzarAPantallaPin(string tarjeta)
        {
            _tarjetaActual = tarjeta;
            txtPinEntrada.Text = "";
            MostrarPantalla(pnlPin);
        }

        #endregion

        #region 2. Pantalla: Ingreso PIN

        private void ConstruirPantallaPin()
        {
            pnlPin = new Panel { Dock = DockStyle.Fill };

            var lblT = new Label
            {
                Text = "INGRESE SU PIN DE SEGURIDAD",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Top = 40,
                Left = 0,
                Width = 1000,
                Height = 45,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblSub = new Label
            {
                Text = "El PIN debe tener 4 dígitos numéricos:",
                Font = new Font("Segoe UI", 11f),
                ForeColor = Color.FromArgb(200, 215, 230),
                Top = 95,
                Left = 0,
                Width = 1000,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter
            };

            txtPinEntrada = new TextBox
            {
                Font = new Font("Segoe UI", 24f, FontStyle.Bold),
                BackColor = Color.FromArgb(32, 40, 56),
                ForeColor = Color.FromArgb(46, 213, 115),
                TextAlign = HorizontalAlignment.Center,
                PasswordChar = '●',
                MaxLength = 4,
                Left = 380,
                Top = 135,
                Width = 240
            };

            // Teclado Numérico Touch
            var pnlTeclado = CrearTecladoTouch((digito) =>
            {
                if (txtPinEntrada.Text.Length < 4) txtPinEntrada.Text += digito;
            }, () =>
            {
                if (txtPinEntrada.Text.Length > 0)
                    txtPinEntrada.Text = txtPinEntrada.Text[..^1];
            }, async () =>
            {
                await EnviarLoginAsync();
            }, () => CerrarSesion());
            pnlTeclado.Left = 340;
            pnlTeclado.Top = 205;

            pnlPin.Controls.AddRange(new Control[] { lblT, lblSub, txtPinEntrada, pnlTeclado });
            pnlContenedorPantallas.Controls.Add(pnlPin);
        }

        private async Task EnviarLoginAsync()
        {
            string pin = txtPinEntrada.Text.Trim();
            if (pin.Length != 4)
            {
                MessageBox.Show("El PIN debe constar exactamente de 4 dígitos.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sol = new SolicitudLogin
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual!,
                Pin = pin
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(sol);
            if (resp == null)
            {
                MessageBox.Show("Sin respuesta del servidor. Intente más tarde.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (resp.Codigo == CodigosRespuesta.Aprobado)
            {
                // Éxito: el servidor generó el token y lo mostró en la ventana del servidor
                txtTokenEntrada.Text = "";
                MostrarPantalla(pnlToken);
            }
            else
            {
                MessageBox.Show(resp.Mensaje, "Acceso Denegado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPinEntrada.Text = "";
                if (resp.Codigo == CodigosRespuesta.TarjetaBloqueadaOInactiva)
                {
                    CerrarSesion();
                }
            }
        }

        #endregion

        #region 3. Pantalla: Ingreso Token

        private void ConstruirPantallaToken()
        {
            pnlToken = new Panel { Dock = DockStyle.Fill };

            var lblT = new Label
            {
                Text = "📱 TOKEN DE SEGURIDAD MÓVIL",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Top = 40,
                Left = 0,
                Width = 1000,
                Height = 45,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblSub = new Label
            {
                Text = "Se ha enviado un código de 6 dígitos a su teléfono móvil (Ventana del Servidor).\nIngrese el token para acceder a su cuenta:",
                Font = new Font("Segoe UI", 10.5f),
                ForeColor = Color.FromArgb(200, 215, 230),
                Top = 95,
                Left = 0,
                Width = 1000,
                Height = 45,
                TextAlign = ContentAlignment.MiddleCenter
            };

            txtTokenEntrada = new TextBox
            {
                Font = new Font("Segoe UI", 24f, FontStyle.Bold),
                BackColor = Color.FromArgb(32, 40, 56),
                ForeColor = Color.FromArgb(0, 200, 255),
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 6,
                Left = 360,
                Top = 150,
                Width = 280
            };

            var pnlTeclado = CrearTecladoTouch((digito) =>
            {
                if (txtTokenEntrada.Text.Length < 6) txtTokenEntrada.Text += digito;
            }, () =>
            {
                if (txtTokenEntrada.Text.Length > 0)
                    txtTokenEntrada.Text = txtTokenEntrada.Text[..^1];
            }, async () =>
            {
                await EnviarValidarTokenAsync();
            }, () => CerrarSesion());
            pnlTeclado.Left = 340;
            pnlTeclado.Top = 220;

            pnlToken.Controls.AddRange(new Control[] { lblT, lblSub, txtTokenEntrada, pnlTeclado });
            pnlContenedorPantallas.Controls.Add(pnlToken);
        }

        private async Task EnviarValidarTokenAsync()
        {
            string token = txtTokenEntrada.Text.Trim();
            if (token.Length != 6)
            {
                MessageBox.Show("El token debe tener 6 dígitos.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sol = new SolicitudValidarToken
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual!,
                Token = token
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(sol);
            if (resp == null)
            {
                MessageBox.Show("Sin respuesta del servidor.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (resp.Codigo == CodigosRespuesta.Aprobado)
            {
                if (resp.Datos is JsonElement je)
                {
                    _nombreUsuarioActual = je.GetProperty("nombreUsuario").GetString();
                    _saldoActual = je.GetProperty("saldo").GetDecimal();
                    _limiteDiarioActual = je.GetProperty("limiteDiario").GetDecimal();
                    _disponibleHoyActual = je.GetProperty("disponibleHoy").GetDecimal();
                    _tokenSesion = je.GetProperty("tokenSesion").GetString();
                    if (je.TryGetProperty("numeroCuenta", out var pCta)) _numeroCuentaActual = pCta.GetString();
                    if (je.TryGetProperty("codigoBanco", out var pCodB)) _codigoBancoActual = pCodB.GetString();
                    if (je.TryGetProperty("nombreBanco", out var pNomB)) _nombreBancoActual = pNomB.GetString();
                }

                ActualizarMenuPrincipal();
                MostrarPantalla(pnlMenu);
            }
            else
            {
                MessageBox.Show(resp.Mensaje, "Token Inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTokenEntrada.Text = "";
            }
        }

        #endregion

        #region 4. Pantalla: Menú Principal

        private Label lblBienvenidoUsuario = null!;
        private Label lblResumenSaldoMenu = null!;

        private void ConstruirPantallaMenuPrincipal()
        {
            pnlMenu = new Panel { Dock = DockStyle.Fill };

            lblBienvenidoUsuario = new Label
            {
                Text = "Bienvenido/a",
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Top = 25,
                Left = 0,
                Width = 1000,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlMenu.Controls.Add(lblBienvenidoUsuario);

            lblResumenSaldoMenu = new Label
            {
                Text = "Saldo: Q 0.00 | Disponible Hoy: Q 0.00",
                Font = new Font("Segoe UI", 11.5f),
                ForeColor = Color.FromArgb(46, 213, 115),
                Top = 70,
                Left = 0,
                Width = 1000,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlMenu.Controls.Add(lblResumenSaldoMenu);

            // Botones de Operación ATM (4 filas x 2 columnas)
            int btnW = 350;
            int btnH = 60;

            // Fila 1: Retiro y Depósito
            CrearBotonMenuAtm("💵 RETIRO DE EFECTIVO", Color.FromArgb(0, 122, 255), 120, 125, btnW, btnH, () => MostrarPantalla(pnlRetiro));
            CrearBotonMenuAtm("➕ DEPÓSITO DE EFECTIVO", Color.FromArgb(46, 213, 115), 530, 125, btnW, btnH, () => MostrarPantalla(pnlDeposito));

            // Fila 2: Transferencias entre Usuarios y Transferencias entre Diferentes Bancos
            CrearBotonMenuAtm("🔁 TRANSFERENCIA ENTRE USUARIOS", Color.FromArgb(0, 168, 255), 120, 205, btnW, btnH, () => AbrirPantallaTransferenciaInterna());
            CrearBotonMenuAtm("🏦 TRANSFERENCIA INTERBANCARIA", Color.FromArgb(255, 159, 26), 530, 205, btnW, btnH, async () => await AbrirPantallaTransferenciaInterbancariaAsync());

            // Fila 3: Consulta de Saldo y Últimas Transacciones
            CrearBotonMenuAtm("📊 CONSULTA DE SALDO", Color.FromArgb(155, 89, 182), 120, 285, btnW, btnH, async () => await ConsultarSaldoServidorAsync());
            CrearBotonMenuAtm("📋 ÚLTIMAS TRANSACCIONES", Color.FromArgb(241, 196, 15), 530, 285, btnW, btnH, async () => await ConsultarUltimasTxAsync());

            // Fila 4: Cambio de PIN y Salir
            CrearBotonMenuAtm("🔑 CAMBIO DE PIN", Color.FromArgb(230, 126, 34), 120, 365, btnW, btnH, () => MostrarPantalla(pnlCambioPin));
            CrearBotonMenuAtm("🚪 FINALIZAR / SALIR", Color.FromArgb(231, 76, 60), 530, 365, btnW, btnH, () => CerrarSesion("Gracias por usar nuestros servicios."));

            pnlContenedorPantallas.Controls.Add(pnlMenu);
        }

        private void ActualizarMenuPrincipal()
        {
            string infoCuenta = !string.IsNullOrWhiteSpace(_numeroCuentaActual) ? $"  |  Cuenta No: {_numeroCuentaActual}" : "";
            lblBienvenidoUsuario.Text = $"Bienvenido/a, {_nombreUsuarioActual}";
            lblResumenSaldoMenu.Text = $"Saldo Actual: Q {_saldoActual:N2}{infoCuenta}  |  Disponible Retiro Hoy: Q {_disponibleHoyActual:N2}";
        }

        private void CrearBotonMenuAtm(string texto, Color color, int x, int y, int w, int h, Action accion)
        {
            var btn = new Button
            {
                Text = texto,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = x,
                Top = y,
                Width = w,
                Height = h,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => accion();
            pnlMenu.Controls.Add(btn);
        }

        #endregion

        #region 5. Pantalla: Retiro con Desglose y Desglose Alternativo (Código 92)

        private void ConstruirPantallaRetiro()
        {
            pnlRetiro = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            var lblT = new Label
            {
                Text = "💵 RETIRO DE EFECTIVO",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Top = 20,
                Left = 0,
                Width = 1000,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlRetiro.Controls.Add(lblT);

            var lblM = new Label { Text = "Monto Solicitado (Q):", Left = 120, Top = 75, Width = 180, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
            numMontoRetiro = new NumericUpDown
            {
                Left = 310,
                Top = 72,
                Width = 180,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Maximum = 10000,
                Value = 100,
                Increment = 1
            };
            numMontoRetiro.ValueChanged += (s, e) => ValidarSumaDesgloseRetiro();
            pnlRetiro.Controls.Add(lblM);
            pnlRetiro.Controls.Add(numMontoRetiro);

            // Botones de montos rápidos
            int[] montosRapidos = { 100, 200, 300, 500, 1000 };
            int mx = 510;
            foreach (var mr in montosRapidos)
            {
                var btnM = new Button
                {
                    Text = $"Q {mr}",
                    Left = mx,
                    Top = 70,
                    Width = 80,
                    Height = 35,
                    BackColor = Color.FromArgb(40, 50, 70),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat
                };
                int val = mr;
                btnM.Click += (s, e) =>
                {
                    numMontoRetiro.Value = val;
                    CalcularDesgloseAutomaticoRetiro(val);
                };
                pnlRetiro.Controls.Add(btnM);
                mx += 90;
            }

            var grpDesglose = new GroupBox
            {
                Text = "Desglose por Denominación (debe coincidir exactamente con el monto):",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 215, 230),
                Left = 120,
                Top = 120,
                Width = 760,
                Height = 280
            };

            int[] denoms = { 200, 100, 50, 20, 10, 5, 1 };
            int dy = 30;
            foreach (var d in denoms)
            {
                var lblD = new Label { Text = $"Billetes de Q {d}:", Left = 30, Top = dy + 3, Width = 150, Font = new Font("Segoe UI", 9.5f) };
                var num = new NumericUpDown
                {
                    Left = 200,
                    Top = dy,
                    Width = 120,
                    Font = new Font("Segoe UI", 10f),
                    Minimum = 0,
                    Maximum = 500,
                    Value = 0
                };
                num.ValueChanged += (s, e) => ValidarSumaDesgloseRetiro();
                _inputsRetiroDesglose[d] = num;

                grpDesglose.Controls.Add(lblD);
                grpDesglose.Controls.Add(num);
                dy += 34;
            }

            lblTotalDesgloseRetiro = new Label
            {
                Text = "Suma del Desglose: Q 0.00",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                Left = 380,
                Top = 130,
                Width = 350,
                Height = 40
            };
            grpDesglose.Controls.Add(lblTotalDesgloseRetiro);

            var btnAutoDesglose = new Button
            {
                Text = "⚡ Calcular Desglose Automático",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 180, 255),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 380,
                Top = 180,
                Width = 320,
                Height = 38
            };
            btnAutoDesglose.Click += (s, e) => CalcularDesgloseAutomaticoRetiro((int)numMontoRetiro.Value);
            grpDesglose.Controls.Add(btnAutoDesglose);

            pnlRetiro.Controls.Add(grpDesglose);

            var btnConfirmarRetiro = new Button
            {
                Text = "Confirmar Retiro ➡️",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 280,
                Top = 420,
                Width = 220,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnConfirmarRetiro.Click += async (s, e) => await EjecutarRetiroAsync();
            pnlRetiro.Controls.Add(btnConfirmarRetiro);

            var btnVolver = new Button
            {
                Text = "⬅️ Regresar al Menú",
                Font = new Font("Segoe UI", 11f),
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 520,
                Top = 420,
                Width = 200,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnVolver.Click += (s, e) => MostrarPantalla(pnlMenu);
            pnlRetiro.Controls.Add(btnVolver);

            pnlContenedorPantallas.Controls.Add(pnlRetiro);
        }

        private void CalcularDesgloseAutomaticoRetiro(int monto)
        {
            int restante = monto;
            int[] denoms = { 200, 100, 50, 20, 10, 5, 1 };

            foreach (var d in denoms)
            {
                int cant = restante / d;
                _inputsRetiroDesglose[d].Value = cant;
                restante %= d;
            }
            ValidarSumaDesgloseRetiro();
        }

        private decimal ValidarSumaDesgloseRetiro()
        {
            decimal suma = 0;
            foreach (var kvp in _inputsRetiroDesglose)
            {
                suma += kvp.Key * kvp.Value.Value;
            }

            lblTotalDesgloseRetiro.Text = $"Suma del Desglose: Q {suma:N2}";
            if (suma == numMontoRetiro.Value)
            {
                lblTotalDesgloseRetiro.ForeColor = Color.FromArgb(46, 213, 115);
            }
            else
            {
                lblTotalDesgloseRetiro.ForeColor = Color.FromArgb(255, 71, 87);
            }
            return suma;
        }

        private async Task EjecutarRetiroAsync()
        {
            decimal monto = numMontoRetiro.Value;
            decimal suma = ValidarSumaDesgloseRetiro();

            if (suma != monto)
            {
                MessageBox.Show("El desglose de billetes no coincide con el monto total solicitado.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var desglose = new Dictionary<string, int>();
            foreach (var kvp in _inputsRetiroDesglose)
            {
                if (kvp.Value.Value > 0) desglose[kvp.Key.ToString()] = (int)kvp.Value.Value;
            }

            var sol = new SolicitudRetiro
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual!,
                Sesion = _tokenSesion ?? "",
                Monto = monto,
                Desglose = desglose
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(sol);
            if (resp == null)
            {
                MessageBox.Show("Tiempo de espera agotado. No se completó la operación.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (resp.Codigo == CodigosRespuesta.Aprobado)
            {
                decimal nuevoSaldo = 0;
                int idTx = 0;
                if (resp.Datos is JsonElement je)
                {
                    nuevoSaldo = je.GetProperty("saldo").GetDecimal();
                    idTx = je.GetProperty("idTransaccion").GetInt32();
                    _disponibleHoyActual = je.GetProperty("disponibleHoy").GetDecimal();
                }
                _saldoActual = nuevoSaldo;
                ActualizarMenuPrincipal();

                MostrarComprobante("RETIRO DE EFECTIVO", idTx, monto, desglose, nuevoSaldo);
            }
            else if (resp.Codigo == CodigosRespuesta.BilletesInsuficientes)
            {
                Dictionary<string, int>? alt = null;
                if (resp.Datos is JsonElement je && je.TryGetProperty("desgloseAlternativo", out var jeAlt) && jeAlt.ValueKind == JsonValueKind.Object)
                {
                    alt = JsonSerializer.Deserialize<Dictionary<string, int>>(jeAlt.GetRawText());
                }

                if (alt != null && alt.Count > 0)
                {
                    string desgloseTexto = string.Join("\n", alt.Where(x => x.Value > 0).Select(x => $"  • {x.Value} billete(s) de Q {x.Key}"));
                    var dlg = MessageBox.Show(
                        $"El cajero no tiene los billetes exactos solicitados, pero puede entregarle el siguiente desglose alternativo:\n\n{desgloseTexto}\n\n¿Desea ACEPTAR este desglose alternativo o CANCELAR la operación?",
                        "Propuesta de Desglose Alternativo (Código 92)",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (dlg == DialogResult.Yes)
                    {
                        foreach (var kvp in _inputsRetiroDesglose) kvp.Value.Value = 0;
                        foreach (var kvp in alt)
                        {
                            if (int.TryParse(kvp.Key, out int d) && _inputsRetiroDesglose.ContainsKey(d))
                            {
                                _inputsRetiroDesglose[d].Value = kvp.Value;
                            }
                        }
                        await EjecutarRetiroAsync();
                    }
                }
                else
                {
                    MessageBox.Show(resp.Mensaje, "Billetes Insuficientes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                MessageBox.Show($"{resp.Mensaje} (Código: {resp.Codigo})", "Operación Rechazada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion

        #region 6. Pantalla: Depósito

        private void ConstruirPantallaDeposito()
        {
            pnlDeposito = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            var lblT = new Label
            {
                Text = "➕ DEPÓSITO DE EFECTIVO",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                Top = 20,
                Left = 0,
                Width = 1000,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlDeposito.Controls.Add(lblT);

            var grpDesgloseD = new GroupBox
            {
                Text = "Ingrese la cantidad de billetes a depositar:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 215, 230),
                Left = 160,
                Top = 80,
                Width = 680,
                Height = 310
            };

            int[] denoms = { 200, 100, 50, 20, 10, 5, 1 };
            int dy = 30;
            foreach (var d in denoms)
            {
                var lblD = new Label { Text = $"Billetes de Q {d}:", Left = 30, Top = dy + 3, Width = 150, Font = new Font("Segoe UI", 9.5f) };
                var num = new NumericUpDown
                {
                    Left = 200,
                    Top = dy,
                    Width = 120,
                    Font = new Font("Segoe UI", 10f),
                    Minimum = 0,
                    Maximum = 500,
                    Value = 0
                };
                num.ValueChanged += (s, e) => CalcularTotalDeposito();
                _inputsDepositoDesglose[d] = num;

                grpDesgloseD.Controls.Add(lblD);
                grpDesgloseD.Controls.Add(num);
                dy += 36;
            }

            lblTotalDesgloseDeposito = new Label
            {
                Text = "Total a Depositar: Q 0.00",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                Left = 360,
                Top = 130,
                Width = 300,
                Height = 40
            };
            grpDesgloseD.Controls.Add(lblTotalDesgloseDeposito);

            pnlDeposito.Controls.Add(grpDesgloseD);

            var btnConfirmarDep = new Button
            {
                Text = "Confirmar Depósito ➡️",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 280,
                Top = 410,
                Width = 220,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnConfirmarDep.Click += async (s, e) => await EjecutarDepositoAsync();
            pnlDeposito.Controls.Add(btnConfirmarDep);

            var btnVolverD = new Button
            {
                Text = "⬅️ Regresar al Menú",
                Font = new Font("Segoe UI", 11f),
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 520,
                Top = 410,
                Width = 200,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnVolverD.Click += (s, e) => MostrarPantalla(pnlMenu);
            pnlDeposito.Controls.Add(btnVolverD);

            pnlContenedorPantallas.Controls.Add(pnlDeposito);
        }

        private decimal CalcularTotalDeposito()
        {
            decimal total = 0;
            foreach (var kvp in _inputsDepositoDesglose)
            {
                total += kvp.Key * kvp.Value.Value;
            }
            lblTotalDesgloseDeposito.Text = $"Total a Depositar: Q {total:N2}";
            return total;
        }

        private async Task EjecutarDepositoAsync()
        {
            decimal monto = CalcularTotalDeposito();
            if (monto <= 0)
            {
                MessageBox.Show("Debe ingresar al menos un billete para realizar el depósito.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var desglose = new Dictionary<string, int>();
            foreach (var kvp in _inputsDepositoDesglose)
            {
                if (kvp.Value.Value > 0) desglose[kvp.Key.ToString()] = (int)kvp.Value.Value;
            }

            var sol = new SolicitudDeposito
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual!,
                Sesion = _tokenSesion ?? "",
                Monto = monto,
                Desglose = desglose
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(sol);
            if (resp == null)
            {
                MessageBox.Show("Tiempo de espera agotado. No se completó la operación.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (resp.Codigo == CodigosRespuesta.Aprobado)
            {
                decimal nuevoSaldo = 0;
                int idTx = 0;
                if (resp.Datos is JsonElement je)
                {
                    nuevoSaldo = je.GetProperty("saldo").GetDecimal();
                    idTx = je.GetProperty("idTransaccion").GetInt32();
                }
                _saldoActual = nuevoSaldo;
                ActualizarMenuPrincipal();

                foreach (var kvp in _inputsDepositoDesglose) kvp.Value.Value = 0;

                MostrarComprobante("DEPÓSITO DE EFECTIVO", idTx, monto, desglose, nuevoSaldo);
            }
            else
            {
                MessageBox.Show($"{resp.Mensaje} (Código: {resp.Codigo})", "Depósito Rechazado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion

        #region 6.B Pantalla: Transferencias entre Usuarios (Mismo Banco)

        private void ConstruirPantallaTransferenciaInterna()
        {
            pnlTransferenciaInterna = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            var lblT = new Label
            {
                Text = "🔁 TRANSFERENCIA ENTRE USUARIOS (MISMO BANCO)",
                Font = new Font("Segoe UI", 17f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Top = 20,
                Left = 0,
                Width = 1000,
                Height = 38,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlTransferenciaInterna.Controls.Add(lblT);

            var grpForm = new GroupBox
            {
                Text = "Datos de la Transferencia a Cuenta de Nuestro Banco:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 215, 230),
                Left = 130,
                Top = 70,
                Width = 740,
                Height = 335
            };

            var lblCta = new Label
            {
                Text = "Número de Cuenta o Tarjeta Destino:",
                Left = 30,
                Top = 38,
                Width = 270,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            txtCuentaDestinoInterna = new TextBox
            {
                Left = 305,
                Top = 34,
                Width = 220,
                Font = new Font("Consolas", 12f, FontStyle.Bold),
                BackColor = Color.FromArgb(32, 40, 56),
                ForeColor = Color.White,
                Text = "1001000002"
            };
            txtCuentaDestinoInterna.TextChanged += (s, e) =>
            {
                _cuentaInternaVerificada = false;
                lblEstadoVerificacionInterna.Text = "⚠️ Presione 'Verificar Usuario' antes de transferir.";
                lblEstadoVerificacionInterna.ForeColor = Color.FromArgb(255, 177, 66);
            };

            var btnVerif = new Button
            {
                Text = "🔍 Verificar Usuario",
                Left = 540,
                Top = 32,
                Width = 170,
                Height = 34,
                BackColor = Color.FromArgb(0, 168, 255),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnVerif.FlatAppearance.BorderSize = 0;
            btnVerif.Click += async (s, e) => await VerificarCuentaInternaAsync();

            lblEstadoVerificacionInterna = new Label
            {
                Text = "Ingrese el número de cuenta (ej. 1001000002) o tarjeta (16 dígitos) y verifique el titular.",
                Left = 30,
                Top = 78,
                Width = 680,
                Height = 32,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 177, 66)
            };

            // Cuentas rápidas de demostración
            var lblRap = new Label
            {
                Text = "Cuentas de nuestro banco (clic rápido):",
                Left = 30,
                Top = 115,
                Width = 260,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.Gray
            };
            grpForm.Controls.Add(lblRap);

            string[,] ctasInternasDemo = {
                { "Ana López (1001000002)", "1001000002" },
                { "Juan Pérez (1001000003)", "1001000003" },
                { "María Gómez (1001000004)", "1001000004" }
            };
            for (int i = 0; i < 3; i++)
            {
                string lblD = ctasInternasDemo[i, 0];
                string valD = ctasInternasDemo[i, 1];
                var bDemo = new Button
                {
                    Text = lblD,
                    Left = 30 + (i * 225),
                    Top = 136,
                    Width = 215,
                    Height = 28,
                    BackColor = Color.FromArgb(40, 50, 70),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 8.5f),
                    Cursor = Cursors.Hand
                };
                bDemo.FlatAppearance.BorderSize = 0;
                bDemo.Click += async (s, e) =>
                {
                    txtCuentaDestinoInterna.Text = valD;
                    await VerificarCuentaInternaAsync();
                };
                grpForm.Controls.Add(bDemo);
            }

            var lblMonto = new Label
            {
                Text = "Monto a Transferir (Q):",
                Left = 30,
                Top = 185,
                Width = 250,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };
            numMontoTransferenciaInterna = new NumericUpDown
            {
                Left = 305,
                Top = 182,
                Width = 200,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Minimum = 1,
                Maximum = 50000,
                DecimalPlaces = 2,
                Value = 100
            };

            var lblConcepto = new Label
            {
                Text = "Concepto / Motivo:",
                Left = 30,
                Top = 235,
                Width = 250,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };
            txtConceptoTransferenciaInterna = new TextBox
            {
                Left = 305,
                Top = 232,
                Width = 380,
                Font = new Font("Segoe UI", 10.5f),
                BackColor = Color.FromArgb(32, 40, 56),
                ForeColor = Color.White,
                Text = "Pago entre cuentas"
            };

            var lblNotaToken = new Label
            {
                Text = "🔐 Al confirmar se generará un Token único de registro de transferencia para su comprobante e historial.",
                Left = 30,
                Top = 285,
                Width = 680,
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                ForeColor = Color.FromArgb(0, 200, 255)
            };

            grpForm.Controls.AddRange(new Control[]
            {
                lblCta, txtCuentaDestinoInterna, btnVerif, lblEstadoVerificacionInterna,
                lblMonto, numMontoTransferenciaInterna, lblConcepto, txtConceptoTransferenciaInterna, lblNotaToken
            });
            pnlTransferenciaInterna.Controls.Add(grpForm);

            btnConfirmarTransferenciaInterna = new Button
            {
                Text = "Realizar Transferencia ➡️",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 260,
                Top = 425,
                Width = 240,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnConfirmarTransferenciaInterna.FlatAppearance.BorderSize = 0;
            btnConfirmarTransferenciaInterna.Click += async (s, e) => await EjecutarTransferenciaInternaClienteAsync();
            pnlTransferenciaInterna.Controls.Add(btnConfirmarTransferenciaInterna);

            var btnVolver = new Button
            {
                Text = "⬅️ Regresar al Menú",
                Font = new Font("Segoe UI", 11f),
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 520,
                Top = 425,
                Width = 200,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnVolver.FlatAppearance.BorderSize = 0;
            btnVolver.Click += (s, e) => MostrarPantalla(pnlMenu);
            pnlTransferenciaInterna.Controls.Add(btnVolver);

            pnlContenedorPantallas.Controls.Add(pnlTransferenciaInterna);
        }

        private void AbrirPantallaTransferenciaInterna()
        {
            _cuentaInternaVerificada = false;
            lblEstadoVerificacionInterna.Text = "Ingrese el número de cuenta o tarjeta y presione 'Verificar Usuario'.";
            lblEstadoVerificacionInterna.ForeColor = Color.FromArgb(255, 177, 66);
            MostrarPantalla(pnlTransferenciaInterna);
        }

        private async Task VerificarCuentaInternaAsync()
        {
            string cta = txtCuentaDestinoInterna.Text.Trim();
            if (string.IsNullOrEmpty(cta))
            {
                MessageBox.Show("Ingrese un número de cuenta o tarjeta destino.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var req = new SolicitudVerificarCuenta
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual ?? "",
                BancoDestino = _codigoBancoActual ?? "",
                CuentaDestino = cta
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(req);
            if (resp != null && resp.Codigo == CodigosRespuesta.Aprobado && resp.Datos is JsonElement je)
            {
                bool existe = je.GetProperty("existe").GetBoolean();
                if (existe)
                {
                    _cuentaInternaVerificada = true;
                    _titularVerificadoInterno = je.GetProperty("nombreTitular").GetString() ?? "";
                    _cuentaVerificadaInterna = je.GetProperty("cuentaDestino").GetString() ?? cta;

                    lblEstadoVerificacionInterna.Text = $"✅ Usuario Verificado: {_titularVerificadoInterno} (Cuenta: {_cuentaVerificadaInterna})";
                    lblEstadoVerificacionInterna.ForeColor = Color.FromArgb(46, 213, 115);
                    return;
                }
            }

            _cuentaInternaVerificada = false;
            lblEstadoVerificacionInterna.Text = $"❌ {resp?.Mensaje ?? "Cuenta no encontrada en nuestro banco."}";
            lblEstadoVerificacionInterna.ForeColor = Color.FromArgb(255, 71, 87);
        }

        private async Task EjecutarTransferenciaInternaClienteAsync()
        {
            if (!_cuentaInternaVerificada)
            {
                await VerificarCuentaInternaAsync();
                if (!_cuentaInternaVerificada)
                {
                    MessageBox.Show("Primero debe verificar que el número de cuenta exista.", "Verificación Requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            decimal monto = numMontoTransferenciaInterna.Value;
            if (monto <= 0)
            {
                MessageBox.Show("El monto debe ser mayor a Q 0.00.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"¿Confirma realizar la transferencia entre usuarios?\n\n" +
                $"• Destinatario: {_titularVerificadoInterno}\n" +
                $"• Cuenta Destino: {_cuentaVerificadaInterna}\n" +
                $"• Monto: Q {monto:N2}\n" +
                $"• Concepto: {txtConceptoTransferenciaInterna.Text.Trim()}",
                "Confirmar Transferencia",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            var req = new SolicitudTransferencia
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual!,
                Sesion = _tokenSesion ?? "",
                EsInterbancaria = false,
                BancoDestino = _codigoBancoActual ?? "",
                CuentaDestino = _cuentaVerificadaInterna,
                Monto = monto,
                Concepto = txtConceptoTransferenciaInterna.Text.Trim()
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(req);
            if (resp == null)
            {
                MessageBox.Show("Sin respuesta del servidor.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (resp.Codigo == CodigosRespuesta.Aprobado && resp.Datos is JsonElement je)
            {
                ProcesarRespuestaTransferenciaExitosa(je);
            }
            else
            {
                MessageBox.Show($"{resp.Mensaje} (Código: {resp.Codigo})", "Transferencia Rechazada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion

        #region 6.C Pantalla: Apartado de Transferencias entre Diferentes Bancos (Interbancarias)

        private void ConstruirPantallaTransferenciaInterbancaria()
        {
            pnlTransferenciaInterbancaria = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            var lblT = new Label
            {
                Text = "🏦 APARTADO DE TRANSFERENCIAS ENTRE DIFERENTES BANCOS",
                Font = new Font("Segoe UI", 16.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 159, 26),
                Top = 15,
                Left = 0,
                Width = 1000,
                Height = 35,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlTransferenciaInterbancaria.Controls.Add(lblT);

            var grpIb = new GroupBox
            {
                Text = "Coordinación Interbancaria (Verificación de Cuenta + Token en Ambos Bancos + Límite Coordinado):",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 215, 230),
                Left = 110,
                Top = 58,
                Width = 780,
                Height = 365
            };

            var lblBanco = new Label
            {
                Text = "1. Seleccione Banco Destino:",
                Left = 25,
                Top = 35,
                Width = 230,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };

            cbBancoDestino = new ComboBox
            {
                Left = 265,
                Top = 31,
                Width = 480,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(32, 40, 56),
                ForeColor = Color.White
            };
            cbBancoDestino.SelectedIndexChanged += (s, e) =>
            {
                _cuentaInterbancariaVerificada = false;
                ActualizarEtiquetaCuposInterbancarios();
                lblEstadoVerificacionInterbancaria.Text = "⚠️ Ingrese el número de cuenta y presione 'Verificar Cuenta en Banco'.";
                lblEstadoVerificacionInterbancaria.ForeColor = Color.FromArgb(255, 177, 66);
            };

            lblCuposCoordinadosBancos = new Label
            {
                Text = "Límite Coordinado Disponible Hoy -> Nuestro Banco: Q 50,000.00 | Banco Destino: Q 50,000.00",
                Left = 25,
                Top = 68,
                Width = 730,
                Height = 25,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255)
            };

            var lblCtaIb = new Label
            {
                Text = "2. Número de Cuenta Destino:",
                Left = 25,
                Top = 104,
                Width = 230,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };

            txtCuentaDestinoInterbancaria = new TextBox
            {
                Left = 265,
                Top = 100,
                Width = 260,
                Font = new Font("Consolas", 12f, FontStyle.Bold),
                BackColor = Color.FromArgb(32, 40, 56),
                ForeColor = Color.FromArgb(46, 213, 115),
                Text = "2002000001"
            };
            txtCuentaDestinoInterbancaria.TextChanged += (s, e) =>
            {
                _cuentaInterbancariaVerificada = false;
                lblEstadoVerificacionInterbancaria.Text = "⚠️ Cuenta modificada. Presione 'Verificar Cuenta en Banco'.";
                lblEstadoVerificacionInterbancaria.ForeColor = Color.FromArgb(255, 177, 66);
            };

            var btnVerifIb = new Button
            {
                Text = "🔍 Verificar Cuenta en Banco",
                Left = 540,
                Top = 98,
                Width = 205,
                Height = 34,
                BackColor = Color.FromArgb(255, 159, 26),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnVerifIb.FlatAppearance.BorderSize = 0;
            btnVerifIb.Click += async (s, e) => await VerificarCuentaInterbancariaAsync();

            lblEstadoVerificacionInterbancaria = new Label
            {
                Text = "Seleccione el banco, ingrese el número de cuenta y verifique si existe el usuario.",
                Left = 25,
                Top = 142,
                Width = 725,
                Height = 30,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 177, 66)
            };

            // Botones rápidos de cuentas interbancarias de prueba
            var lblDemoIb = new Label
            {
                Text = "Cuentas de otros bancos (clic rápido):",
                Left = 25,
                Top = 175,
                Width = 250,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.Gray
            };
            grpIb.Controls.Add(lblDemoIb);

            string[,] demosIb = {
                { "BANC02: 2002000001 (Roberto)", "BANC02", "2002000001" },
                { "BANC02: 2002000002 (Gabriela)", "BANC02", "2002000002" },
                { "BANC03: 3003000001 (Lucía)", "BANC03", "3003000001" },
                { "BANC04: 4004000001 (Claudia)", "BANC04", "4004000001" }
            };
            for (int i = 0; i < 4; i++)
            {
                string textoBtn = demosIb[i, 0];
                string codB = demosIb[i, 1];
                string ctaB = demosIb[i, 2];

                var bQuick = new Button
                {
                    Text = textoBtn,
                    Left = 25 + (i * 182),
                    Top = 195,
                    Width = 176,
                    Height = 28,
                    BackColor = Color.FromArgb(40, 50, 70),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 8f),
                    Cursor = Cursors.Hand
                };
                bQuick.FlatAppearance.BorderSize = 0;
                bQuick.Click += async (s, e) =>
                {
                    SeleccionarBancoEnCombo(codB);
                    txtCuentaDestinoInterbancaria.Text = ctaB;
                    await VerificarCuentaInterbancariaAsync();
                };
                grpIb.Controls.Add(bQuick);
            }

            var lblMontoIb = new Label
            {
                Text = "3. Monto a Transferir (Q):",
                Left = 25,
                Top = 240,
                Width = 230,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };
            numMontoTransferenciaInterbancaria = new NumericUpDown
            {
                Left = 265,
                Top = 237,
                Width = 200,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Minimum = 1,
                Maximum = 50000,
                DecimalPlaces = 2,
                Value = 250
            };

            var lblConceptoIb = new Label
            {
                Text = "4. Concepto / Referencia:",
                Left = 25,
                Top = 285,
                Width = 230,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };
            txtConceptoTransferenciaInterbancaria = new TextBox
            {
                Left = 265,
                Top = 282,
                Width = 480,
                Font = new Font("Segoe UI", 10.5f),
                BackColor = Color.FromArgb(32, 40, 56),
                ForeColor = Color.White,
                Text = "Transferencia interbancaria"
            };

            var lblInfoTokenAmbos = new Label
            {
                Text = "📌 Al confirmar: se genera el Token de Transferencia (guardado en Nuestro Banco, el Otro Banco y el Usuario)\n" +
                       "   y el monto se resta coordinadamente del límite diario de los dos bancos.",
                Left = 25,
                Top = 320,
                Width = 730,
                Height = 38,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(46, 213, 115)
            };

            grpIb.Controls.AddRange(new Control[]
            {
                lblBanco, cbBancoDestino, lblCuposCoordinadosBancos,
                lblCtaIb, txtCuentaDestinoInterbancaria, btnVerifIb, lblEstadoVerificacionInterbancaria,
                lblMontoIb, numMontoTransferenciaInterbancaria, lblConceptoIb, txtConceptoTransferenciaInterbancaria,
                lblInfoTokenAmbos
            });

            pnlTransferenciaInterbancaria.Controls.Add(grpIb);

            btnConfirmarTransferenciaInterbancaria = new Button
            {
                Text = "Confirmar Transferencia Interbancaria ➡️",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 220,
                Top = 435,
                Width = 310,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnConfirmarTransferenciaInterbancaria.FlatAppearance.BorderSize = 0;
            btnConfirmarTransferenciaInterbancaria.Click += async (s, e) => await EjecutarTransferenciaInterbancariaClienteAsync();
            pnlTransferenciaInterbancaria.Controls.Add(btnConfirmarTransferenciaInterbancaria);

            var btnVolverIb = new Button
            {
                Text = "⬅️ Regresar al Menú",
                Font = new Font("Segoe UI", 10.5f),
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 550,
                Top = 435,
                Width = 200,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnVolverIb.FlatAppearance.BorderSize = 0;
            btnVolverIb.Click += (s, e) => MostrarPantalla(pnlMenu);
            pnlTransferenciaInterbancaria.Controls.Add(btnVolverIb);

            pnlContenedorPantallas.Controls.Add(pnlTransferenciaInterbancaria);
        }

        private async Task AbrirPantallaTransferenciaInterbancariaAsync()
        {
            _cuentaInterbancariaVerificada = false;
            await CargarCatalogoBancosEnClienteAsync();
            MostrarPantalla(pnlTransferenciaInterbancaria);
        }

        private async Task CargarCatalogoBancosEnClienteAsync()
        {
            var resp = await _clienteTcp.EnviarSolicitudAsync(new SolicitudListarBancos { Cajero = _config.CodigoCajero });
            _bancosRed.Clear();
            cbBancoDestino.Items.Clear();

            if (resp != null && resp.Codigo == CodigosRespuesta.Aprobado && resp.Datos is JsonElement je)
            {
                if (je.TryGetProperty("codigoBancoPropio", out var pCod)) _codigoBancoActual = pCod.GetString();
                if (je.TryGetProperty("nombreBancoPropio", out var pNom)) _nombreBancoActual = pNom.GetString();

                if (je.TryGetProperty("bancos", out var arr))
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        var b = JsonSerializer.Deserialize<ItemBancoProtocolo>(item.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (b != null)
                        {
                            _bancosRed.Add(b);
                            if (!b.EsPropio)
                            {
                                cbBancoDestino.Items.Add($"{b.CodigoBanco} - {b.NombreBanco} (Cupo Disp: Q {b.DisponibleHoy:N2})");
                            }
                        }
                    }
                }
            }

            if (cbBancoDestino.Items.Count > 0)
            {
                cbBancoDestino.SelectedIndex = 0;
            }
            ActualizarEtiquetaCuposInterbancarios();
        }

        private void SeleccionarBancoEnCombo(string codigoBanco)
        {
            for (int i = 0; i < cbBancoDestino.Items.Count; i++)
            {
                string itemTxt = cbBancoDestino.Items[i]?.ToString() ?? "";
                if (itemTxt.StartsWith(codigoBanco + " ", StringComparison.OrdinalIgnoreCase))
                {
                    cbBancoDestino.SelectedIndex = i;
                    break;
                }
            }
        }

        private string ObtenerCodigoBancoSeleccionadoCombo()
        {
            string txt = cbBancoDestino.SelectedItem?.ToString() ?? "";
            if (txt.Contains(" - "))
            {
                return txt.Split(" - ")[0].Trim();
            }
            return txt.Trim();
        }

        private void ActualizarEtiquetaCuposInterbancarios()
        {
            var propio = _bancosRed.FirstOrDefault(b => b.EsPropio);
            string codSel = ObtenerCodigoBancoSeleccionadoCombo();
            var dest = _bancosRed.FirstOrDefault(b => b.CodigoBanco.Equals(codSel, StringComparison.OrdinalIgnoreCase));

            decimal dispOrig = propio?.DisponibleHoy ?? 50000m;
            decimal dispDest = dest?.DisponibleHoy ?? 50000m;

            lblCuposCoordinadosBancos.Text =
                $"Límite Diario Coordinado -> Nuestro Banco ({propio?.CodigoBanco ?? "BANC01"}): Q {dispOrig:N2}  |  Banco Destino ({codSel}): Q {dispDest:N2}";
        }

        private async Task VerificarCuentaInterbancariaAsync()
        {
            string codBanco = ObtenerCodigoBancoSeleccionadoCombo();
            string cta = txtCuentaDestinoInterbancaria.Text.Trim();

            if (string.IsNullOrEmpty(codBanco))
            {
                MessageBox.Show("Seleccione un banco de destino.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(cta))
            {
                MessageBox.Show("Ingrese el número de cuenta del banco destino.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            lblEstadoVerificacionInterbancaria.Text = $"⏳ Consultando existencia de cuenta {cta} en {codBanco}...";
            lblEstadoVerificacionInterbancaria.ForeColor = Color.FromArgb(0, 200, 255);

            var req = new SolicitudVerificarCuenta
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual ?? "",
                BancoDestino = codBanco,
                CuentaDestino = cta
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(req);
            if (resp != null && resp.Codigo == CodigosRespuesta.Aprobado && resp.Datos is JsonElement je)
            {
                bool existe = je.GetProperty("existe").GetBoolean();
                if (existe)
                {
                    _cuentaInterbancariaVerificada = true;
                    _titularVerificadoInterbancario = je.GetProperty("nombreTitular").GetString() ?? "";
                    _cuentaVerificadaInterbancaria = je.GetProperty("cuentaDestino").GetString() ?? cta;
                    _codigoBancoVerificado = je.GetProperty("bancoDestino").GetString() ?? codBanco;
                    _nombreBancoVerificado = je.GetProperty("nombreBancoDestino").GetString() ?? codBanco;

                    decimal cupoOrig = je.TryGetProperty("cupoDisponibleOrigen", out var pCo) ? pCo.GetDecimal() : 50000m;
                    decimal cupoDest = je.TryGetProperty("cupoDisponibleDestino", out var pCd) ? pCd.GetDecimal() : 50000m;

                    lblCuposCoordinadosBancos.Text =
                        $"Límite Diario Coordinado -> Nuestro Banco: Q {cupoOrig:N2}  |  {_codigoBancoVerificado}: Q {cupoDest:N2}";

                    lblEstadoVerificacionInterbancaria.Text =
                        $"✅ Usuario Verificado en {_nombreBancoVerificado}: {_titularVerificadoInterbancario} (Cuenta: {_cuentaVerificadaInterbancaria})";
                    lblEstadoVerificacionInterbancaria.ForeColor = Color.FromArgb(46, 213, 115);
                    return;
                }
            }

            _cuentaInterbancariaVerificada = false;
            lblEstadoVerificacionInterbancaria.Text = $"❌ {resp?.Mensaje ?? "No existe el usuario en el banco seleccionado."}";
            lblEstadoVerificacionInterbancaria.ForeColor = Color.FromArgb(255, 71, 87);
        }

        private async Task EjecutarTransferenciaInterbancariaClienteAsync()
        {
            if (!_cuentaInterbancariaVerificada)
            {
                await VerificarCuentaInterbancariaAsync();
                if (!_cuentaInterbancariaVerificada)
                {
                    MessageBox.Show("Debe verificar primero que la cuenta y el usuario existan en el banco seleccionado.", "Verificación Requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            decimal monto = numMontoTransferenciaInterbancaria.Value;
            if (monto <= 0)
            {
                MessageBox.Show("El monto a transferir debe ser mayor a Q 0.00.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var dlg = MessageBox.Show(
                $"¿Confirma realizar la Transferencia Interbancaria?\n\n" +
                $"• Banco Destino: {_codigoBancoVerificado} - {_nombreBancoVerificado}\n" +
                $"• Titular Verificado: {_titularVerificadoInterbancario}\n" +
                $"• Número de Cuenta: {_cuentaVerificadaInterbancaria}\n" +
                $"• Monto: Q {monto:N2}\n\n" +
                $"Se generará un Token único que será guardado por nuestro banco, el banco destino y su cuenta, " +
                $"y el monto se restará coordinadamente al límite de ambos bancos.",
                "Confirmar Transferencia Interbancaria",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dlg != DialogResult.Yes) return;

            var req = new SolicitudTransferencia
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual!,
                Sesion = _tokenSesion ?? "",
                EsInterbancaria = true,
                BancoDestino = _codigoBancoVerificado,
                CuentaDestino = _cuentaVerificadaInterbancaria,
                Monto = monto,
                Concepto = txtConceptoTransferenciaInterbancaria.Text.Trim()
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(req);
            if (resp == null)
            {
                MessageBox.Show("Sin respuesta del servidor.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (resp.Codigo == CodigosRespuesta.Aprobado && resp.Datos is JsonElement je)
            {
                await CargarCatalogoBancosEnClienteAsync();
                ProcesarRespuestaTransferenciaExitosa(je);
            }
            else
            {
                MessageBox.Show($"{resp.Mensaje} (Código: {resp.Codigo})", "Transferencia Interbancaria Rechazada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ProcesarRespuestaTransferenciaExitosa(JsonElement je)
        {
            int idTx = je.TryGetProperty("idTransaccion", out var pId) ? pId.GetInt32() : 0;
            string tokenTrf = je.TryGetProperty("tokenTransferencia", out var pTok) ? (pTok.GetString() ?? "") : "";
            bool esIb = je.TryGetProperty("esInterbancaria", out var pIb) && pIb.GetBoolean();
            string bOrig = je.TryGetProperty("bancoOrigen", out var pBo) ? (pBo.GetString() ?? "") : "";
            string ctaOrig = je.TryGetProperty("cuentaOrigen", out var pCo) ? (pCo.GetString() ?? "") : "";
            string bDest = je.TryGetProperty("bancoDestino", out var pBd) ? (pBd.GetString() ?? "") : "";
            string nomBancoDest = je.TryGetProperty("nombreBancoDestino", out var pNbd) ? (pNbd.GetString() ?? "") : "";
            string ctaDest = je.TryGetProperty("cuentaDestino", out var pCd) ? (pCd.GetString() ?? "") : "";
            string nomDest = je.TryGetProperty("nombreDestino", out var pNd) ? (pNd.GetString() ?? "") : "";
            decimal monto = je.TryGetProperty("monto", out var pM) ? pM.GetDecimal() : 0m;
            string concepto = je.TryGetProperty("concepto", out var pCon) ? (pCon.GetString() ?? "") : "";
            decimal nuevoSaldo = je.TryGetProperty("saldo", out var pS) ? pS.GetDecimal() : _saldoActual;
            decimal cupoOrig = je.TryGetProperty("cupoRestanteBancoOrigen", out var pCro) ? pCro.GetDecimal() : 0m;
            decimal cupoDest = je.TryGetProperty("cupoRestanteBancoDestino", out var pCrd) ? pCrd.GetDecimal() : 0m;

            _saldoActual = nuevoSaldo;
            ActualizarMenuPrincipal();

            MostrarComprobanteTransferencia(
                esIb ? "TRANSFERENCIA INTERBANCARIA" : "TRANSFERENCIA ENTRE USUARIOS",
                idTx,
                tokenTrf,
                bOrig,
                ctaOrig,
                bDest,
                nomBancoDest,
                ctaDest,
                nomDest,
                monto,
                concepto,
                nuevoSaldo,
                cupoOrig,
                cupoDest,
                esIb);
        }

        #endregion

        #region 7. Pantalla: Cambio de PIN

        private void ConstruirPantallaCambioPin()
        {
            pnlCambioPin = new Panel { Dock = DockStyle.Fill };

            var lblT = new Label
            {
                Text = "🔑 CAMBIO DE PIN",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                Top = 30,
                Left = 0,
                Width = 1000,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlCambioPin.Controls.Add(lblT);

            int y = 100;
            var lblA = new Label { Text = "PIN Actual (4 dígitos):", Left = 280, Top = y + 4, Width = 190, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
            txtPinActual = new TextBox { Left = 480, Top = y, Width = 180, Font = new Font("Segoe UI", 12f), PasswordChar = '●', MaxLength = 4 };
            pnlCambioPin.Controls.AddRange(new Control[] { lblA, txtPinActual });
            y += 50;

            var lblN = new Label { Text = "Nuevo PIN (4 dígitos):", Left = 280, Top = y + 4, Width = 190, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
            txtPinNuevo = new TextBox { Left = 480, Top = y, Width = 180, Font = new Font("Segoe UI", 12f), PasswordChar = '●', MaxLength = 4 };
            pnlCambioPin.Controls.AddRange(new Control[] { lblN, txtPinNuevo });
            y += 50;

            var lblC = new Label { Text = "Confirmar Nuevo PIN:", Left = 280, Top = y + 4, Width = 190, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
            txtPinConfirmar = new TextBox { Left = 480, Top = y, Width = 180, Font = new Font("Segoe UI", 12f), PasswordChar = '●', MaxLength = 4 };
            pnlCambioPin.Controls.AddRange(new Control[] { lblC, txtPinConfirmar });
            y += 70;

            var btnCambiar = new Button
            {
                Text = "Actualizar PIN ➡️",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 280,
                Top = y,
                Width = 190,
                Height = 42,
                Cursor = Cursors.Hand
            };
            btnCambiar.Click += async (s, e) => await EjecutarCambioPinAsync();
            pnlCambioPin.Controls.Add(btnCambiar);

            var btnVolver = new Button
            {
                Text = "⬅️ Cancelar",
                Font = new Font("Segoe UI", 11f),
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 490,
                Top = y,
                Width = 170,
                Height = 42,
                Cursor = Cursors.Hand
            };
            btnVolver.Click += (s, e) => MostrarPantalla(pnlMenu);
            pnlCambioPin.Controls.Add(btnVolver);

            pnlContenedorPantallas.Controls.Add(pnlCambioPin);
        }

        private async Task EjecutarCambioPinAsync()
        {
            string actual = txtPinActual.Text.Trim();
            string nuevo = txtPinNuevo.Text.Trim();
            string conf = txtPinConfirmar.Text.Trim();

            if (nuevo.Length != 4 || !nuevo.All(char.IsDigit))
            {
                MessageBox.Show("El nuevo PIN debe constar exactamente de 4 dígitos numéricos.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (nuevo != conf)
            {
                MessageBox.Show("La confirmación del nuevo PIN no coincide.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (actual == nuevo)
            {
                MessageBox.Show("El nuevo PIN debe ser diferente al actual.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sol = new SolicitudCambioPin
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual!,
                PinActual = actual,
                PinNuevo = nuevo
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(sol);
            if (resp == null)
            {
                MessageBox.Show("Sin respuesta del servidor.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (resp.Codigo == CodigosRespuesta.Aprobado)
            {
                MessageBox.Show(resp.Mensaje, "Cambio de PIN Exitoso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtPinActual.Text = "";
                txtPinNuevo.Text = "";
                txtPinConfirmar.Text = "";
                MostrarPantalla(pnlMenu);
            }
            else
            {
                MessageBox.Show(resp.Mensaje, "Error en Cambio de PIN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion

        #region 8. Pantalla: Consulta de Saldo

        private void ConstruirPantallaConsultaSaldo()
        {
            pnlConsultaSaldo = new Panel { Dock = DockStyle.Fill };

            var lblT = new Label
            {
                Text = "📊 CONSULTA DE SALDO",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Top = 30,
                Left = 0,
                Width = 1000,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlConsultaSaldo.Controls.Add(lblT);

            lblInfoSaldo = new Label
            {
                Font = new Font("Consolas", 12f),
                ForeColor = Color.FromArgb(220, 235, 250),
                BackColor = Color.FromArgb(24, 30, 42),
                Top = 90,
                Left = 180,
                Width = 640,
                Height = 250,
                Padding = new Padding(20)
            };
            pnlConsultaSaldo.Controls.Add(lblInfoSaldo);

            var btnVolver = new Button
            {
                Text = "⬅️ Regresar al Menú",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 400,
                Top = 365,
                Width = 200,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnVolver.Click += (s, e) => MostrarPantalla(pnlMenu);
            pnlConsultaSaldo.Controls.Add(btnVolver);

            pnlContenedorPantallas.Controls.Add(pnlConsultaSaldo);
        }

        private async Task ConsultarSaldoServidorAsync()
        {
            var sol = new SolicitudConsultaSaldo
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual!
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(sol);
            if (resp != null && resp.Codigo == CodigosRespuesta.Aprobado && resp.Datos is JsonElement je)
            {
                _saldoActual = je.GetProperty("saldo").GetDecimal();
                _limiteDiarioActual = je.GetProperty("limiteDiario").GetDecimal();
                _disponibleHoyActual = je.GetProperty("disponibleHoy").GetDecimal();
                if (je.TryGetProperty("numeroCuenta", out var pCta) && !string.IsNullOrWhiteSpace(pCta.GetString()))
                {
                    _numeroCuentaActual = pCta.GetString();
                }

                decimal retiradoHoy = _limiteDiarioActual - _disponibleHoyActual;
                if (retiradoHoy < 0) retiradoHoy = 0;

                lblInfoSaldo.Text =
                    $"ESTADO DE CUENTA - RED DE CAJEROS CENTRAL\n" +
                    $"------------------------------------------------------\n" +
                    $"Titular:         {_nombreUsuarioActual}\n" +
                    $"Cuenta Bancaria: {_numeroCuentaActual ?? "N/A"}\n" +
                    $"Tarjeta:         **** {_tarjetaActual![^4..]}\n" +
                    $"Fecha Consulta:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    $"------------------------------------------------------\n" +
                    $"SALDO DISPONIBLE:             Q {_saldoActual:N2}\n" +
                    $"Límite Diario Retiros:        Q {_limiteDiarioActual:N2}\n" +
                    $"Total Retirado Hoy (Red):     Q {retiradoHoy:N2}\n" +
                    $"Disponible para Retiro Hoy:   Q {_disponibleHoyActual:N2}\n" +
                    $"------------------------------------------------------";

                ActualizarMenuPrincipal();
                MostrarPantalla(pnlConsultaSaldo);
            }
            else
            {
                MessageBox.Show("No se pudo consultar el saldo.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion

        #region 9. Pantalla: Últimas 5 Transacciones

        private void ConstruirPantallaUltimasTx()
        {
            pnlUltimasTx = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

            var lblT = new Label
            {
                Text = "📋 ÚLTIMAS TRANSACCIONES Y TOKENS DE TRANSFERENCIA",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(241, 196, 15),
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlUltimasTx.Controls.Add(lblT);

            dgvUltimasTx = new DataGridView
            {
                Top = 60,
                Left = 40,
                Width = 900,
                Height = 300,
                BackgroundColor = Color.FromArgb(24, 30, 42),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(40, 48, 64),
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvUltimasTx.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 40, 56);
            dgvUltimasTx.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0, 200, 255);
            dgvUltimasTx.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            dgvUltimasTx.DefaultCellStyle.BackColor = Color.FromArgb(24, 30, 42);
            dgvUltimasTx.DefaultCellStyle.ForeColor = Color.White;

            dgvUltimasTx.Columns.Add("Fecha", "Fecha y Hora");
            dgvUltimasTx.Columns.Add("Cajero", "Cajero / Canal");
            dgvUltimasTx.Columns.Add("Tipo", "Operación");
            dgvUltimasTx.Columns.Add("Monto", "Monto");
            dgvUltimasTx.Columns.Add("Token", "Token Transferencia");
            dgvUltimasTx.Columns.Add("Resultado", "Resultado / Detalle");

            pnlUltimasTx.Controls.Add(dgvUltimasTx);

            var btnVolver = new Button
            {
                Text = "⬅️ Regresar al Menú",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 390,
                Top = 380,
                Width = 200,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnVolver.Click += (s, e) => MostrarPantalla(pnlMenu);
            pnlUltimasTx.Controls.Add(btnVolver);

            pnlContenedorPantallas.Controls.Add(pnlUltimasTx);
        }

        private async Task ConsultarUltimasTxAsync()
        {
            var sol = new SolicitudUltimasTransacciones
            {
                Cajero = _config.CodigoCajero,
                NumeroTarjeta = _tarjetaActual!,
                Cantidad = 10
            };

            var resp = await _clienteTcp.EnviarSolicitudAsync(sol);
            if (resp != null && resp.Codigo == CodigosRespuesta.Aprobado && resp.Datos is JsonElement je)
            {
                dgvUltimasTx.Rows.Clear();
                if (je.TryGetProperty("transacciones", out var txsArray))
                {
                    foreach (var item in txsArray.EnumerateArray())
                    {
                        string fecha = item.GetProperty("fecha").GetString() ?? "";
                        string caj = item.GetProperty("cajero").GetString() ?? "";
                        string tipo = item.GetProperty("tipo").GetString() ?? "";
                        decimal monto = item.GetProperty("monto").GetDecimal();
                        string cod = item.GetProperty("codigoRespuesta").GetString() ?? "";
                        string tokenTrf = item.TryGetProperty("tokenTransferencia", out var pTok) && pTok.ValueKind == JsonValueKind.String
                            ? (pTok.GetString() ?? "-")
                            : "-";
                        string det = item.TryGetProperty("motivoRechazo", out var pDet) && pDet.ValueKind == JsonValueKind.String
                            ? (pDet.GetString() ?? "")
                            : "";

                        string resTexto = cod == "00"
                            ? (string.IsNullOrEmpty(det) ? "Aprobada" : det)
                            : $"Rechazada ({cod})";

                        dgvUltimasTx.Rows.Add(fecha, caj, tipo, $"Q {monto:N2}", tokenTrf, resTexto);
                    }
                }
                MostrarPantalla(pnlUltimasTx);
            }
            else
            {
                MessageBox.Show("No se pudieron obtener las últimas transacciones.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion

        #region 10. Pantalla: Comprobante en Pantalla (Imprimir / Guardar)

        private void ConstruirPantallaComprobante()
        {
            pnlComprobante = new Panel { Dock = DockStyle.Fill };

            var lblT = new Label
            {
                Text = "🧾 COMPROBANTE DE TRANSACCIÓN",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                Top = 20,
                Left = 0,
                Width = 1000,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlComprobante.Controls.Add(lblT);

            lblTextoComprobante = new Label
            {
                Font = new Font("Consolas", 10f),
                ForeColor = Color.Black,
                BackColor = Color.FromArgb(245, 245, 245),
                Top = 65,
                Left = 220,
                Width = 560,
                Height = 375,
                Padding = new Padding(20)
            };
            pnlComprobante.Controls.Add(lblTextoComprobante);

            var btnGuardar = new Button
            {
                Text = "💾 Guardar Comprobante (TXT)",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 280,
                Top = 450,
                Width = 240,
                Height = 40,
                Cursor = Cursors.Hand
            };
            btnGuardar.Click += (s, e) => GuardarComprobanteEnArchivo();
            pnlComprobante.Controls.Add(btnGuardar);

            var btnMenu = new Button
            {
                Text = "Volver al Menú ➡️",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 540,
                Top = 450,
                Width = 180,
                Height = 40,
                Cursor = Cursors.Hand
            };
            btnMenu.Click += (s, e) => MostrarPantalla(pnlMenu);
            pnlComprobante.Controls.Add(btnMenu);

            pnlContenedorPantallas.Controls.Add(pnlComprobante);
        }

        private void MostrarComprobante(string tipoOp, int idTx, decimal monto, Dictionary<string, int> desglose, decimal nuevoSaldo)
        {
            string desgloseTexto = string.Join("\n  ", desglose.Select(x => $"{x.Value} billete(s) de Q {x.Key}"));

            lblTextoComprobante.Text =
                $"================================================\n" +
                $"             BANCO NACIONAL CENTRAL             \n" +
                $"            COMPROBANTE DE OPERACIÓN            \n" +
                $"================================================\n" +
                $"Cajero:          {_config.CodigoCajero}\n" +
                $"Transacción No:  #{idTx}\n" +
                $"Fecha y Hora:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Cliente:         {_nombreUsuarioActual}\n" +
                $"Tarjeta:         **** {_tarjetaActual![^4..]}\n" +
                $"Tipo Operación:  {tipoOp}\n" +
                $"Monto Aplicado:  Q {monto:N2}\n" +
                $"------------------------------------------------\n" +
                $"Desglose Entregado / Depositado:\n  {desgloseTexto}\n" +
                $"------------------------------------------------\n" +
                $"NUEVO SALDO:     Q {nuevoSaldo:N2}\n" +
                $"================================================\n" +
                $"       ¡Gracias por preferir nuestro banco!     \n" +
                $"================================================";

            MostrarPantalla(pnlComprobante);
        }

        private void MostrarComprobanteTransferencia(
            string tipoOp,
            int idTx,
            string tokenTransferencia,
            string bancoOrigen,
            string cuentaOrigen,
            string bancoDestino,
            string nombreBancoDestino,
            string cuentaDestino,
            string nombreDestino,
            decimal monto,
            string concepto,
            decimal nuevoSaldo,
            decimal cupoRestanteOrigen,
            decimal cupoRestanteDestino,
            bool esInterbancaria)
        {
            string lineaCupos = esInterbancaria
                ? $"Cupo Rest. {bancoOrigen}: Q {cupoRestanteOrigen:N2}\n" +
                  $"Cupo Rest. {bancoDestino}: Q {cupoRestanteDestino:N2}\n"
                : $"Registro:        Interno en {bancoOrigen}\n";

            lblTextoComprobante.Text =
                $"================================================\n" +
                $"             BANCO NACIONAL CENTRAL             \n" +
                $"         COMPROBANTE DE TRANSFERENCIA           \n" +
                $"================================================\n" +
                $"TOKEN REGISTRO:  {tokenTransferencia}\n" +
                $"Transacción No:  #{idTx}   |   Cajero: {_config.CodigoCajero}\n" +
                $"Fecha y Hora:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Tipo Operación:  {tipoOp}\n" +
                $"------------------------------------------------\n" +
                $"ORIGEN:          {_nombreUsuarioActual}\n" +
                $"Banco / Cuenta:  {bancoOrigen} - {cuentaOrigen}\n" +
                $"DESTINO:         {nombreDestino}\n" +
                $"Banco / Cuenta:  {bancoDestino} ({nombreBancoDestino}) - {cuentaDestino}\n" +
                $"Concepto:        {concepto}\n" +
                $"------------------------------------------------\n" +
                $"MONTO ENVIADO:   Q {monto:N2}\n" +
                $"NUEVO SALDO:     Q {nuevoSaldo:N2}\n" +
                $"{lineaCupos}" +
                $"================================================\n" +
                $" Token guardado en ambos bancos y en su cuenta  \n" +
                $"================================================";

            MostrarPantalla(pnlComprobante);
        }

        private void GuardarComprobanteEnArchivo()
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "Archivo de Texto (*.txt)|*.txt",
                FileName = $"Comprobante_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(sfd.FileName, lblTextoComprobante.Text);
                MessageBox.Show("Comprobante guardado con éxito.", "Guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        #endregion

        #region Teclado Touch Reutilizable

        private Panel CrearTecladoTouch(Action<string> onDigito, Action onBorrar, Action onContinuar, Action onCancelar)
        {
            var pnl = new Panel { Width = 320, Height = 280, BackColor = Color.Transparent };

            string[,] teclas = {
                { "1", "2", "3" },
                { "4", "5", "6" },
                { "7", "8", "9" },
                { "Cancelar", "0", "Borrar" }
            };

            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    string texto = teclas[r, c];
                    var btn = new Button
                    {
                        Text = texto,
                        Font = new Font("Segoe UI", (texto.Length > 1 ? 9.5f : 14f), FontStyle.Bold),
                        Left = c * 105,
                        Top = r * 55,
                        Width = 98,
                        Height = 48,
                        FlatStyle = FlatStyle.Flat,
                        Cursor = Cursors.Hand
                    };
                    btn.FlatAppearance.BorderSize = 0;

                    if (texto == "Cancelar")
                    {
                        btn.BackColor = Color.FromArgb(231, 76, 60);
                        btn.ForeColor = Color.White;
                        btn.Click += (s, e) => onCancelar();
                    }
                    else if (texto == "Borrar")
                    {
                        btn.BackColor = Color.FromArgb(241, 196, 15);
                        btn.ForeColor = Color.Black;
                        btn.Click += (s, e) => onBorrar();
                    }
                    else
                    {
                        btn.BackColor = Color.FromArgb(40, 50, 70);
                        btn.ForeColor = Color.White;
                        string d = texto;
                        btn.Click += (s, e) => onDigito(d);
                    }
                    pnl.Controls.Add(btn);
                }
            }

            var btnEnter = new Button
            {
                Text = "CONTINUAR ➡️",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 0,
                Top = 225,
                Width = 308,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnEnter.FlatAppearance.BorderSize = 0;
            btnEnter.Click += (s, e) => onContinuar();
            pnl.Controls.Add(btnEnter);

            return pnl;
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timerReloj?.Stop();
            timerReloj?.Dispose();
            timerInactividad?.Stop();
            timerInactividad?.Dispose();
            _lectorRfid?.Dispose();
            _clienteTcp?.Detener();
            base.OnFormClosing(e);
        }
    }
}
