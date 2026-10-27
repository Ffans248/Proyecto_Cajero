using System;
using System.Drawing;
using System.Windows.Forms;
using Cajero.Servidor.AccesoDatos;
using Cajero.Servidor.Modelos;

namespace Cajero.Servidor.UI
{
    public enum ModoFormUsuario
    {
        Crear,
        CambiarTarjeta,
        CambiarLimite,
        AsignarPinTemporal
    }

    public class FormUsuario : Form
    {
        private readonly RepositorioServidor _repo;
        private readonly ModoFormUsuario _modo;
        private readonly UsuarioDto? _usuario;
        private readonly int _idAdmin;

        private TextBox txtNombre = null!;
        private TextBox txtTarjeta = null!;
        private TextBox txtPin = null!;
        private TextBox txtRfidUid = null!;
        private NumericUpDown numSaldo = null!;
        private NumericUpDown numLimite = null!;
        private Button btnGuardar = null!;
        private Button btnCancelar = null!;

        public bool OperacionExitosa { get; private set; }

        public FormUsuario(RepositorioServidor repo, ModoFormUsuario modo, int idAdmin, UsuarioDto? usuario = null)
        {
            _repo = repo;
            _modo = modo;
            _idAdmin = idAdmin;
            _usuario = usuario;

            InicializarComponentes();
        }

        private void InicializarComponentes()
        {
            this.Size = new Size(460, 480);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(24, 28, 36);
            this.ForeColor = Color.White;

            string titulo = _modo switch
            {
                ModoFormUsuario.Crear => "👤 CREAR NUEVO USUARIO",
                ModoFormUsuario.CambiarTarjeta => $"💳 CAMBIAR TARJETA ({_usuario?.Nombre})",
                ModoFormUsuario.CambiarLimite => $"📊 MODIFICAR LÍMITE DIARIO ({_usuario?.Nombre})",
                ModoFormUsuario.AsignarPinTemporal => $"🔑 ASIGNAR PIN TEMPORAL ({_usuario?.Nombre})",
                _ => "Gestión de Usuario"
            };

            this.Text = titulo;

            var lblHeader = new Label
            {
                Text = titulo,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Dock = DockStyle.Top,
                Height = 45,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(lblHeader);

            int y = 55;

            // Nombre
            var lblNom = new Label { Text = "Nombre Completo:", Left = 30, Top = y + 4, Width = 140, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            txtNombre = new TextBox
            {
                Left = 180, Top = y, Width = 230, Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(35, 42, 54), ForeColor = Color.White,
                Text = _usuario?.Nombre ?? "",
                ReadOnly = _modo != ModoFormUsuario.Crear
            };
            this.Controls.Add(lblNom);
            this.Controls.Add(txtNombre);
            y += 40;

            // Tarjeta (16 dígitos)
            var lblTar = new Label { Text = "No. Tarjeta (16 dig):", Left = 30, Top = y + 4, Width = 140, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            txtTarjeta = new TextBox
            {
                Left = 180, Top = y, Width = 230, Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(35, 42, 54), ForeColor = Color.White,
                MaxLength = 16,
                Text = _modo == ModoFormUsuario.Crear ? "" : (_modo == ModoFormUsuario.CambiarTarjeta ? "" : _usuario?.NumeroTarjeta ?? "")
            };
            this.Controls.Add(lblTar);
            this.Controls.Add(txtTarjeta);
            y += 40;

            // PIN (4 dígitos)
            var lblP = new Label { Text = "PIN (4 dígitos):", Left = 30, Top = y + 4, Width = 140, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            txtPin = new TextBox
            {
                Left = 180, Top = y, Width = 230, Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(35, 42, 54), ForeColor = Color.White,
                MaxLength = 4,
                PasswordChar = '●'
            };
            this.Controls.Add(lblP);
            this.Controls.Add(txtPin);
            y += 40;

            // UID RFID físico (Opcional, e.g. 99A90BA4)
            var lblRfid = new Label { Text = "UID RFID (ESP32):", Left = 30, Top = y + 4, Width = 140, Font = new Font("Segoe UI", 9f) };
            txtRfidUid = new TextBox
            {
                Left = 180, Top = y, Width = 230, Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(35, 42, 54), ForeColor = Color.White
            };
            this.Controls.Add(lblRfid);
            this.Controls.Add(txtRfidUid);
            y += 40;

            // Saldo inicial
            var lblS = new Label { Text = "Saldo Inicial (Q):", Left = 30, Top = y + 4, Width = 140, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            numSaldo = new NumericUpDown
            {
                Left = 180, Top = y, Width = 230, Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(35, 42, 54), ForeColor = Color.White,
                DecimalPlaces = 2,
                Maximum = 1000000,
                Value = _usuario?.Saldo ?? 1000m,
                Enabled = _modo == ModoFormUsuario.Crear
            };
            this.Controls.Add(lblS);
            this.Controls.Add(numSaldo);
            y += 40;

            // Límite diario
            var lblL = new Label { Text = "Límite Diario (Q):", Left = 30, Top = y + 4, Width = 140, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            numLimite = new NumericUpDown
            {
                Left = 180, Top = y, Width = 230, Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(35, 42, 54), ForeColor = Color.White,
                DecimalPlaces = 2,
                Maximum = 100000,
                Value = _usuario?.LimiteDiario ?? 2000m,
                Enabled = (_modo == ModoFormUsuario.Crear || _modo == ModoFormUsuario.CambiarLimite)
            };
            this.Controls.Add(lblL);
            this.Controls.Add(numLimite);
            y += 50;

            // Adaptar campos según el modo
            if (_modo == ModoFormUsuario.CambiarTarjeta)
            {
                lblTar.Text = "Nueva Tarjeta (16):";
                lblP.Text = "PIN Nueva Tarjeta:";
                lblRfid.Visible = false;
                txtRfidUid.Visible = false;
                lblS.Visible = false;
                numSaldo.Visible = false;
                lblL.Visible = false;
                numLimite.Visible = false;
            }
            else if (_modo == ModoFormUsuario.CambiarLimite)
            {
                lblTar.Visible = false;
                txtTarjeta.Visible = false;
                lblP.Visible = false;
                txtPin.Visible = false;
                lblRfid.Visible = false;
                txtRfidUid.Visible = false;
                lblS.Visible = false;
                numSaldo.Visible = false;
                lblL.Text = "Nuevo Límite (Q):";
            }
            else if (_modo == ModoFormUsuario.AsignarPinTemporal)
            {
                lblTar.Visible = false;
                txtTarjeta.Visible = false;
                lblP.Text = "PIN Temporal (4):";
                lblRfid.Visible = false;
                txtRfidUid.Visible = false;
                lblS.Visible = false;
                numSaldo.Visible = false;
                lblL.Visible = false;
                numLimite.Visible = false;
            }

            btnGuardar = new Button
            {
                Text = "Guardar",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Top = y,
                Left = 70,
                Width = 140,
                Height = 38,
                Cursor = Cursors.Hand
            };
            btnGuardar.FlatAppearance.BorderSize = 0;
            btnGuardar.Click += BtnGuardar_Click;

            btnCancelar = new Button
            {
                Text = "Cancelar",
                Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Top = y,
                Left = 230,
                Width = 140,
                Height = 38,
                Cursor = Cursors.Hand
            };
            btnCancelar.FlatAppearance.BorderSize = 0;
            btnCancelar.Click += (s, e) => this.Close();

            this.Controls.Add(btnGuardar);
            this.Controls.Add(btnCancelar);
        }

        private void BtnGuardar_Click(object? sender, EventArgs e)
        {
            if (_modo == ModoFormUsuario.Crear)
            {
                if (string.IsNullOrWhiteSpace(txtNombre.Text))
                {
                    MessageBox.Show("Ingrese el nombre completo.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (txtTarjeta.Text.Trim().Length != 16)
                {
                    MessageBox.Show("La tarjeta debe constar de 16 dígitos numéricos.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (txtPin.Text.Trim().Length != 4)
                {
                    MessageBox.Show("El PIN debe tener exactamente 4 dígitos.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var (ok, msj) = _repo.CrearUsuario(txtNombre.Text.Trim(), txtTarjeta.Text.Trim(), txtPin.Text.Trim(), numSaldo.Value, numLimite.Value, txtRfidUid.Text.Trim());
                if (ok)
                {
                    MessageBox.Show("Usuario registrado con éxito.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    OperacionExitosa = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(msj, "Error al crear", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (_modo == ModoFormUsuario.CambiarTarjeta)
            {
                if (txtTarjeta.Text.Trim().Length != 16)
                {
                    MessageBox.Show("El nuevo número de tarjeta debe tener 16 dígitos.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (txtPin.Text.Trim().Length != 4)
                {
                    MessageBox.Show("El PIN debe tener 4 dígitos.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var (ok, msj) = _repo.CambiarTarjetaUsuario(_usuario!.IdUsuario, txtTarjeta.Text.Trim(), txtPin.Text.Trim(), _idAdmin);
                if (ok)
                {
                    MessageBox.Show(msj, "Tarjeta Reemplazada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    OperacionExitosa = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(msj, "Error al cambiar tarjeta", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (_modo == ModoFormUsuario.CambiarLimite)
            {
                if (numLimite.Value <= 0)
                {
                    MessageBox.Show("El límite debe ser mayor a 0.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var (ok, msj) = _repo.CambiarLimiteUsuario(_usuario!.IdUsuario, numLimite.Value, _idAdmin);
                if (ok)
                {
                    MessageBox.Show(msj, "Límite Actualizado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    OperacionExitosa = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(msj, "Error al actualizar límite", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (_modo == ModoFormUsuario.AsignarPinTemporal)
            {
                if (txtPin.Text.Trim().Length != 4)
                {
                    MessageBox.Show("El PIN temporal debe tener 4 dígitos numéricos.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var (ok, msj) = _repo.AsignarPinTemporal(_usuario!.NumeroTarjeta, txtPin.Text.Trim(), _idAdmin);
                if (ok)
                {
                    MessageBox.Show(msj, "PIN Temporal", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    OperacionExitosa = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(msj, "Error al asignar PIN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
