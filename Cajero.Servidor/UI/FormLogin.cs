using System;
using System.Drawing;
using System.Windows.Forms;
using Cajero.Servidor.AccesoDatos;
using Cajero.Servidor.Comunicaciones;
using Cajero.Servidor.Modelos;

namespace Cajero.Servidor.UI
{
    public class FormLogin : Form
    {
        private readonly RepositorioServidor _repo;
        private readonly ServidorTcp _servidorTcp;
        private TextBox txtUsuario = null!;
        private TextBox txtPassword = null!;
        private Button btnLogin = null!;
        private Label lblEstado = null!;

        public FormLogin()
        {
            _repo = new RepositorioServidor();
            _servidorTcp = new ServidorTcp(_repo, 5000);
            try
            {
                _servidorTcp.Iniciar();
            }
            catch
            {
                // Si Cajero.UI ya está escuchando en el puerto 5000, no interrumpir el inicio
            }
            InicializarComponentes();
        }

        private void InicializarComponentes()
        {
            this.Text = "Servidor Central - Acceso Administrativo";
            this.Size = new Size(400, 430);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(20, 24, 33);
            this.ForeColor = Color.White;

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 100,
                BackColor = Color.FromArgb(15, 18, 25)
            };

            var lblLogo = new Label
            {
                Text = "🏦 RED DE CAJEROS CENTRAL",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Dock = DockStyle.Top,
                Height = 45,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblSub = new Label
            {
                Text = "Servidor Central & Dashboard Fase II (Puerto 5000 Activo)",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(46, 213, 115),
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter
            };

            pnlHeader.Controls.Add(lblSub);
            pnlHeader.Controls.Add(lblLogo);
            this.Controls.Add(pnlHeader);

            int y = 130;

            var lblU = new Label
            {
                Text = "Usuari:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Left = 50,
                Top = y,
                Width = 300,
                ForeColor = Color.FromArgb(200, 215, 230)
            };
            this.Controls.Add(lblU);
            y += 24;

            txtUsuario = new TextBox
            {
                Left = 50,
                Top = y,
                Width = 285,
                Font = new Font("Segoe UI", 11f),
                BackColor = Color.FromArgb(32, 40, 54),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Text = "admin"
            };
            this.Controls.Add(txtUsuario);
            y += 42;

            var lblP = new Label
            {
                Text = "Contraseña:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Left = 50,
                Top = y,
                Width = 300,
                ForeColor = Color.FromArgb(200, 215, 230)
            };
            this.Controls.Add(lblP);
            y += 24;

            txtPassword = new TextBox
            {
                Left = 50,
                Top = y,
                Width = 285,
                Font = new Font("Segoe UI", 11f),
                BackColor = Color.FromArgb(32, 40, 54),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                PasswordChar = '●',
                Text = "Admin123!"
            };
            txtPassword.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) BtnLogin_Click(s, e); };
            this.Controls.Add(txtPassword);
            y += 50;

            btnLogin = new Button
            {
                Text = "INICIAR SESIÓN",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Left = 50,
                Top = y,
                Width = 285,
                Height = 42,
                Cursor = Cursors.Hand
            };
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.Click += BtnLogin_Click;
            this.Controls.Add(btnLogin);
            y += 48;

            lblEstado = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(255, 71, 87),
                Left = 40,
                Top = y,
                Width = 310,
                Height = 35,
                TextAlign = ContentAlignment.TopCenter
            };
            this.Controls.Add(lblEstado);
        }

        private void BtnLogin_Click(object? sender, EventArgs e)
        {
            string user = txtUsuario.Text.Trim();
            string pass = txtPassword.Text;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                lblEstado.Text = "Por favor ingrese usuario y contraseña.";
                return;
            }

            btnLogin.Enabled = false;
            lblEstado.ForeColor = Color.FromArgb(0, 200, 255);
            lblEstado.Text = "Validando credenciales...";
            Application.DoEvents();

            try
            {
                var admin = _repo.AutenticarAdmin(user, pass);
                if (admin != null)
                {
                    lblEstado.ForeColor = Color.FromArgb(46, 213, 115);
                    lblEstado.Text = $"Acceso concedido ({admin.Rol}).";

                    try
                    {
                        if (!_servidorTcp.EstaCorriendo)
                            _servidorTcp.Iniciar();
                    }
                    catch { }

                    _repo.RegistrarEnBitacora("Administrador", "Inicio de Sesión", $"Admin '{admin.Usuario}' inició sesión en el servidor ({admin.Rol})");

                    var dashboard = new FormDashboard(admin, _repo, _servidorTcp);
                    dashboard.FormClosed += (s, args) => this.Close();
                    dashboard.Show();
                    this.Hide();
                }
                else
                {
                    lblEstado.ForeColor = Color.FromArgb(255, 71, 87);
                    lblEstado.Text = "Credenciales incorrectas o usuario inactivo.";
                    btnLogin.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                lblEstado.ForeColor = Color.FromArgb(255, 71, 87);
                lblEstado.Text = "Error de BD: " + ex.Message;
                btnLogin.Enabled = true;
            }
        }
    }
}
