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
            this.Size = new Size(1300, 800);
            this.MinimumSize = new Size(1150, 700);
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
                Width = 350
            };
            pnlHeader.Controls.Add(lblTituloModulo);

            lblEstadoServidor = new Label
            {
                Text = $"🟢 Servidor TCP: Puerto {_servidorTcp.Puerto} (En Línea)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                Left = 600,
                Top = 22,
                Width = 280
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
                Left = 900,
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
                Left = 1080,
                Top = 14,
                Width = 180,
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
            AgregarBotonMenu("👥 Gestión Usuarios", () => MostrarModulo(pnlUsuarios, "👥 Gestión de Usuarios y Tarjetas"), ref btnY);
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

            // Crear los módulos individuales
            ConstruirModuloResumen();
            ConstruirModuloMonitorCajeros();
            ConstruirModuloTransacciones();
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
                Height = 42,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 40, 56);
            btn.Click += (s, e) => accion();
            pnlSidebar.Controls.Add(btn);
            y += 44;
        }

        private void MostrarModulo(Panel panel, string titulo)
        {
            pnlContenido.SuspendLayout();
            pnlResumen.Visible = false;
            pnlMonitorCajeros.Visible = false;
            pnlTransacciones.Visible = false;
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
                var alertas = _repo.ObtenerAlertas();

                this.BeginInvoke(new Action(() =>
                {
                    ActualizarVistaResumen(resumen, txs, cajeros);
                    ActualizarVistaCajeros(cajeros);
                }));
            });
        }

        private async Task CargarDatosCompletosAsync()
        {
            var resumen = await Task.Run(() => _repo.ObtenerResumenDiario());
            var cajeros = await Task.Run(() => _repo.ObtenerEstadoCajeros());
            var txs = await Task.Run(() => _repo.ObtenerTransacciones());
            var usuarios = await Task.Run(() => _repo.ObtenerUsuarios());
            var alertas = await Task.Run(() => _repo.ObtenerAlertas());
            var bitacora = await Task.Run(() => _repo.ObtenerBitacora());

            ActualizarVistaResumen(resumen, txs, cajeros);
            ActualizarVistaCajeros(cajeros);
            ActualizarVistaTransacciones(txs);
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
            cbFiltroTipoTx = new ComboBox { Left = 245, Top = 10, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            cbFiltroTipoTx.Items.AddRange(new object[] { "Todos", "RETIRO", "DEPOSITO", "CAMBIO_PIN" });
            cbFiltroTipoTx.SelectedIndex = 0;
            cbFiltroTipoTx.SelectedIndexChanged += (s, e) => FiltrarTransacciones();

            var lblFUsr = new Label { Text = "Buscar:", Left = 380, Top = 14, Width = 55 };
            txtFiltroUsuarioTx = new TextBox { Left = 440, Top = 10, Width = 180, Font = new Font("Segoe UI", 9.5f) };
            txtFiltroUsuarioTx.TextChanged += (s, e) => FiltrarTransacciones();

            var btnExportar = new Button
            {
                Text = "📥 Exportar Reporte",
                Left = 640,
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
            dgvTransacciones.Columns.Add("Desglose", "Desglose Billetes");
            dgvTransacciones.Columns.Add("Resultado", "Resultado");
            dgvTransacciones.Columns.Add("Motivo", "Motivo de Rechazo");

            pnlTransacciones.Controls.Add(dgvTransacciones);
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
                    sw.WriteLine("ID,Fecha,Cajero,Usuario,Tarjeta,Tipo,Monto,Resultado,Motivo");
                    var txs = _repo.ObtenerTransacciones();
                    foreach (var t in txs)
                    {
                        sw.WriteLine($"{t.IdTransaccion},{t.Fecha:yyyy-MM-dd HH:mm:ss},{t.Cajero},{t.Usuario},{t.TarjetaEnmascarada},{t.Tipo},{t.Monto},{t.CodigoRespuesta},{t.MotivoRechazo}");
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
            dgvUsuarios.Columns.Add("Nombre", "Nombre");
            dgvUsuarios.Columns.Add("Saldo", "Saldo Actual");
            dgvUsuarios.Columns.Add("LimiteDiario", "Límite Diario");
            dgvUsuarios.Columns.Add("RetiradoHoy", "Retirado Hoy (Global)");
            dgvUsuarios.Columns.Add("DisponibleHoy", "Disponible Restante");
            dgvUsuarios.Columns.Add("Tarjeta", "Tarjeta Activa");
            dgvUsuarios.Columns.Add("Estado", "Estado");
            dgvUsuarios.Columns.Add("Intentos", "Intentos Fallidos");

            pnlUsuarios.Controls.Add(dgvUsuarios);
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
                    u.Nombre,
                    $"Q {u.Saldo:N2}",
                    $"Q {u.LimiteDiario:N2}",
                    $"Q {u.RetiradoHoy:N2}",
                    $"Q {u.DisponibleHoy:N2}",
                    u.TarjetaEnmascarada,
                    u.Estado,
                    u.IntentosFallidos
                );

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

            var lblBuscar = new Label { Text = "Número de Tarjeta (16 dígitos) o ID:", Left = 20, Top = 35, Width = 260, Font = new Font("Segoe UI", 9.5f) };
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
                Text = "Ingrese una tarjeta para ver el detalle de saldo, retirado hoy, disponible y último acceso."
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
            var u = usuarios.FirstOrDefault(x => x.NumeroTarjeta == tarj || x.IdUsuario.ToString() == tarj);

            if (u == null)
            {
                lblDetalleUsuarioFase1.Text = "No se encontró ningún usuario con ese número de tarjeta o ID.";
                return;
            }

            var txs = _repo.UltimasTransacciones(u.NumeroTarjeta, 5);
            string ultimas = string.Join("\n  • ", txs.Select(t => $"{t.Fecha} | {t.Cajero} | {t.Tipo} | Q{t.Monto:N2} | Cod:{t.CodigoRespuesta}"));

            lblDetalleUsuarioFase1.Text =
                $"USUARIO: {u.Nombre} (ID: {u.IdUsuario}) - Estado: {u.Estado}\n" +
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
            cbOrigen.Items.AddRange(new object[] { "Todos", "Cajero", "Servidor", "Administrador" });
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
