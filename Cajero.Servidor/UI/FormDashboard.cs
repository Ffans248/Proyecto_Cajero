using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cajero.Protocolo;
using Cajero.Servidor.AccesoDatos;
using Cajero.Servidor.Comunicaciones;
using Cajero.Servidor.Modelos;
using Cajero.Servidor.UI.Controles;

namespace Cajero.Servidor.UI
{
    public class FormDashboard : Form
    {
        private readonly AdministradorSesion _admin;
        private readonly RepositorioServidor _repo;
        private readonly ServidorTcp _servidorTcp;

        // Controles de Navegación
        private Panel pnlSidebar = null!;
        private Panel pnlHeader = null!;
        private Panel pnlContenido = null!;
        private Label lblTituloModulo = null!;
        private Label lblEstadoServidor = null!;
        private Button btnActualizarAnimado = null!;
        private System.Windows.Forms.Timer timerAutoRefresh = null!;
        private System.Windows.Forms.Timer timerAnimacionBoton = null!;

        // Vistas / Pestañas principales
        private Panel pnlResumen = null!;
        private Panel pnlMonitorCajeros = null!;
        private Panel pnlTransacciones = null!;
        private Panel pnlInterbancario = null!;
        private Panel pnlUsuarios = null!;
        private Panel pnlConsultasFase1 = null!;
        private Panel pnlAlertas = null!;
        private Panel pnlBitacora = null!;
        private Panel pnlConfiguracion = null!;

        // Gráficas
        private GraficaPanel graficaHoras = null!;
        private GraficaPanel graficaMontosCajero = null!;
        private GraficaPanel graficaDenominaciones = null!;

        // Grids
        private DataGridView dgvCajeros = null!;
        private DataGridView dgvTransacciones = null!;
        private DataGridView dgvBancosRed = null!;
        private DataGridView dgvTransferencias = null!;
        private DataGridView dgvUsuarios = null!;
        private DataGridView dgvAlertas = null!;
        private DataGridView dgvBitacora = null!;

        // Controles de KPIs
        private Label lblKpiCajeros = null!;
        private Label lblKpiEfectivo = null!;
        private Label lblKpiRetiros = null!;
        private Label lblKpiDepositos = null!;
        private Label lblKpiTransacciones = null!;
        private Label lblKpiAlertas = null!;

        // Filtros de transacciones
        private ComboBox cbFiltroCajeroTx = null!;
        private ComboBox cbFiltroTipoTx = null!;
        private TextBox txtFiltroUsuarioTx = null!;

        // Filtro de transferencias
        private TextBox txtFiltroTokenTransferencia = null!;

        // Consultas Fase 1
        private TextBox txtConsultaUsuario = null!;
        private Label lblDetalleUsuarioFase1 = null!;
        private Label lblControlUsuariosFase1 = null!;

        public FormDashboard(AdministradorSesion admin, RepositorioServidor repo, ServidorTcp servidorTcp)
        {
            _admin = admin;
            _repo = repo;
            _servidorTcp = servidorTcp;

            InicializarComponentes();
            ConectarEventosServidor();
            _ = CargarDatosCompletosAsync();
        }

        private void ConectarEventosServidor()
        {
            _servidorTcp.TokenGenerado += (token, user, atm, exp) =>
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => MostrarVentanaToken(token, user, atm, exp)));
                }
                else
                {
                    MostrarVentanaToken(token, user, atm, exp);
                }
            };

            _servidorTcp.CajeroConectado += (cod, ip) =>
            {
                if (this.InvokeRequired)
                    this.BeginInvoke(new Action(() => ActualizarDashboardSilencioso()));
            };

            _servidorTcp.CajeroDesconectado += (cod, motivo) =>
            {
                if (this.InvokeRequired)
                    this.BeginInvoke(new Action(() => ActualizarDashboardSilencioso()));
            };

            _servidorTcp.TransaccionProcesada += (cajero, tipo, monto, cod) =>
            {
                if (this.InvokeRequired)
                    this.BeginInvoke(new Action(() => ActualizarDashboardSilencioso()));
            };

            _servidorTcp.AlertaDisparada += (tipo, msj) =>
            {
                if (this.InvokeRequired)
                    this.BeginInvoke(new Action(() => ActualizarDashboardSilencioso()));
            };
        }

        private void MostrarVentanaToken(string token, string user, string atm, DateTime exp)
        {
            try
            {
                var fToken = new FormToken(token, user, atm, exp);
                fToken.Show();
            }
            catch { }
        }

        private void InicializarComponentes()
        {
            this.Text = $"Panel de Control Central - Cajeros Automáticos (.NET 8) [{_admin.Usuario} - {_admin.Rol}]";
            this.Size = new Size(1340, 820);
            this.MinimumSize = new Size(1180, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(18, 22, 30);
            this.ForeColor = Color.White;

            // ================= 1. HEADER =================
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(24, 28, 38)
            };

            lblTituloModulo = new Label
            {
                Text = "📊 Resumen General del Día",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Left = 240,
                Top = 16,
                Width = 380
            };
            pnlHeader.Controls.Add(lblTituloModulo);

            var infoPropio = _repo.ObtenerInfoBancoPropio();
            lblEstadoServidor = new Label
            {
                Text = $"🟢 Servidor TCP: Puerto {_servidorTcp.Puerto} | {infoPropio.codigoBanco} ({infoPropio.nombreBanco})",
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                Left = 620,
                Top = 22,
                Width = 310
            };
            pnlHeader.Controls.Add(lblEstadoServidor);

            // Botón de Actualizar con Feedback Visual Animado
            btnActualizarAnimado = new Button
            {
                Text = "🔄 Actualizar",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 940,
                Top = 14,
                Width = 150,
                Height = 36,
                Cursor = Cursors.Hand
            };
            btnActualizarAnimado.FlatAppearance.BorderSize = 0;
            btnActualizarAnimado.Click += async (s, e) => await EjecutarAnimacionActualizarAsync();
            pnlHeader.Controls.Add(btnActualizarAnimado);

            var lblAdminInfo = new Label
            {
                Text = $"👤 {_admin.Usuario}\n({_admin.Rol})",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(200, 215, 230),
                Left = 1100,
                Top = 14,
                Width = 190,
                TextAlign = ContentAlignment.MiddleRight
            };
            pnlHeader.Controls.Add(lblAdminInfo);

            this.Controls.Add(pnlHeader);

            // ================= 2. SIDEBAR =================
            pnlSidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 225,
                BackColor = Color.FromArgb(15, 18, 25)
            };

            var lblBrand = new Label
            {
                Text = "🏦 ATM CENTRAL\nSISTEMA DISTRIBUIDO",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Dock = DockStyle.Top,
                Height = 65,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlSidebar.Controls.Add(lblBrand);

            int btnY = 75;
            AgregarBotonMenu("📊 Resumen General", () => MostrarModulo(pnlResumen, "📊 Resumen General del Día"), ref btnY);
            AgregarBotonMenu("🖥️ Monitor Cajeros", () => MostrarModulo(pnlMonitorCajeros, "🖥️ Monitor en Tiempo Real de Cajeros"), ref btnY);
            AgregarBotonMenu("💳 Transacciones", () => MostrarModulo(pnlTransacciones, "💳 Transacciones en Tiempo Real"), ref btnY);
            AgregarBotonMenu("🏦 Red Interbancaria", () => MostrarModulo(pnlInterbancario, "🏦 Red Interbancaria, Cupos y Tokens"), ref btnY);
            AgregarBotonMenu("👥 Gestión Usuarios", () => MostrarModulo(pnlUsuarios, "👥 Gestión de Usuarios, Cuentas y Tarjetas"), ref btnY);
            AgregarBotonMenu("🔍 Consultas Fase I", () => MostrarModulo(pnlConsultasFase1, "🔍 Consultas y Control de Usuarios (Fase I)"), ref btnY);
            AgregarBotonMenu("⚠️ Panel Alertas", () => MostrarModulo(pnlAlertas, "⚠️ Alertas del Sistema y Notificaciones"), ref btnY);
            AgregarBotonMenu("📜 Bitácora Auditoría", () => MostrarModulo(pnlBitacora, "📜 Bitácora de Auditoría del Servidor"), ref btnY);
            AgregarBotonMenu("⚙️ Configuración", () => MostrarModulo(pnlConfiguracion, "⚙️ Parámetros y Configuración del Servidor"), ref btnY);

            // Botón Salir
            var btnCerrarSesion = new Button
            {
                Text = "🚪 Cerrar Sesión",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(255, 71, 87),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Bottom,
                Height = 45,
                Cursor = Cursors.Hand
            };
            btnCerrarSesion.FlatAppearance.BorderSize = 0;
            btnCerrarSesion.Click += (s, e) => this.Close();
            pnlSidebar.Controls.Add(btnCerrarSesion);

            this.Controls.Add(pnlSidebar);

            // ================= 3. CONTENEDOR DE PÓRTICOS =================
            pnlContenido = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 24, 33)
            };
            this.Controls.Add(pnlContenido);
            pnlContenido.BringToFront();

            // Crear los módulos individuales
            ConstruirModuloResumen();
            ConstruirModuloMonitorCajeros();
            ConstruirModuloTransacciones();
            ConstruirModuloInterbancario();
            ConstruirModuloUsuarios();
            ConstruirModuloConsultasFase1();
            ConstruirModuloAlertas();
            ConstruirModuloBitacora();
            ConstruirModuloConfiguracion();

            MostrarModulo(pnlResumen, "📊 Resumen General del Día");

            // Timer de refresco automático cada 5 segundos
            timerAutoRefresh = new System.Windows.Forms.Timer { Interval = 5000 };
            timerAutoRefresh.Tick += (s, e) => ActualizarDashboardSilencioso();
            timerAutoRefresh.Start();
        }

        private void AgregarBotonMenu(string texto, Action accion, ref int y)
        {
            var btn = new Button
            {
                Text = "  " + texto,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(210, 220, 235),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Top = y,
                Left = 0,
                Width = 225,
                Height = 40,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 40, 56);
            btn.Click += (s, e) => accion();
            pnlSidebar.Controls.Add(btn);
            y += 42;
        }

        private void MostrarModulo(Panel panel, string titulo)
        {
            pnlContenido.SuspendLayout();
            pnlResumen.Visible = false;
            pnlMonitorCajeros.Visible = false;
            pnlTransacciones.Visible = false;
            pnlInterbancario.Visible = false;
            pnlUsuarios.Visible = false;
            pnlConsultasFase1.Visible = false;
            pnlAlertas.Visible = false;
            pnlBitacora.Visible = false;
            pnlConfiguracion.Visible = false;

            panel.Visible = true;
            panel.BringToFront();
            lblTituloModulo.Text = titulo;
            pnlContenido.ResumeLayout();
        }

        #region Animación y Feedback del Botón Actualizar

        private async Task EjecutarAnimacionActualizarAsync()
        {
            btnActualizarAnimado.Enabled = false;

            string[] frames = { "⏳ Actualizando.", "⏳ Actualizando..", "⏳ Actualizando...", "🔄 Refrescando" };
            Color[] colores = { Color.FromArgb(255, 165, 2), Color.FromArgb(255, 177, 66), Color.FromArgb(0, 180, 255) };

            for (int i = 0; i < 4; i++)
            {
                btnActualizarAnimado.Text = frames[i % frames.Length];
                btnActualizarAnimado.BackColor = colores[i % colores.Length];
                await Task.Delay(120);
            }

            await CargarDatosCompletosAsync();

            // Feedback de Éxito
            btnActualizarAnimado.Text = "✓ ¡Listo!";
            btnActualizarAnimado.BackColor = Color.FromArgb(46, 213, 115); // Verde brillante
            await Task.Delay(600);

            btnActualizarAnimado.Text = "🔄 Actualizar";
            btnActualizarAnimado.BackColor = Color.FromArgb(0, 122, 255);
            btnActualizarAnimado.Enabled = true;
        }

        private void ActualizarDashboardSilencioso()
        {
            Task.Run(() =>
            {
                var resumen = _repo.ObtenerResumenDiario();
                var cajeros = _repo.ObtenerEstadoCajeros();
                var txs = _repo.ObtenerTransacciones();
                var bancos = _repo.ObtenerBancosRed();
                var transferencias = _repo.ObtenerHistorialTransferencias(null, txtFiltroTokenTransferencia?.Text);

                this.BeginInvoke(new Action(() =>
                {
                    ActualizarVistaResumen(resumen, txs, cajeros);
                    ActualizarVistaCajeros(cajeros);
                    ActualizarVistaInterbancario(bancos, transferencias);
                }));
            });
        }

        private async Task CargarDatosCompletosAsync()
        {
            var resumen = await Task.Run(() => _repo.ObtenerResumenDiario());
            var cajeros = await Task.Run(() => _repo.ObtenerEstadoCajeros());
            var txs = await Task.Run(() => _repo.ObtenerTransacciones());
            var bancos = await Task.Run(() => _repo.ObtenerBancosRed());
            var transferencias = await Task.Run(() => _repo.ObtenerHistorialTransferencias(null, txtFiltroTokenTransferencia?.Text));
            var usuarios = await Task.Run(() => _repo.ObtenerUsuarios());
            var alertas = await Task.Run(() => _repo.ObtenerAlertas());
            var bitacora = await Task.Run(() => _repo.ObtenerBitacora());

            ActualizarVistaResumen(resumen, txs, cajeros);
            ActualizarVistaCajeros(cajeros);
            ActualizarVistaTransacciones(txs);
            ActualizarVistaInterbancario(bancos, transferencias);
            ActualizarVistaUsuarios(usuarios);
            ActualizarVistaAlertas(alertas);
            ActualizarVistaBitacora(bitacora);
        }

        #endregion

        #region 1. Módulo Resumen General

        private void ConstruirModuloResumen()
        {
            pnlResumen = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            // Panel superior con Cards KPIs
            var pnlKpis = new FlowLayoutPanel
            {
                Top = 15,
                Left = 15,
                Width = 1030,
                Height = 115,
                FlowDirection = FlowDirection.LeftToRight
            };

            lblKpiCajeros = CrearCardKpi("Cajeros En Línea", "0 / 0", Color.FromArgb(46, 213, 115), pnlKpis);
            lblKpiEfectivo = CrearCardKpi("Efectivo Red", "Q 0.00", Color.FromArgb(0, 180, 255), pnlKpis);
            lblKpiRetiros = CrearCardKpi("Retiros Hoy", "Q 0.00", Color.FromArgb(255, 71, 87), pnlKpis);
            lblKpiDepositos = CrearCardKpi("Depósitos Hoy", "Q 0.00", Color.FromArgb(46, 204, 113), pnlKpis);
            lblKpiTransacciones = CrearCardKpi("Transacciones", "0", Color.FromArgb(241, 196, 15), pnlKpis);
            lblKpiAlertas = CrearCardKpi("Alertas Activas", "0", Color.FromArgb(231, 76, 60), pnlKpis);

            pnlResumen.Controls.Add(pnlKpis);

            // Gráficas
            graficaHoras = new GraficaPanel
            {
                Titulo = "📈 Retiros vs Depósitos por Hora del Día",
                Tipo = TipoGrafica.RetirosVsDepositosPorHora,
                Top = 145,
                Left = 20,
                Width = 500,
                Height = 260
            };
            pnlResumen.Controls.Add(graficaHoras);

            graficaMontosCajero = new GraficaPanel
            {
                Titulo = "🏦 Monto Total por Cajero en Red",
                Tipo = TipoGrafica.MontosPorCajero,
                Top = 145,
                Left = 535,
                Width = 500,
                Height = 260
            };
            pnlResumen.Controls.Add(graficaMontosCajero);

            graficaDenominaciones = new GraficaPanel
            {
                Titulo = "💵 Distribución de Billetes en Red (Q 200 a Q 1)",
                Tipo = TipoGrafica.DistribucionDenominaciones,
                Top = 415,
                Left = 20,
                Width = 1015,
                Height = 250
            };
            pnlResumen.Controls.Add(graficaDenominaciones);

            pnlContenido.Controls.Add(pnlResumen);
        }

        private Label CrearCardKpi(string titulo, string valorInicial, Color acento, Control parent)
        {
            var pnl = new Panel
            {
                Width = 160,
                Height = 95,
                BackColor = Color.FromArgb(28, 34, 46),
                Margin = new Padding(5)
            };

            var bar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 4,
                BackColor = acento
            };
            pnl.Controls.Add(bar);

            var lblT = new Label
            {
                Text = titulo,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(170, 185, 205),
                Top = 10,
                Left = 12,
                Width = 140
            };
            pnl.Controls.Add(lblT);

            var lblV = new Label
            {
                Text = valorInicial,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.White,
                Top = 35,
                Left = 12,
                Width = 140,
                Height = 50
            };
            pnl.Controls.Add(lblV);

            parent.Controls.Add(pnl);
            return lblV;
        }

        private void ActualizarVistaResumen(ResumenDiarioDto res, List<TransaccionDto> txs, List<EstadoCajeroDto> cajeros)
        {
            lblKpiCajeros.Text = $"{res.CajerosEnLinea} / {res.TotalCajeros}";
            lblKpiEfectivo.Text = $"Q {res.EfectivoTotalRed:N0}";
            lblKpiRetiros.Text = $"Q {res.TotalRetiradoHoy:N0}";
            lblKpiDepositos.Text = $"Q {res.TotalDepositadoHoy:N0}";
            lblKpiTransacciones.Text = $"{res.NumeroTransaccionesHoy}";
            lblKpiAlertas.Text = $"{res.AlertasActivas}";

            // Gráfica por horas
            var retPorHora = new Dictionary<int, decimal>();
            var depPorHora = new Dictionary<int, decimal>();
            foreach (var t in txs.Where(x => x.Aprobada && x.Fecha.Date == DateTime.Today))
            {
                int h = t.Fecha.Hour;
                if (t.Tipo == "RETIRO")
                    retPorHora[h] = (retPorHora.TryGetValue(h, out var v) ? v : 0) + t.Monto;
                else if (t.Tipo == "DEPOSITO")
                    depPorHora[h] = (depPorHora.TryGetValue(h, out var v) ? v : 0) + t.Monto;
            }
            graficaHoras.RetirosPorHora = retPorHora;
            graficaHoras.DepositosPorHora = depPorHora;
            graficaHoras.Invalidate();

            // Gráfica montos por cajero
            var montosCaj = new Dictionary<string, decimal>();
            foreach (var c in cajeros)
            {
                montosCaj[c.Codigo] = c.EfectivoTotal;
            }
            graficaMontosCajero.MontosPorCajero = montosCaj;
            graficaMontosCajero.Invalidate();

            // Gráfica de denominaciones totales en la red
            var billetesRed = new Dictionary<int, int>
            {
                { 200, cajeros.Sum(x => x.Billetes200) },
                { 100, cajeros.Sum(x => x.Billetes100) },
                { 50,  cajeros.Sum(x => x.Billetes50) },
                { 20,  cajeros.Sum(x => x.Billetes20) },
                { 10,  cajeros.Sum(x => x.Billetes10) },
                { 5,   cajeros.Sum(x => x.Billetes5) },
                { 1,   cajeros.Sum(x => x.Billetes1) }
            };
            graficaDenominaciones.BilletesPorDenominacion = billetesRed;
            graficaDenominaciones.Invalidate();
        }

        #endregion

        #region 2. Módulo Monitor de Cajeros

        private void ConstruirModuloMonitorCajeros()
        {
            pnlMonitorCajeros = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            var pnlTopAcciones = new Panel { Dock = DockStyle.Top, Height = 45 };

            var btnInit = new Button
            {
                Text = "💵 Inicializar Efectivo (≤10k)",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Width = 200,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnInit.FlatAppearance.BorderSize = 0;
            btnInit.Click += (s, e) => AbrirCargaEfectivo(true);
            pnlTopAcciones.Controls.Add(btnInit);

            var btnAdd = new Button
            {
                Text = "➕ Agregar Efectivo (≤30k)",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 180, 255),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 210,
                Width = 190,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnAdd.FlatAppearance.BorderSize = 0;
            btnAdd.Click += (s, e) => AbrirCargaEfectivo(false);
            pnlTopAcciones.Controls.Add(btnAdd);

            var btnFueraServ = new Button
            {
                Text = "🚫 Poner Fuera de Servicio",
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 410,
                Width = 180,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnFueraServ.FlatAppearance.BorderSize = 0;
            btnFueraServ.Click += async (s, e) => await CambiarEstadoRemotoCajero(TiposComando.FueraDeServicio, "Fuera de servicio");
            pnlTopAcciones.Controls.Add(btnFueraServ);

            var btnHabilitar = new Button
            {
                Text = "✅ Habilitar Cajero",
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(39, 174, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 600,
                Width = 150,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnHabilitar.FlatAppearance.BorderSize = 0;
            btnHabilitar.Click += async (s, e) => await CambiarEstadoRemotoCajero(TiposComando.Habilitar, "En línea");
            pnlTopAcciones.Controls.Add(btnHabilitar);

            var btnMsg = new Button
            {
                Text = "📢 Mensaje Remoto",
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(155, 89, 182),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 760,
                Width = 150,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnMsg.FlatAppearance.BorderSize = 0;
            btnMsg.Click += (s, e) =>
            {
                string cod = ObtenerCajeroSeleccionado();
                if (string.IsNullOrEmpty(cod)) return;
                var fMsg = new FormMensajeRemoto(_servidorTcp, cod);
                fMsg.ShowDialog();
            };
            pnlTopAcciones.Controls.Add(btnMsg);

            pnlMonitorCajeros.Controls.Add(pnlTopAcciones);

            dgvCajeros = CrearGridGenerico();
            dgvCajeros.Dock = DockStyle.Fill;
            dgvCajeros.Columns.Add("Codigo", "Código");
            dgvCajeros.Columns.Add("Nombre", "Nombre / Ubicación");
            dgvCajeros.Columns.Add("IP", "IP");
            dgvCajeros.Columns.Add("Estado", "Estado");
            dgvCajeros.Columns.Add("EfectivoTotal", "Efectivo Total");
            dgvCajeros.Columns.Add("Desglose", "Q200 | Q100 | Q50 | Q20 | Q10 | Q5 | Q1");
            dgvCajeros.Columns.Add("Heartbeat", "Último Heartbeat");
            dgvCajeros.Columns.Add("UltimaTx", "Última Operación");

            pnlMonitorCajeros.Controls.Add(dgvCajeros);
            dgvCajeros.BringToFront();
            pnlContenido.Controls.Add(pnlMonitorCajeros);
        }

        private void ActualizarVistaCajeros(List<EstadoCajeroDto> cajeros)
        {
            dgvCajeros.Rows.Clear();
            foreach (var c in cajeros)
            {
                int rowIdx = dgvCajeros.Rows.Add(
                    c.Codigo,
                    $"{c.Nombre} ({c.Ubicacion})",
                    c.IP,
                    c.Estado,
                    $"Q {c.EfectivoTotal:N2}",
                    $"{c.Billetes200} | {c.Billetes100} | {c.Billetes50} | {c.Billetes20} | {c.Billetes10} | {c.Billetes5} | {c.Billetes1}",
                    c.UltimoHeartbeat?.ToString("HH:mm:ss") ?? "Sin señal",
                    c.UltimaTransaccion?.ToString("yyyy-MM-dd HH:mm") ?? "Ninguna"
                );

                var cellEstado = dgvCajeros.Rows[rowIdx].Cells["Estado"];
                if (c.Estado == "En línea")
                {
                    cellEstado.Style.ForeColor = Color.FromArgb(46, 213, 115);
                    cellEstado.Style.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                }
                else if (c.Estado == "Efectivo bajo")
                {
                    cellEstado.Style.ForeColor = Color.FromArgb(255, 177, 66);
                    cellEstado.Style.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                }
                else
                {
                    cellEstado.Style.ForeColor = Color.FromArgb(255, 71, 87);
                    cellEstado.Style.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                }
            }
        }

        private string ObtenerCajeroSeleccionado()
        {
            if (dgvCajeros.SelectedRows.Count == 0)
            {
                MessageBox.Show("Por favor seleccione un cajero de la lista.", "Atención", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return string.Empty;
            }
            return dgvCajeros.SelectedRows[0].Cells["Codigo"].Value?.ToString() ?? string.Empty;
        }

        private void AbrirCargaEfectivo(bool esInit)
        {
            string cod = ObtenerCajeroSeleccionado();
            if (string.IsNullOrEmpty(cod)) return;

            var form = new FormCargaEfectivo(_repo, cod, _admin.IdAdmin, esInit);
            form.ShowDialog();
            if (form.OperacionExitosa)
            {
                _ = _servidorTcp.EnviarComandoAsync(cod, TiposComando.ActualizarInventario);
                ActualizarDashboardSilencioso();
            }
        }

        private async Task CambiarEstadoRemotoCajero(string comando, string nuevoEstado)
        {
            string cod = ObtenerCajeroSeleccionado();
            if (string.IsNullOrEmpty(cod)) return;

            _repo.ActualizarEstadoCajero(cod, nuevoEstado);
            await _servidorTcp.EnviarComandoAsync(cod, comando);
            ActualizarDashboardSilencioso();
            MessageBox.Show($"Cajero {cod} actualizado a '{nuevoEstado}'. Comando remoto enviado.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        #endregion

        #region 3. Módulo Transacciones

        private void ConstruirModuloTransacciones()
        {
            pnlTransacciones = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            var pnlFiltros = new Panel { Dock = DockStyle.Top, Height = 50 };

            var lblFCaj = new Label { Text = "Cajero:", Left = 10, Top = 14, Width = 55 };
            cbFiltroCajeroTx = new ComboBox { Left = 70, Top = 10, Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cbFiltroCajeroTx.Items.AddRange(new object[] { "Todos", "ATM-01", "ATM-02", "ATM-03", "ATM-04", "ATM-05" });
            cbFiltroCajeroTx.SelectedIndex = 0;
            cbFiltroCajeroTx.SelectedIndexChanged += (s, e) => FiltrarTransacciones();

            var lblFTipo = new Label { Text = "Tipo:", Left = 195, Top = 14, Width = 45 };
            cbFiltroTipoTx = new ComboBox { Left = 245, Top = 10, Width = 195, DropDownStyle = ComboBoxStyle.DropDownList };
            cbFiltroTipoTx.Items.AddRange(new object[]
            {
                "Todos",
                "RETIRO",
                "DEPOSITO",
                "CAMBIO_PIN",
                "TRANSFERENCIA_ENVIADA",
                "TRANSFERENCIA_RECIBIDA",
                "TRANSFERENCIA_INTERBANCARIA"
            });
            cbFiltroTipoTx.SelectedIndex = 0;
            cbFiltroTipoTx.SelectedIndexChanged += (s, e) => FiltrarTransacciones();

            var lblFUsr = new Label { Text = "Buscar:", Left = 455, Top = 14, Width = 55 };
            txtFiltroUsuarioTx = new TextBox { Left = 515, Top = 10, Width = 180, Font = new Font("Segoe UI", 9.5f) };
            txtFiltroUsuarioTx.TextChanged += (s, e) => FiltrarTransacciones();

            var btnExportar = new Button
            {
                Text = "📥 Exportar Reporte",
                Left = 715,
                Top = 8,
                Width = 150,
                Height = 32,
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnExportar.FlatAppearance.BorderSize = 0;
            btnExportar.Click += (s, e) => ExportarTransaccionesReporte();

            pnlFiltros.Controls.AddRange(new Control[] { lblFCaj, cbFiltroCajeroTx, lblFTipo, cbFiltroTipoTx, lblFUsr, txtFiltroUsuarioTx, btnExportar });
            pnlTransacciones.Controls.Add(pnlFiltros);

            dgvTransacciones = CrearGridGenerico();
            dgvTransacciones.Dock = DockStyle.Fill;
            dgvTransacciones.Columns.Add("Fecha", "Fecha / Hora");
            dgvTransacciones.Columns.Add("Cajero", "Cajero");
            dgvTransacciones.Columns.Add("Usuario", "Usuario");
            dgvTransacciones.Columns.Add("Tarjeta", "Tarjeta");
            dgvTransacciones.Columns.Add("Tipo", "Tipo");
            dgvTransacciones.Columns.Add("Monto", "Monto");
            dgvTransacciones.Columns.Add("TokenTransferencia", "Token Transferencia");
            dgvTransacciones.Columns.Add("Desglose", "Desglose / Detalle");
            dgvTransacciones.Columns.Add("Resultado", "Resultado");
            dgvTransacciones.Columns.Add("Motivo", "Motivo / Info");

            pnlTransacciones.Controls.Add(dgvTransacciones);
            dgvTransacciones.BringToFront();
            pnlContenido.Controls.Add(pnlTransacciones);
        }

        private void FiltrarTransacciones()
        {
            string caj = cbFiltroCajeroTx.SelectedItem?.ToString() ?? "Todos";
            string tipo = cbFiltroTipoTx.SelectedItem?.ToString() ?? "Todos";
            string usr = txtFiltroUsuarioTx.Text.Trim();

            var txs = _repo.ObtenerTransacciones(null, caj, tipo, usr);
            ActualizarVistaTransacciones(txs);
        }

        private void ActualizarVistaTransacciones(List<TransaccionDto> txs)
        {
            dgvTransacciones.Rows.Clear();
            foreach (var t in txs)
            {
                int idx = dgvTransacciones.Rows.Add(
                    t.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                    t.Cajero,
                    t.Usuario,
                    t.TarjetaEnmascarada,
                    t.Tipo,
                    $"Q {t.Monto:N2}",
                    string.IsNullOrEmpty(t.TokenTransferencia) ? "-" : t.TokenTransferencia,
                    t.Desglose,
                    t.Aprobada ? "Aprobado (00)" : $"Rechazado ({t.CodigoRespuesta})",
                    t.MotivoRechazo ?? "-"
                );

                var cellRes = dgvTransacciones.Rows[idx].Cells["Resultado"];
                cellRes.Style.ForeColor = t.Aprobada ? Color.FromArgb(46, 213, 115) : Color.FromArgb(255, 71, 87);
                cellRes.Style.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            }
        }

        private void ExportarTransaccionesReporte()
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "Archivo CSV (*.csv)|*.csv|Reporte HTML (*.html)|*.html",
                FileName = $"Reporte_Transacciones_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    using var sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8);
                    sw.WriteLine("ID,Fecha,Cajero,Usuario,Tarjeta,Tipo,Monto,TokenTransferencia,Resultado,Motivo");
                    var txs = _repo.ObtenerTransacciones();
                    foreach (var t in txs)
                    {
                        sw.WriteLine($"{t.IdTransaccion},{t.Fecha:yyyy-MM-dd HH:mm:ss},{t.Cajero},{t.Usuario},{t.TarjetaEnmascarada},{t.Tipo},{t.Monto},{t.TokenTransferencia},{t.CodigoRespuesta},{t.MotivoRechazo}");
                    }
                    MessageBox.Show("Reporte exportado exitosamente.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al exportar: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        #endregion

        #region 3.5 Módulo Red Interbancaria, Cupos y Tokens de Transferencia

        private void ConstruirModuloInterbancario()
        {
            pnlInterbancario = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            // --- SECCIÓN SUPERIOR: BANCOS DE LA RED Y CUPOS COORDINADOS ---
            var pnlTopBancos = new Panel { Dock = DockStyle.Top, Height = 260 };

            var pnlAccionesBancos = new Panel { Dock = DockStyle.Top, Height = 45 };

            var infoRedActual = ClienteInterbancario.ObtenerInfoRedLocal();
            var lblSecBancos = new Label
            {
                Text = $"📡 Red: {infoRedActual.NombreRedSsid} ({infoRedActual.IpLocal})",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Left = 5,
                Top = 10,
                Width = 225
            };
            pnlAccionesBancos.Controls.Add(lblSecBancos);

            var btnEscanearLan = new Button
            {
                Text = "🔍 Escanear Red LAN",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(14, 165, 233),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 235,
                Top = 5,
                Width = 140,
                Height = 33,
                Cursor = Cursors.Hand
            };
            btnEscanearLan.FlatAppearance.BorderSize = 0;
            btnEscanearLan.Click += async (s, e) =>
            {
                btnEscanearLan.Enabled = false;
                btnEscanearLan.Text = "⏳ Escaneando...";
                try
                {
                    var propio = _repo.ObtenerInfoBancoPropio();
                    var diag = await ClienteInterbancario.EscanearSubredBancosAsync(propio.codigoBanco, _servidorTcp.Puerto);
                    lblSecBancos.Text = $"📡 Red: {diag.NombreRedSsid} ({diag.IpLocal})";

                    foreach (var det in diag.BancosDetectados)
                    {
                        _repo.GuardarBancoRed(det.codigoBanco, det.nombreBanco, det.ip, det.puerto, false, Math.Max(25000m, det.cupoDisp), null, true);
                    }

                    ActualizarVistaInterbancario(_repo.ObtenerBancosRed(), _repo.ObtenerHistorialTransferencias(null, txtFiltroTokenTransferencia?.Text));

                    string detalleBancos = diag.BancosDetectados.Count > 0
                        ? string.Join("\n", diag.BancosDetectados.Select(b => $"  • {b.codigoBanco} - {b.nombreBanco} ({b.ip}:{b.puerto})"))
                        : "  • Ningún otro servidor bancario respondió en el puerto 5000 dentro de esta subred.";

                    MessageBox.Show(
                        $"Red Conectada: {diag.NombreRedSsid} ({diag.TipoInterfaz})\n" +
                        $"IP Local: {diag.IpLocal} | Subred: {diag.SubredCidr} | Gateway: {diag.Gateway}\n" +
                        $"Otras Interfaces: {diag.OtrasInterfaces}\n\n" +
                        $"Bancos Detectados en la Misma Red ({diag.BancosDetectados.Count}):\n{detalleBancos}",
                        "Escaneo de Red Interbancaria",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                finally
                {
                    btnEscanearLan.Text = "🔍 Escanear Red LAN";
                    btnEscanearLan.Enabled = true;
                }
            };
            pnlAccionesBancos.Controls.Add(btnEscanearLan);

            var btnConfigBanco = new Button
            {
                Text = "➕ Configurar Banco / IP",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 180, 255),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 380,
                Top = 5,
                Width = 155,
                Height = 33,
                Cursor = Cursors.Hand
            };
            btnConfigBanco.FlatAppearance.BorderSize = 0;
            btnConfigBanco.Click += (s, e) => MostrarDialogoConfigurarBanco();
            pnlAccionesBancos.Controls.Add(btnConfigBanco);

            var btnPingBanco = new Button
            {
                Text = "📡 Ping Banco (TCP)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 540,
                Top = 5,
                Width = 135,
                Height = 33,
                Cursor = Cursors.Hand
            };
            btnPingBanco.FlatAppearance.BorderSize = 0;
            btnPingBanco.Click += async (s, e) => await ProbarPingBancoSeleccionadoAsync();
            pnlAccionesBancos.Controls.Add(btnPingBanco);

            var btnResetCupos = new Button
            {
                Text = "🔄 Reiniciar Cupos Hoy",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 196, 15),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 680,
                Top = 5,
                Width = 145,
                Height = 33,
                Cursor = Cursors.Hand
            };
            btnResetCupos.FlatAppearance.BorderSize = 0;
            btnResetCupos.Click += (s, e) =>
            {
                _repo.ReiniciarCuposDiariosBancos();
                ActualizarVistaInterbancario(_repo.ObtenerBancosRed(), _repo.ObtenerHistorialTransferencias(null, txtFiltroTokenTransferencia?.Text));
                MessageBox.Show("Los cupos diarios de transferencia de todos los bancos han sido reiniciados a Q 0.00 transferidos hoy.", "Cupos Reiniciados", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            pnlAccionesBancos.Controls.Add(btnResetCupos);

            var btnCuentasDemo = new Button
            {
                Text = "👤 Cuentas Demo",
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.FromArgb(155, 89, 182),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 830,
                Top = 5,
                Width = 130,
                Height = 33,
                Cursor = Cursors.Hand
            };
            btnCuentasDemo.FlatAppearance.BorderSize = 0;
            btnCuentasDemo.Click += (s, e) => MostrarDialogoCuentaExternaSimulada();
            pnlAccionesBancos.Controls.Add(btnCuentasDemo);

            var btnSimularEntrante = new Button
            {
                Text = "📥 Simular Rx Entrante",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(39, 174, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 970,
                Top = 5,
                Width = 150,
                Height = 33,
                Cursor = Cursors.Hand
            };
            btnSimularEntrante.FlatAppearance.BorderSize = 0;
            btnSimularEntrante.Click += (s, e) => MostrarDialogoSimularTransferenciaEntrante();
            pnlAccionesBancos.Controls.Add(btnSimularEntrante);

            dgvBancosRed = CrearGridGenerico();
            dgvBancosRed.Dock = DockStyle.Fill;
            dgvBancosRed.Columns.Add("CodigoBanco", "Código");
            dgvBancosRed.Columns.Add("NombreBanco", "Banco");
            dgvBancosRed.Columns.Add("Rol", "Rol");
            dgvBancosRed.Columns.Add("Endpoint", "IP Router : Puerto");
            dgvBancosRed.Columns.Add("LimiteDiario", "Cupo Límite Diario");
            dgvBancosRed.Columns.Add("TransferidoHoy", "Restado Hoy (Coordinado)");
            dgvBancosRed.Columns.Add("DisponibleHoy", "Cupo Disponible Hoy");
            dgvBancosRed.Columns.Add("Estado", "Estado");

            pnlTopBancos.Controls.Add(dgvBancosRed);
            pnlTopBancos.Controls.Add(pnlAccionesBancos);
            dgvBancosRed.BringToFront();

            // --- SECCIÓN INFERIOR: BITÁCORA DE TRANSFERENCIAS Y TOKENS ---
            var pnlBottomTransferencias = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };

            var pnlFiltroTrf = new Panel { Dock = DockStyle.Top, Height = 42 };

            var lblTitTrf = new Label
            {
                Text = "🔑 REGISTRO COMPARTIDO DE TRANSFERENCIAS & TOKENS (dbo.TransferenciaBancaria)",
                Font = new Font("Segoe UI", 9.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                Left = 5,
                Top = 10,
                Width = 570
            };
            pnlFiltroTrf.Controls.Add(lblTitTrf);

            var lblBusqToken = new Label
            {
                Text = "Buscar Token / Cuenta:",
                Left = 590,
                Top = 12,
                Width = 145
            };
            pnlFiltroTrf.Controls.Add(lblBusqToken);

            txtFiltroTokenTransferencia = new TextBox
            {
                Left = 740,
                Top = 8,
                Width = 240,
                Font = new Font("Consolas", 10f),
                BackColor = Color.FromArgb(30, 38, 52),
                ForeColor = Color.FromArgb(241, 196, 15)
            };
            txtFiltroTokenTransferencia.TextChanged += (s, e) =>
            {
                var trfs = _repo.ObtenerHistorialTransferencias(null, txtFiltroTokenTransferencia.Text.Trim());
                ActualizarGridTransferencias(trfs);
            };
            pnlFiltroTrf.Controls.Add(txtFiltroTokenTransferencia);

            dgvTransferencias = CrearGridGenerico();
            dgvTransferencias.Dock = DockStyle.Fill;
            dgvTransferencias.Columns.Add("Token", "Token Transferencia");
            dgvTransferencias.Columns.Add("Fecha", "Fecha / Hora");
            dgvTransferencias.Columns.Add("Tipo", "Tipo / Dir");
            dgvTransferencias.Columns.Add("Origen", "Banco / Cuenta Origen");
            dgvTransferencias.Columns.Add("Destino", "Banco / Cuenta Destino");
            dgvTransferencias.Columns.Add("Monto", "Monto");
            dgvTransferencias.Columns.Add("Estado", "Estado");
            dgvTransferencias.Columns.Add("CupoOrigen", "Cupo Rest. Origen");
            dgvTransferencias.Columns.Add("CupoDestino", "Cupo Rest. Destino");
            dgvTransferencias.Columns.Add("Concepto", "Concepto");

            pnlBottomTransferencias.Controls.Add(dgvTransferencias);
            pnlBottomTransferencias.Controls.Add(pnlFiltroTrf);
            dgvTransferencias.BringToFront();

            pnlInterbancario.Controls.Add(pnlBottomTransferencias);
            pnlInterbancario.Controls.Add(pnlTopBancos);
            pnlBottomTransferencias.BringToFront();
            pnlContenido.Controls.Add(pnlInterbancario);
        }

        private void ActualizarVistaInterbancario(List<BancoRedDto> bancos, List<TransferenciaBancariaDto> transferencias)
        {
            if (dgvBancosRed == null || dgvTransferencias == null) return;

            dgvBancosRed.Rows.Clear();
            foreach (var b in bancos)
            {
                int idx = dgvBancosRed.Rows.Add(
                    b.CodigoBanco,
                    b.NombreBanco,
                    b.EsPropio ? "⭐ NUESTRO BANCO" : "🌐 Banco Remoto",
                    $"{b.IP}:{b.Puerto}",
                    $"Q {b.LimiteDiario:N2}",
                    $"Q {b.TransferidoHoy:N2}",
                    $"Q {b.DisponibleHoy:N2}",
                    b.Activo ? "Activo" : "Inactivo"
                );

                var cellDisp = dgvBancosRed.Rows[idx].Cells["DisponibleHoy"];
                cellDisp.Style.ForeColor = b.DisponibleHoy > 0 ? Color.FromArgb(46, 213, 115) : Color.FromArgb(255, 71, 87);
                cellDisp.Style.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

                if (b.EsPropio)
                {
                    dgvBancosRed.Rows[idx].Cells["Rol"].Style.ForeColor = Color.FromArgb(0, 200, 255);
                    dgvBancosRed.Rows[idx].Cells["Rol"].Style.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                }
            }

            ActualizarGridTransferencias(transferencias);
        }

        private void ActualizarGridTransferencias(List<TransferenciaBancariaDto> transferencias)
        {
            dgvTransferencias.Rows.Clear();
            foreach (var t in transferencias)
            {
                int idx = dgvTransferencias.Rows.Add(
                    t.TokenTransferencia,
                    t.FechaCreacion.ToString("yyyy-MM-dd HH:mm:ss"),
                    $"{t.TipoTransferencia} ({t.Direccion})",
                    $"{t.BancoOrigen} - {t.CuentaOrigen} ({t.NombreOrigen})",
                    $"{t.BancoDestino} - {t.CuentaDestino} ({t.NombreDestino})",
                    $"Q {t.Monto:N2}",
                    t.Estado,
                    t.CupoRestanteBancoOrigen.HasValue ? $"Q {t.CupoRestanteBancoOrigen.Value:N2}" : "-",
                    t.CupoRestanteBancoDestino.HasValue ? $"Q {t.CupoRestanteBancoDestino.Value:N2}" : "-",
                    t.Concepto ?? "-"
                );

                dgvTransferencias.Rows[idx].Cells["Token"].Style.ForeColor = Color.FromArgb(241, 196, 15);
                dgvTransferencias.Rows[idx].Cells["Token"].Style.Font = new Font("Consolas", 9.5f, FontStyle.Bold);

                var cellEst = dgvTransferencias.Rows[idx].Cells["Estado"];
                bool ok = t.Estado == "APROBADA" || t.Estado == EstadosTransferencia.Completada;
                cellEst.Style.ForeColor = ok ? Color.FromArgb(46, 213, 115) : Color.FromArgb(255, 71, 87);
                cellEst.Style.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            }
        }

        private async Task ProbarPingBancoSeleccionadoAsync()
        {
            if (dgvBancosRed.SelectedRows.Count == 0)
            {
                MessageBox.Show("Seleccione un banco de la tabla.", "Atención", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string cod = dgvBancosRed.SelectedRows[0].Cells["CodigoBanco"].Value?.ToString() ?? "";
            var banco = _repo.ObtenerBancosRed().FirstOrDefault(x => x.CodigoBanco == cod);
            if (banco == null) return;

            var propio = _repo.ObtenerInfoBancoPropio();
            var req = new SolicitudIbPing
            {
                BancoOrigen = propio.codigoBanco
            };

            var resp = await ClienteInterbancario.EnviarMensajeTcpBancoAsync(banco.IP, banco.Puerto, req);
            if (resp != null && resp.Codigo == CodigosRespuesta.Aprobado)
            {
                MessageBox.Show(
                    $"Conexión exitosa con {banco.NombreBanco} ({banco.IP}:{banco.Puerto})\n\n" +
                    $"Mensaje: {resp.Mensaje}\n" +
                    $"Cupo Diario Disponible local: Q {banco.DisponibleHoy:N2}",
                    "Ping Interbancario Exitoso",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    $"No hubo respuesta TCP directa de {banco.NombreBanco} en {banco.IP}:{banco.Puerto}.\n" +
                    $"Detalle: {resp?.Mensaje ?? "Timeout"}\n\n" +
                    $"Nota: Mientras el servidor del otro compañero no esté en línea en el router, el sistema utilizará el padrón de cuentas externas de respaldo para permitir pruebas locales.",
                    "Aviso de Red Interbancaria",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void MostrarDialogoConfigurarBanco()
        {
            BancoRedDto? seleccionado = null;
            if (dgvBancosRed.SelectedRows.Count > 0)
            {
                string codSel = dgvBancosRed.SelectedRows[0].Cells["CodigoBanco"].Value?.ToString() ?? "";
                seleccionado = _repo.ObtenerBancosRed().FirstOrDefault(x => x.CodigoBanco == codSel);
            }

            using var dlg = new Form
            {
                Text = "Configurar Banco en la Red Interbancaria",
                Size = new Size(460, 420),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(22, 28, 40),
                ForeColor = Color.White
            };

            var lblCod = new Label { Text = "Código Banco (ej. BANC02):", Left = 20, Top = 20, Width = 180 };
            var txtCod = new TextBox { Left = 210, Top = 18, Width = 200, Text = seleccionado?.CodigoBanco ?? "BANC02" };

            var lblNom = new Label { Text = "Nombre del Banco:", Left = 20, Top = 60, Width = 180 };
            var txtNom = new TextBox { Left = 210, Top = 58, Width = 200, Text = seleccionado?.NombreBanco ?? "Banco Industrial Estudiantil" };

            var lblIp = new Label { Text = "IP en el Router:", Left = 20, Top = 100, Width = 180 };
            var txtIp = new TextBox { Left = 210, Top = 98, Width = 200, Text = seleccionado?.IP ?? "192.168.1.102" };

            var lblPto = new Label { Text = "Puerto TCP (ej. 5000):", Left = 20, Top = 140, Width = 180 };
            var txtPto = new TextBox { Left = 210, Top = 138, Width = 200, Text = (seleccionado?.Puerto ?? 5000).ToString() };

            var lblLim = new Label { Text = "Cupo Límite Diario (Q):", Left = 20, Top = 180, Width = 180 };
            var txtLim = new TextBox { Left = 210, Top = 178, Width = 200, Text = (seleccionado?.LimiteDiario ?? 25000m).ToString("0.00") };

            var lblSql = new Label { Text = "Cadena SQL (Opcional):", Left = 20, Top = 220, Width = 180 };
            var txtSql = new TextBox { Left = 210, Top = 218, Width = 200, Text = seleccionado?.CadenaConexionBD ?? "" };

            var chkPropio = new CheckBox { Text = "Es nuestro banco local", Left = 210, Top = 258, Width = 200, Checked = seleccionado?.EsPropio ?? false };
            var chkActivo = new CheckBox { Text = "Banco Activo en Red", Left = 210, Top = 288, Width = 200, Checked = seleccionado?.Activo ?? true };

            var btnGuardar = new Button
            {
                Text = "💾 Guardar Banco",
                Left = 210,
                Top = 328,
                Width = 200,
                Height = 36,
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            btnGuardar.Click += (s, e) =>
            {
                if (!int.TryParse(txtPto.Text.Trim(), out int puerto) || !decimal.TryParse(txtLim.Text.Trim(), out decimal limite))
                {
                    MessageBox.Show("Verifique el puerto y el límite diario.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _repo.GuardarBancoRed(
                    txtCod.Text.Trim().ToUpperInvariant(),
                    txtNom.Text.Trim(),
                    txtIp.Text.Trim(),
                    puerto,
                    chkPropio.Checked,
                    limite,
                    string.IsNullOrWhiteSpace(txtSql.Text) ? null : txtSql.Text.Trim(),
                    chkActivo.Checked);

                ActualizarVistaInterbancario(_repo.ObtenerBancosRed(), _repo.ObtenerHistorialTransferencias(null, txtFiltroTokenTransferencia?.Text));
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            };

            dlg.Controls.AddRange(new Control[] { lblCod, txtCod, lblNom, txtNom, lblIp, txtIp, lblPto, txtPto, lblLim, txtLim, lblSql, txtSql, chkPropio, chkActivo, btnGuardar });
            dlg.ShowDialog(this);
        }

        private void MostrarDialogoCuentaExternaSimulada()
        {
            using var dlg = new Form
            {
                Text = "Cuentas Externas de Respaldo / Demo (dbo.CuentaBancoExterno)",
                Size = new Size(560, 420),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(22, 28, 40),
                ForeColor = Color.White
            };

            var grid = CrearGridGenerico();
            grid.Top = 15;
            grid.Left = 15;
            grid.Width = 515;
            grid.Height = 200;
            grid.Columns.Add("Banco", "Banco");
            grid.Columns.Add("Cuenta", "Número de Cuenta");
            grid.Columns.Add("Titular", "Nombre Titular");
            grid.Columns.Add("Saldo", "Saldo Ref.");

            void Recargar()
            {
                grid.Rows.Clear();
                foreach (var c in _repo.ObtenerCuentasExternasSimuladas())
                {
                    grid.Rows.Add(c.CodigoBanco, c.NumeroCuenta, c.NombreTitular, $"Q {c.SaldoSimulado:N2}");
                }
            }
            Recargar();

            var lblB = new Label { Text = "Código Banco:", Left = 15, Top = 230, Width = 100 };
            var txtB = new TextBox { Left = 120, Top = 228, Width = 110, Text = "BANC02" };

            var lblC = new Label { Text = "No. Cuenta:", Left = 250, Top = 230, Width = 90 };
            var txtC = new TextBox { Left = 345, Top = 228, Width = 185, Text = "2002000003" };

            var lblN = new Label { Text = "Nombre Titular:", Left = 15, Top = 270, Width = 100 };
            var txtN = new TextBox { Left = 120, Top = 268, Width = 410, Text = "Estudiante Banco Par" };

            var btnAdd = new Button
            {
                Text = "➕ Guardar Cuenta Externa",
                Left = 120,
                Top = 315,
                Width = 220,
                Height = 36,
                BackColor = Color.FromArgb(0, 180, 255),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            btnAdd.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtB.Text) || string.IsNullOrWhiteSpace(txtC.Text) || string.IsNullOrWhiteSpace(txtN.Text))
                    return;

                _repo.GuardarCuentaExternaSimulada(txtB.Text.Trim().ToUpperInvariant(), txtC.Text.Trim(), txtN.Text.Trim(), 10000m);
                Recargar();
                MessageBox.Show("Cuenta externa registrada para pruebas interbancarias.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            dlg.Controls.AddRange(new Control[] { grid, lblB, txtB, lblC, txtC, lblN, txtN, btnAdd });
            dlg.ShowDialog(this);
        }

        private void MostrarDialogoSimularTransferenciaEntrante()
        {
            using var dlg = new Form
            {
                Text = "Simular Transferencia Interbancaria Entrante desde Otro Banco",
                Size = new Size(460, 380),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(22, 28, 40),
                ForeColor = Color.White
            };

            var lblTok = new Label { Text = "Token del Otro Banco:", Left = 20, Top = 20, Width = 170 };
            var txtTok = new TextBox { Left = 200, Top = 18, Width = 220, Text = RepositorioServidor.GenerarTokenTransferenciaUnico() };

            var lblBo = new Label { Text = "Banco Origen:", Left = 20, Top = 60, Width = 170 };
            var txtBo = new TextBox { Left = 200, Top = 58, Width = 220, Text = "BANC02" };

            var lblCo = new Label { Text = "Cuenta Origen:", Left = 20, Top = 100, Width = 170 };
            var txtCo = new TextBox { Left = 200, Top = 98, Width = 220, Text = "2002000001" };

            var lblNo = new Label { Text = "Titular Origen:", Left = 20, Top = 140, Width = 170 };
            var txtNo = new TextBox { Left = 200, Top = 138, Width = 220, Text = "Roberto Gómez (BANC02)" };

            var lblCd = new Label { Text = "Cuenta Destino (Local):", Left = 20, Top = 180, Width = 170 };
            var txtCd = new TextBox { Left = 200, Top = 178, Width = 220, Text = "1001000001" };

            var lblMo = new Label { Text = "Monto a Acreditar (Q):", Left = 20, Top = 220, Width = 170 };
            var txtMo = new TextBox { Left = 200, Top = 218, Width = 220, Text = "250.00" };

            var btnEjecutar = new Button
            {
                Text = "📥 Procesar Transferencia Entrante",
                Left = 140,
                Top = 275,
                Width = 280,
                Height = 40,
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnEjecutar.Click += (s, e) =>
            {
                if (!decimal.TryParse(txtMo.Text.Trim(), out decimal monto) || monto <= 0)
                {
                    MessageBox.Show("Ingrese un monto válido.", "Atención", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var propio = _repo.ObtenerInfoBancoPropio();
                var res = _repo.RecibirTransferenciaInterbancariaEntrante(new SolicitudIbTransferir
                {
                    TokenTransferencia = txtTok.Text.Trim(),
                    BancoOrigen = txtBo.Text.Trim().ToUpperInvariant(),
                    CuentaOrigen = txtCo.Text.Trim(),
                    NombreOrigen = txtNo.Text.Trim(),
                    BancoDestino = propio.codigoBanco,
                    CuentaDestino = txtCd.Text.Trim(),
                    Monto = monto,
                    Concepto = "Transferencia entrante interbancaria"
                });

                if (res.codigo == CodigosRespuesta.Aprobado && res.datos != null)
                {
                    ActualizarDashboardSilencioso();
                    MessageBox.Show(
                        $"¡Transferencia Entrante Aprobada!\n\n" +
                        $"• Token Guardado: {res.datos.TokenTransferencia}\n" +
                        $"• Beneficiario Local: {res.datos.NombreDestino}\n" +
                        $"• Cupo Restante Nuestro Banco: Q {res.datos.CupoRestanteBancoDestino:N2}",
                        "Transferencia Entrante Registrada",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    dlg.Close();
                }
                else
                {
                    MessageBox.Show($"Rechazada ({res.codigo}): {res.mensaje}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            dlg.Controls.AddRange(new Control[] { lblTok, txtTok, lblBo, txtBo, lblCo, txtCo, lblNo, txtNo, lblCd, txtCd, lblMo, txtMo, btnEjecutar });
            dlg.ShowDialog(this);
        }

        #endregion

        #region 4. Módulo Gestión de Usuarios

        private void ConstruirModuloUsuarios()
        {
            pnlUsuarios = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            var pnlAccionesU = new Panel { Dock = DockStyle.Top, Height = 45 };

            var btnCrearU = new Button
            {
                Text = "➕ Crear Usuario",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 180, 255),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Width = 140,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnCrearU.FlatAppearance.BorderSize = 0;
            btnCrearU.Click += (s, e) =>
            {
                var f = new FormUsuario(_repo, ModoFormUsuario.Crear, _admin.IdAdmin);
                f.ShowDialog();
                if (f.OperacionExitosa) ActualizarVistaUsuarios(_repo.ObtenerUsuarios());
            };
            pnlAccionesU.Controls.Add(btnCrearU);

            var btnCambiarTar = new Button
            {
                Text = "💳 Cambiar Tarjeta",
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 150,
                Width = 140,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnCambiarTar.FlatAppearance.BorderSize = 0;
            btnCambiarTar.Click += (s, e) =>
            {
                var u = ObtenerUsuarioSeleccionado();
                if (u == null) return;
                var f = new FormUsuario(_repo, ModoFormUsuario.CambiarTarjeta, _admin.IdAdmin, u);
                f.ShowDialog();
                if (f.OperacionExitosa) ActualizarVistaUsuarios(_repo.ObtenerUsuarios());
            };
            pnlAccionesU.Controls.Add(btnCambiarTar);

            var btnCambiarLim = new Button
            {
                Text = "📊 Modificar Límite",
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(241, 196, 15),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Left = 300,
                Width = 140,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnCambiarLim.FlatAppearance.BorderSize = 0;
            btnCambiarLim.Click += (s, e) =>
            {
                var u = ObtenerUsuarioSeleccionado();
                if (u == null) return;
                var f = new FormUsuario(_repo, ModoFormUsuario.CambiarLimite, _admin.IdAdmin, u);
                f.ShowDialog();
                if (f.OperacionExitosa) ActualizarVistaUsuarios(_repo.ObtenerUsuarios());
            };
            pnlAccionesU.Controls.Add(btnCambiarLim);

            var btnBloquear = new Button
            {
                Text = "🔒 Bloquear / Desbloquear",
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 450,
                Width = 180,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnBloquear.FlatAppearance.BorderSize = 0;
            btnBloquear.Click += (s, e) =>
            {
                var u = ObtenerUsuarioSeleccionado();
                if (u == null) return;
                bool nuevoBloqueo = u.Estado == "Activo";
                _repo.CambiarEstadoBloqueoUsuario(u.IdUsuario, nuevoBloqueo, _admin.IdAdmin);
                ActualizarVistaUsuarios(_repo.ObtenerUsuarios());
                MessageBox.Show($"Usuario {u.Nombre} ahora está {(nuevoBloqueo ? "BLOQUEADO" : "ACTIVO")}.", "Estado Actualizado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            pnlAccionesU.Controls.Add(btnBloquear);

            var btnPinTemp = new Button
            {
                Text = "🔑 Asignar PIN Temporal",
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(155, 89, 182),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 640,
                Width = 170,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnPinTemp.FlatAppearance.BorderSize = 0;
            btnPinTemp.Click += (s, e) =>
            {
                var u = ObtenerUsuarioSeleccionado();
                if (u == null) return;
                var f = new FormUsuario(_repo, ModoFormUsuario.AsignarPinTemporal, _admin.IdAdmin, u);
                f.ShowDialog();
                if (f.OperacionExitosa) ActualizarVistaUsuarios(_repo.ObtenerUsuarios());
            };
            pnlAccionesU.Controls.Add(btnPinTemp);

            pnlUsuarios.Controls.Add(pnlAccionesU);

            dgvUsuarios = CrearGridGenerico();
            dgvUsuarios.Dock = DockStyle.Fill;
            dgvUsuarios.Columns.Add("IdUsuario", "ID");
            dgvUsuarios.Columns.Add("NumeroCuenta", "No. Cuenta Bancaria");
            dgvUsuarios.Columns.Add("Nombre", "Nombre");
            dgvUsuarios.Columns.Add("Saldo", "Saldo Actual");
            dgvUsuarios.Columns.Add("LimiteDiario", "Límite Diario");
            dgvUsuarios.Columns.Add("RetiradoHoy", "Retirado Hoy (Global)");
            dgvUsuarios.Columns.Add("DisponibleHoy", "Disponible Restante");
            dgvUsuarios.Columns.Add("Tarjeta", "Tarjeta Activa");
            dgvUsuarios.Columns.Add("Estado", "Estado");
            dgvUsuarios.Columns.Add("Intentos", "Intentos Fallidos");

            pnlUsuarios.Controls.Add(dgvUsuarios);
            dgvUsuarios.BringToFront();
            pnlContenido.Controls.Add(pnlUsuarios);
        }

        private UsuarioDto? ObtenerUsuarioSeleccionado()
        {
            if (dgvUsuarios.SelectedRows.Count == 0)
            {
                MessageBox.Show("Seleccione un usuario de la tabla.", "Atención", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            int id = Convert.ToInt32(dgvUsuarios.SelectedRows[0].Cells["IdUsuario"].Value);
            return _repo.ObtenerUsuarios().FirstOrDefault(x => x.IdUsuario == id);
        }

        private void ActualizarVistaUsuarios(List<UsuarioDto> usuarios)
        {
            dgvUsuarios.Rows.Clear();
            foreach (var u in usuarios)
            {
                int idx = dgvUsuarios.Rows.Add(
                    u.IdUsuario,
                    u.NumeroCuenta,
                    u.Nombre,
                    $"Q {u.Saldo:N2}",
                    $"Q {u.LimiteDiario:N2}",
                    $"Q {u.RetiradoHoy:N2}",
                    $"Q {u.DisponibleHoy:N2}",
                    u.TarjetaEnmascarada,
                    u.Estado,
                    u.IntentosFallidos
                );

                dgvUsuarios.Rows[idx].Cells["NumeroCuenta"].Style.ForeColor = Color.FromArgb(0, 200, 255);
                dgvUsuarios.Rows[idx].Cells["NumeroCuenta"].Style.Font = new Font("Consolas", 9.5f, FontStyle.Bold);

                var cellEst = dgvUsuarios.Rows[idx].Cells["Estado"];
                cellEst.Style.ForeColor = u.Estado == "Activo" ? Color.FromArgb(46, 213, 115) : Color.FromArgb(255, 71, 87);
                cellEst.Style.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            }
        }

        #endregion

        #region 5. Módulo Consultas Fase 1

        private void ConstruirModuloConsultasFase1()
        {
            pnlConsultasFase1 = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };

            var grpIndividual = new GroupBox
            {
                Text = "🔍 Consulta Individual por Usuario (Fase I)",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Top = 15,
                Left = 20,
                Width = 980,
                Height = 260
            };

            var lblBuscar = new Label { Text = "Número de Tarjeta, Cuenta o ID:", Left = 20, Top = 35, Width = 260, Font = new Font("Segoe UI", 9.5f) };
            txtConsultaUsuario = new TextBox { Left = 290, Top = 32, Width = 220, Font = new Font("Segoe UI", 10f), Text = "1111222233334444" };
            var btnConsultar = new Button
            {
                Text = "Consultar",
                Left = 525,
                Top = 30,
                Width = 120,
                Height = 32,
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnConsultar.Click += (s, e) => EjecutarConsultaUsuarioFase1();

            lblDetalleUsuarioFase1 = new Label
            {
                Top = 75,
                Left = 20,
                Width = 940,
                Height = 165,
                Font = new Font("Consolas", 10f),
                ForeColor = Color.FromArgb(220, 235, 250),
                BackColor = Color.FromArgb(26, 32, 44),
                Padding = new Padding(12),
                Text = "Ingrese una tarjeta o cuenta para ver el detalle de saldo, retirado hoy, disponible y último acceso."
            };

            grpIndividual.Controls.AddRange(new Control[] { lblBuscar, txtConsultaUsuario, btnConsultar, lblDetalleUsuarioFase1 });
            pnlConsultasFase1.Controls.Add(grpIndividual);

            var grpGeneral = new GroupBox
            {
                Text = "📊 Control General de Usuarios (Fase I)",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                Top = 290,
                Left = 20,
                Width = 980,
                Height = 260
            };

            lblControlUsuariosFase1 = new Label
            {
                Top = 30,
                Left = 20,
                Width = 940,
                Height = 210,
                Font = new Font("Consolas", 10f),
                ForeColor = Color.FromArgb(220, 235, 250),
                BackColor = Color.FromArgb(26, 32, 44),
                Padding = new Padding(12),
                Text = "Cargando métricas consolidadas..."
            };

            grpGeneral.Controls.Add(lblControlUsuariosFase1);
            pnlConsultasFase1.Controls.Add(grpGeneral);

            pnlContenido.Controls.Add(pnlConsultasFase1);
        }

        private void EjecutarConsultaUsuarioFase1()
        {
            string tarj = txtConsultaUsuario.Text.Trim();
            var usuarios = _repo.ObtenerUsuarios();
            var u = usuarios.FirstOrDefault(x => x.NumeroTarjeta == tarj || x.NumeroCuenta == tarj || x.IdUsuario.ToString() == tarj);

            if (u == null)
            {
                lblDetalleUsuarioFase1.Text = "No se encontró ningún usuario con ese número de tarjeta, cuenta o ID.";
                return;
            }

            var txs = _repo.UltimasTransacciones(u.NumeroTarjeta, 5);
            string ultimas = string.Join("\n  • ", txs.Select(t => $"{t.Fecha} | {t.Cajero} | {t.Tipo} | Q{t.Monto:N2} | {(string.IsNullOrEmpty(t.TokenTransferencia) ? $"Cod:{t.CodigoRespuesta}" : $"Token:{t.TokenTransferencia}")}"));

            lblDetalleUsuarioFase1.Text =
                $"USUARIO: {u.Nombre} (ID: {u.IdUsuario} | Cuenta: {u.NumeroCuenta}) - Estado: {u.Estado}\n" +
                $"Tarjeta Activa: **** {u.NumeroTarjeta[^4..]} | Creado: {u.FechaCreacion:yyyy-MM-dd}\n" +
                $"------------------------------------------------------------------------\n" +
                $"• Saldo Actual:            Q {u.Saldo:N2}\n" +
                $"• Límite Diario Asignado:  Q {u.LimiteDiario:N2}\n" +
                $"• Cantidad Retirada Hoy:   Q {u.RetiradoHoy:N2} (En toda la red de cajeros)\n" +
                $"• Monto Máximo Disponible: Q {u.DisponibleHoy:N2}\n" +
                $"------------------------------------------------------------------------\n" +
                $"Últimas Transacciones:\n  • {ultimas}";

            // Actualizar resumen general
            var resumen = _repo.ObtenerResumenDiario();
            var cambiosPin = _repo.ObtenerCambiosPin();
            var todasTx = _repo.ObtenerTransacciones();
            var ultimaOp = todasTx.FirstOrDefault();

            lblControlUsuariosFase1.Text =
                $"MÉTRICAS GLOBALES DE USUARIOS (FASE I & II):\n" +
                $"------------------------------------------------------------------------\n" +
                $"• Total Retirado por Todos los Usuarios Hoy: Q {resumen.TotalRetiradoHoy:N2}\n" +
                $"• Total Depositado Hoy:                      Q {resumen.TotalDepositadoHoy:N2}\n" +
                $"• Promedio Depositado por Usuario:           Q {resumen.PromedioDepositadoPorUsuario:N2}\n" +
                $"• Usuarios que Cambiaron su PIN Hoy:         {cambiosPin.Count} evento(s)\n" +
                $"• Último Usuario que Realizó una Operación:  {(ultimaOp != null ? $"{ultimaOp.Usuario} ({ultimaOp.Tipo} Q{ultimaOp.Monto:N2} en {ultimaOp.Cajero} a las {ultimaOp.Fecha:HH:mm:ss})" : "Ninguno aún")}\n" +
                $"• Total de Usuarios Registrados en el Sistema: {usuarios.Count} usuarios activos\n";
        }

        #endregion

        #region 6. Módulo Alertas

        private void ConstruirModuloAlertas()
        {
            pnlAlertas = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 45 };

            var btnAtender = new Button
            {
                Text = "✓ Marcar como Atendida",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 213, 115),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Width = 200,
                Height = 35,
                Cursor = Cursors.Hand
            };
            btnAtender.FlatAppearance.BorderSize = 0;
            btnAtender.Click += (s, e) =>
            {
                if (dgvAlertas.SelectedRows.Count == 0) return;
                int id = Convert.ToInt32(dgvAlertas.SelectedRows[0].Cells["IdAlerta"].Value);
                _repo.MarcarAlertaAtendida(id, _admin.IdAdmin);
                ActualizarVistaAlertas(_repo.ObtenerAlertas());
            };
            pnlTop.Controls.Add(btnAtender);

            pnlAlertas.Controls.Add(pnlTop);

            dgvAlertas = CrearGridGenerico();
            dgvAlertas.Dock = DockStyle.Fill;
            dgvAlertas.Columns.Add("IdAlerta", "ID");
            dgvAlertas.Columns.Add("Fecha", "Fecha / Hora");
            dgvAlertas.Columns.Add("Tipo", "Tipo de Alerta");
            dgvAlertas.Columns.Add("Cajero", "Cajero");
            dgvAlertas.Columns.Add("Descripcion", "Descripción");
            dgvAlertas.Columns.Add("Estado", "Estado");
            dgvAlertas.Columns.Add("AtendidaPor", "Atendida Por");

            pnlAlertas.Controls.Add(dgvAlertas);
            dgvAlertas.BringToFront();
            pnlContenido.Controls.Add(pnlAlertas);
        }

        private void ActualizarVistaAlertas(List<AlertaDto> alertas)
        {
            dgvAlertas.Rows.Clear();
            foreach (var a in alertas)
            {
                int idx = dgvAlertas.Rows.Add(
                    a.IdAlerta,
                    a.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                    a.Tipo,
                    a.Cajero ?? "Sistema",
                    a.Descripcion,
                    a.Atendida ? "Atendida" : "PENDIENTE",
                    a.AdminAtendio ?? "-"
                );

                var cellEst = dgvAlertas.Rows[idx].Cells["Estado"];
                cellEst.Style.ForeColor = a.Atendida ? Color.FromArgb(46, 213, 115) : Color.FromArgb(255, 71, 87);
                cellEst.Style.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            }
        }

        #endregion

        #region 7. Módulo Bitácora de Auditoría

        private void ConstruirModuloBitacora()
        {
            pnlBitacora = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            var pnlFiltrosB = new Panel { Dock = DockStyle.Top, Height = 45 };

            var lblOB = new Label { Text = "Origen:", Left = 10, Top = 14, Width = 60 };
            var cbOrigen = new ComboBox { Left = 75, Top = 10, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            cbOrigen.Items.AddRange(new object[] { "Todos", "Cajero", "Servidor", "Administrador", "RedInterbancaria" });
            cbOrigen.SelectedIndex = 0;

            var lblBB = new Label { Text = "Buscar:", Left = 230, Top = 14, Width = 60 };
            var txtBuscarB = new TextBox { Left = 295, Top = 10, Width = 220, Font = new Font("Segoe UI", 9.5f) };

            cbOrigen.SelectedIndexChanged += (s, e) => ActualizarVistaBitacora(_repo.ObtenerBitacora(cbOrigen.SelectedItem?.ToString(), txtBuscarB.Text));
            txtBuscarB.TextChanged += (s, e) => ActualizarVistaBitacora(_repo.ObtenerBitacora(cbOrigen.SelectedItem?.ToString(), txtBuscarB.Text));

            pnlFiltrosB.Controls.AddRange(new Control[] { lblOB, cbOrigen, lblBB, txtBuscarB });
            pnlBitacora.Controls.Add(pnlFiltrosB);

            dgvBitacora = CrearGridGenerico();
            dgvBitacora.Dock = DockStyle.Fill;
            dgvBitacora.Columns.Add("IdBitacora", "ID");
            dgvBitacora.Columns.Add("Fecha", "Fecha / Hora");
            dgvBitacora.Columns.Add("Origen", "Origen");
            dgvBitacora.Columns.Add("Accion", "Acción");
            dgvBitacora.Columns.Add("Detalle", "Detalle");

            pnlBitacora.Controls.Add(dgvBitacora);
            dgvBitacora.BringToFront();
            pnlContenido.Controls.Add(pnlBitacora);
        }

        private void ActualizarVistaBitacora(List<BitacoraDto> bitacora)
        {
            dgvBitacora.Rows.Clear();
            foreach (var b in bitacora)
            {
                dgvBitacora.Rows.Add(
                    b.IdBitacora,
                    b.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                    b.Origen,
                    b.Accion,
                    b.Detalle
                );
            }
        }

        #endregion

        #region 8. Módulo Configuración

        private void ConstruirModuloConfiguracion()
        {
            pnlConfiguracion = new Panel { Dock = DockStyle.Fill, Padding = new Padding(25), AutoScroll = true };

            var lblT = new Label
            {
                Text = "⚙️ Parámetros de Operación del Servidor",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Top = 15,
                Left = 20,
                Width = 500,
                Height = 30
            };
            pnlConfiguracion.Controls.Add(lblT);

            var parametros = _repo.ObtenerParametros();
            int y = 60;

            foreach (var p in parametros)
            {
                var lblClave = new Label
                {
                    Text = p.Clave + ":",
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(220, 230, 245),
                    Left = 25,
                    Top = y + 4,
                    Width = 220
                };

                var txtVal = new TextBox
                {
                    Text = p.Valor,
                    Font = new Font("Segoe UI", 10f),
                    BackColor = Color.FromArgb(32, 40, 54),
                    ForeColor = Color.White,
                    Left = 250,
                    Top = y,
                    Width = 180
                };

                var btnGuardarP = new Button
                {
                    Text = "Guardar",
                    Font = new Font("Segoe UI", 8.5f),
                    BackColor = Color.FromArgb(0, 122, 255),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Left = 445,
                    Top = y - 1,
                    Width = 85,
                    Height = 28
                };
                string claveP = p.Clave;
                btnGuardarP.Click += (s, e) =>
                {
                    _repo.GuardarParametro(claveP, txtVal.Text.Trim());
                    MessageBox.Show($"Parámetro '{claveP}' actualizado.", "Configuración", MessageBoxButtons.OK, MessageBoxIcon.Information);
                };

                var lblDesc = new Label
                {
                    Text = p.Descripcion,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                    ForeColor = Color.Gray,
                    Left = 545,
                    Top = y + 4,
                    Width = 400
                };

                pnlConfiguracion.Controls.AddRange(new Control[] { lblClave, txtVal, btnGuardarP, lblDesc });
                y += 45;
            }

            pnlContenido.Controls.Add(pnlConfiguracion);
        }

        #endregion

        #region Helpers de UI

        private DataGridView CrearGridGenerico()
        {
            var grid = new DataGridView
            {
                BackgroundColor = Color.FromArgb(24, 28, 38),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(40, 48, 64),
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false
            };

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 38, 52);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0, 200, 255);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            grid.ColumnHeadersHeight = 35;

            grid.DefaultCellStyle.BackColor = Color.FromArgb(24, 28, 38);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(225, 235, 245);
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9f);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 122, 255);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.RowTemplate.Height = 32;

            return grid;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timerAutoRefresh?.Stop();
            timerAutoRefresh?.Dispose();
            timerAnimacionBoton?.Stop();
            timerAnimacionBoton?.Dispose();
            _servidorTcp?.Detener();
            base.OnFormClosing(e);
        }

        #endregion
    }
}
