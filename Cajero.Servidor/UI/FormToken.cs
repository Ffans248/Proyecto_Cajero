using System;
using System.Drawing;
using System.Windows.Forms;

namespace Cajero.Servidor.UI
{
    public class FormToken : Form
    {
        private Label lblTitulo = null!;
        private Label lblMensajeSimulado = null!;
        private Label lblToken = null!;
        private Label lblDetalles = null!;
        private Label lblContador = null!;
        private ProgressBar pbProgreso = null!;
        private Button btnCopiar = null!;
        private Button btnCerrar = null!;
        private System.Windows.Forms.Timer timerCuentaRegresiva = null!;
        private int _segundosRestantes = 120;
        private readonly string _token;

        public FormToken(string token, string usuario, string cajero, DateTime expiracion)
        {
            _token = token;
            _segundosRestantes = Math.Max(1, (int)(expiracion - DateTime.Now).TotalSeconds);
            InicializarComponentes(token, usuario, cajero, expiracion);
        }

        private void InicializarComponentes(string token, string usuario, string cajero, DateTime expiracion)
        {
            this.Text = "Simulación Móvil - Token de Seguridad";
            this.Size = new Size(420, 390);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(24, 28, 36);
            this.ForeColor = Color.White;

            // Icono / Encabezado
            lblTitulo = new Label
            {
                Text = "📱 MENSAJE DE SEGURIDAD BANCARIA",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 45
            };

            lblMensajeSimulado = new Label
            {
                Text = $"Estimado/a {usuario},\nse ha solicitado acceso en el cajero {cajero}.\nUse el siguiente código de 6 dígitos:",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(210, 215, 225),
                TextAlign = ContentAlignment.MiddleCenter,
                Top = 50,
                Left = 20,
                Width = 365,
                Height = 55
            };

            // Badge del Token
            lblToken = new Label
            {
                Text = token,
                Font = new Font("Consolas", 32, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                BackColor = Color.FromArgb(32, 40, 54),
                TextAlign = ContentAlignment.MiddleCenter,
                Top = 115,
                Left = 60,
                Width = 285,
                Height = 70
            };

            lblDetalles = new Label
            {
                Text = "⚠️ Válido para un solo uso. No lo comparta con nadie.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(255, 177, 66),
                TextAlign = ContentAlignment.MiddleCenter,
                Top = 195,
                Left = 20,
                Width = 365,
                Height = 25
            };

            // Barra de progreso y contador
            lblContador = new Label
            {
                Text = $"Expira en: {_segundosRestantes} segundos",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(170, 185, 205),
                TextAlign = ContentAlignment.MiddleCenter,
                Top = 225,
                Left = 20,
                Width = 365,
                Height = 20
            };

            pbProgreso = new ProgressBar
            {
                Top = 250,
                Left = 30,
                Width = 345,
                Height = 12,
                Maximum = 120,
                Value = Math.Min(120, _segundosRestantes)
            };

            btnCopiar = new Button
            {
                Text = "📋 Copiar Token",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Top = 280,
                Left = 40,
                Width = 150,
                Height = 40,
                Cursor = Cursors.Hand
            };
            btnCopiar.FlatAppearance.BorderSize = 0;
            btnCopiar.Click += (s, e) =>
            {
                Clipboard.SetText(_token);
                btnCopiar.Text = "✓ Copiado!";
                btnCopiar.BackColor = Color.FromArgb(46, 213, 115);
            };

            btnCerrar = new Button
            {
                Text = "Cerrar",
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Top = 280,
                Left = 210,
                Width = 150,
                Height = 40,
                Cursor = Cursors.Hand
            };
            btnCerrar.FlatAppearance.BorderSize = 0;
            btnCerrar.Click += (s, e) => this.Close();

            this.Controls.Add(lblTitulo);
            this.Controls.Add(lblMensajeSimulado);
            this.Controls.Add(lblToken);
            this.Controls.Add(lblDetalles);
            this.Controls.Add(lblContador);
            this.Controls.Add(pbProgreso);
            this.Controls.Add(btnCopiar);
            this.Controls.Add(btnCerrar);

            timerCuentaRegresiva = new System.Windows.Forms.Timer { Interval = 1000 };
            timerCuentaRegresiva.Tick += (s, e) =>
            {
                _segundosRestantes--;
                if (_segundosRestantes <= 0)
                {
                    timerCuentaRegresiva.Stop();
                    lblContador.Text = "TOKEN EXPIRADO";
                    lblContador.ForeColor = Color.FromArgb(255, 71, 87);
                    lblToken.ForeColor = Color.FromArgb(120, 120, 120);
                    pbProgreso.Value = 0;
                }
                else
                {
                    lblContador.Text = $"Expira en: {_segundosRestantes} segundos";
                    pbProgreso.Value = Math.Clamp(_segundosRestantes, 0, 120);
                }
            };
            timerCuentaRegresiva.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timerCuentaRegresiva?.Stop();
            timerCuentaRegresiva?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
