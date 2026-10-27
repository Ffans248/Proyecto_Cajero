using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Cajero.Protocolo;
using Cajero.Servidor.Comunicaciones;

namespace Cajero.Servidor.UI
{
    public class FormMensajeRemoto : Form
    {
        private readonly ServidorTcp _servidorTcp;
        private readonly string _codigoCajero;
        private TextBox txtMensaje = null!;
        private Button btnEnviar = null!;
        private Button btnCancelar = null!;

        public FormMensajeRemoto(ServidorTcp servidorTcp, string codigoCajero)
        {
            _servidorTcp = servidorTcp;
            _codigoCajero = codigoCajero;

            InicializarComponentes();
        }

        private void InicializarComponentes()
        {
            this.Text = $"Enviar Mensaje Remoto - {_codigoCajero}";
            this.Size = new Size(420, 260);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(24, 28, 36);
            this.ForeColor = Color.White;

            var lblHeader = new Label
            {
                Text = $"📢 ENVIAR MENSAJE A PANTALLA ({_codigoCajero})",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(lblHeader);

            var lblDesc = new Label
            {
                Text = "Escriba el mensaje que se mostrará en la pantalla del cajero:",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(200, 215, 230),
                Left = 25,
                Top = 45,
                Width = 355,
                Height = 25
            };
            this.Controls.Add(lblDesc);

            txtMensaje = new TextBox
            {
                Left = 25,
                Top = 75,
                Width = 355,
                Height = 65,
                Multiline = true,
                Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(35, 42, 54),
                ForeColor = Color.White,
                Text = "Cajero en mantenimiento programado. Por favor use otro cajero."
            };
            this.Controls.Add(txtMensaje);

            btnEnviar = new Button
            {
                Text = "Enviar Mensaje",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Top = 155,
                Left = 50,
                Width = 140,
                Height = 38,
                Cursor = Cursors.Hand
            };
            btnEnviar.FlatAppearance.BorderSize = 0;
            btnEnviar.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtMensaje.Text)) return;

                btnEnviar.Enabled = false;
                bool ok = await _servidorTcp.EnviarComandoAsync(_codigoCajero, TiposComando.Mensaje, new Dictionary<string, string>
                {
                    { "texto", txtMensaje.Text.Trim() }
                });

                if (ok)
                {
                    MessageBox.Show("Mensaje transmitido exitosamente al cajero.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Close();
                }
                else
                {
                    MessageBox.Show("No se pudo enviar el mensaje. Verifique que el cajero esté conectado.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnEnviar.Enabled = true;
                }
            };

            btnCancelar = new Button
            {
                Text = "Cancelar",
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Top = 155,
                Left = 210,
                Width = 140,
                Height = 38,
                Cursor = Cursors.Hand
            };
            btnCancelar.FlatAppearance.BorderSize = 0;
            btnCancelar.Click += (s, e) => this.Close();

            this.Controls.Add(btnEnviar);
            this.Controls.Add(btnCancelar);
        }
    }
}
