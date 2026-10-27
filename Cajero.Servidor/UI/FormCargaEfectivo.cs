using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Cajero.Servidor.AccesoDatos;

namespace Cajero.Servidor.UI
{
    public class FormCargaEfectivo : Form
    {
        private readonly RepositorioServidor _repo;
        private readonly string _codigoCajero;
        private readonly int _idAdmin;
        private readonly bool _esInicializacion; // true: Inicializar (<= 10k), false: Agregar (<= 30k)

        private readonly Dictionary<int, NumericUpDown> _inputsDenom = new();
        private Label lblTotal = null!;
        private Button btnGuardar = null!;
        private Button btnCancelar = null!;

        public bool OperacionExitosa { get; private set; }

        public FormCargaEfectivo(RepositorioServidor repo, string codigoCajero, int idAdmin, bool esInicializacion)
        {
            _repo = repo;
            _codigoCajero = codigoCajero;
            _idAdmin = idAdmin;
            _esInicializacion = esInicializacion;

            InicializarComponentes();
        }

        private void InicializarComponentes()
        {
            this.Text = _esInicializacion ? $"Inicializar Efectivo - {_codigoCajero}" : $"Agregar Efectivo - {_codigoCajero}";
            this.Size = new Size(420, 520);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(24, 28, 36);
            this.ForeColor = Color.White;

            var lblHeader = new Label
            {
                Text = _esInicializacion ? $"💵 INICIALIZACIÓN DIARIA ({_codigoCajero})" : $"➕ AGREGAR EFECTIVO ({_codigoCajero})",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 255),
                Dock = DockStyle.Top,
                Height = 45,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(lblHeader);

            var lblDesc = new Label
            {
                Text = _esInicializacion ? "Límite máximo permitido: Q 10,000.00 (Una vez al día)" : "Capacidad máxima en cajero: Q 30,000.00",
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                ForeColor = Color.FromArgb(200, 210, 225),
                Top = 45,
                Left = 20,
                Width = 360,
                Height = 25,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(lblDesc);

            int[] denoms = { 200, 100, 50, 20, 10, 5, 1 };
            int y = 80;

            foreach (var d in denoms)
            {
                var lblD = new Label
                {
                    Text = $"Billetes de Q {d}:",
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(220, 230, 245),
                    Left = 40,
                    Top = y + 4,
                    Width = 140
                };

                var num = new NumericUpDown
                {
                    Left = 190,
                    Top = y,
                    Width = 160,
                    Minimum = 0,
                    Maximum = 1000,
                    Value = 0,
                    Font = new Font("Segoe UI", 10f),
                    BackColor = Color.FromArgb(35, 42, 54),
                    ForeColor = Color.White
                };
                num.ValueChanged += (s, e) => CalcularTotal();

                _inputsDenom[d] = num;
                this.Controls.Add(lblD);
                this.Controls.Add(num);
                y += 38;
            }

            lblTotal = new Label
            {
                Text = "Total a Cargar: Q 0.00",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 213, 115),
                Top = y + 10,
                Left = 20,
                Width = 360,
                Height = 35,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(lblTotal);

            btnGuardar = new Button
            {
                Text = "Procesar Carga",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Top = y + 55,
                Left = 40,
                Width = 150,
                Height = 40,
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
                Top = y + 55,
                Left = 210,
                Width = 150,
                Height = 40,
                Cursor = Cursors.Hand
            };
            btnCancelar.FlatAppearance.BorderSize = 0;
            btnCancelar.Click += (s, e) => this.Close();

            this.Controls.Add(btnGuardar);
            this.Controls.Add(btnCancelar);
        }

        private decimal CalcularTotal()
        {
            decimal total = 0;
            foreach (var kvp in _inputsDenom)
            {
                total += kvp.Key * kvp.Value.Value;
            }
            lblTotal.Text = $"Total a Cargar: Q {total:N2}";
            return total;
        }

        private void BtnGuardar_Click(object? sender, EventArgs e)
        {
            decimal total = CalcularTotal();
            if (total <= 0)
            {
                MessageBox.Show("Debe ingresar al menos un billete para realizar la carga.", "Atención", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var desglose = new Dictionary<string, int>();
            foreach (var kvp in _inputsDenom)
            {
                if (kvp.Value.Value > 0)
                {
                    desglose[kvp.Key.ToString()] = (int)kvp.Value.Value;
                }
            }

            if (_esInicializacion)
            {
                var (cod, msj) = _repo.InicializarCajero(_codigoCajero, _idAdmin, desglose);
                if (cod == "00")
                {
                    MessageBox.Show(msj, "Inicialización Exitosa", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    OperacionExitosa = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(msj, "Error al Inicializar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                var (cod, msj) = _repo.AgregarEfectivo(_codigoCajero, _idAdmin, desglose);
                if (cod == "00")
                {
                    MessageBox.Show(msj, "Recarga Exitosa", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    OperacionExitosa = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(msj, "Error al Agregar Efectivo", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
