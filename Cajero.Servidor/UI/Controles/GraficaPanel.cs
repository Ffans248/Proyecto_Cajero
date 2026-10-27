using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Cajero.Servidor.UI.Controles
{
    public enum TipoGrafica
    {
        RetirosVsDepositosPorHora,
        MontosPorCajero,
        DistribucionDenominaciones
    }

    public class GraficaPanel : Panel
    {
        public TipoGrafica Tipo { get; set; } = TipoGrafica.RetirosVsDepositosPorHora;
        public string Titulo { get; set; } = "Gráfica";

        // Datos para Retiros vs Depósitos por hora (0 a 23)
        public Dictionary<int, decimal> RetirosPorHora { get; set; } = new();
        public Dictionary<int, decimal> DepositosPorHora { get; set; } = new();

        // Datos para montos por cajero (ATM-01: 5000, ...)
        public Dictionary<string, decimal> MontosPorCajero { get; set; } = new();

        // Datos para billetes por denominación (200: 20, 100: 25, ...)
        public Dictionary<int, int> BilletesPorDenominacion { get; set; } = new();

        public GraficaPanel()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(30, 36, 48);
            this.Padding = new Padding(10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Título de la Gráfica
            using var fontTitulo = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            using var brushTitulo = new SolidBrush(Color.FromArgb(230, 240, 255));
            g.DrawString(Titulo, fontTitulo, brushTitulo, 12, 10);

            var rectGrafica = new Rectangle(15, 38, this.Width - 30, this.Height - 50);

            switch (Tipo)
            {
                case TipoGrafica.RetirosVsDepositosPorHora:
                    DibujarRetirosVsDepositos(g, rectGrafica);
                    break;
                case TipoGrafica.MontosPorCajero:
                    DibujarMontosPorCajero(g, rectGrafica);
                    break;
                case TipoGrafica.DistribucionDenominaciones:
                    DibujarDistribucionBilletes(g, rectGrafica);
                    break;
            }
        }

        private void DibujarRetirosVsDepositos(Graphics g, Rectangle rect)
        {
            // Horas típicas de operación (ej. 8am a 20pm)
            int[] horas = { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
            decimal maxValor = 1000m;

            foreach (var h in horas)
            {
                if (RetirosPorHora.TryGetValue(h, out decimal r) && r > maxValor) maxValor = r;
                if (DepositosPorHora.TryGetValue(h, out decimal d) && d > maxValor) maxValor = d;
            }

            // Leyenda
            using var fontLeyenda = new Font("Segoe UI", 8.5f);
            using var brushRetiro = new SolidBrush(Color.FromArgb(255, 71, 87));
            using var brushDeposito = new SolidBrush(Color.FromArgb(46, 213, 115));
            using var brushTexto = new SolidBrush(Color.FromArgb(180, 190, 205));

            g.FillRectangle(brushRetiro, rect.Right - 160, rect.Top - 25, 12, 12);
            g.DrawString("Retiros", fontLeyenda, brushTexto, rect.Right - 145, rect.Top - 26);
            g.FillRectangle(brushDeposito, rect.Right - 80, rect.Top - 25, 12, 12);
            g.DrawString("Depósitos", fontLeyenda, brushTexto, rect.Right - 65, rect.Top - 26);

            int numHoras = horas.Length;
            float anchoGrupo = (float)rect.Width / numHoras;
            float anchoBarra = Math.Max(4, (anchoGrupo - 8) / 2);
            float baseLine = rect.Bottom - 20;

            using var penEje = new Pen(Color.FromArgb(55, 65, 85), 1);
            g.DrawLine(penEje, rect.Left, baseLine, rect.Right, baseLine);

            for (int i = 0; i < numHoras; i++)
            {
                int h = horas[i];
                float xGrupo = rect.Left + (i * anchoGrupo) + 4;

                decimal valR = RetirosPorHora.TryGetValue(h, out decimal vr) ? vr : 0;
                decimal valD = DepositosPorHora.TryGetValue(h, out decimal vd) ? vd : 0;

                float altoR = (float)((valR / maxValor) * (decimal)(baseLine - rect.Top - 15));
                float altoD = (float)((valD / maxValor) * (decimal)(baseLine - rect.Top - 15));

                // Barra Retiro
                g.FillRectangle(brushRetiro, xGrupo, baseLine - altoR, anchoBarra, altoR);

                // Barra Depósito
                g.FillRectangle(brushDeposito, xGrupo + anchoBarra + 2, baseLine - altoD, anchoBarra, altoD);

                // Etiqueta hora
                string etiquetaH = $"{h}h";
                var sizeH = g.MeasureString(etiquetaH, fontLeyenda);
                g.DrawString(etiquetaH, fontLeyenda, brushTexto, xGrupo + (anchoGrupo / 4) - (sizeH.Width / 2), baseLine + 4);
            }
        }

        private void DibujarMontosPorCajero(Graphics g, Rectangle rect)
        {
            if (MontosPorCajero == null || MontosPorCajero.Count == 0)
            {
                using var fontAviso = new Font("Segoe UI", 9.5f, FontStyle.Italic);
                using var brushAviso = new SolidBrush(Color.Gray);
                g.DrawString("Esperando actividad de transacciones...", fontAviso, brushAviso, rect.Left + 20, rect.Top + 40);
                return;
            }

            decimal maxValor = MontosPorCajero.Values.DefaultIfEmpty(100).Max();
            if (maxValor <= 0) maxValor = 100;

            int items = MontosPorCajero.Count;
            float anchoColumna = (float)rect.Width / items;
            float anchoBarra = Math.Max(12, anchoColumna * 0.55f);
            float baseLine = rect.Bottom - 22;

            using var penEje = new Pen(Color.FromArgb(55, 65, 85), 1);
            g.DrawLine(penEje, rect.Left, baseLine, rect.Right, baseLine);

            using var brushBarra = new LinearGradientBrush(
                new Point(0, rect.Top), new Point(0, rect.Bottom),
                Color.FromArgb(0, 180, 255), Color.FromArgb(0, 100, 220));
            using var fontItem = new Font("Segoe UI", 8.5f);
            using var brushTexto = new SolidBrush(Color.FromArgb(200, 215, 230));

            int idx = 0;
            foreach (var kvp in MontosPorCajero)
            {
                float x = rect.Left + (idx * anchoColumna) + (anchoColumna - anchoBarra) / 2;
                float alto = (float)((kvp.Value / maxValor) * (decimal)(baseLine - rect.Top - 25));

                g.FillRectangle(brushBarra, x, baseLine - alto, anchoBarra, alto);

                // Monto arriba de la barra
                string txtMonto = $"Q{kvp.Value:N0}";
                var szMonto = g.MeasureString(txtMonto, fontItem);
                g.DrawString(txtMonto, fontItem, brushTexto, x + (anchoBarra / 2) - (szMonto.Width / 2), baseLine - alto - 15);

                // Código cajero abajo
                var szCod = g.MeasureString(kvp.Key, fontItem);
                g.DrawString(kvp.Key, fontItem, brushTexto, x + (anchoBarra / 2) - (szCod.Width / 2), baseLine + 4);

                idx++;
            }
        }

        private void DibujarDistribucionBilletes(Graphics g, Rectangle rect)
        {
            int[] denominaciones = { 200, 100, 50, 20, 10, 5, 1 };
            int maxCant = 50;

            foreach (var d in denominaciones)
            {
                if (BilletesPorDenominacion.TryGetValue(d, out int cant) && cant > maxCant) maxCant = cant;
            }

            int items = denominaciones.Length;
            float anchoColumna = (float)rect.Width / items;
            float anchoBarra = Math.Max(10, anchoColumna * 0.55f);
            float baseLine = rect.Bottom - 22;

            using var penEje = new Pen(Color.FromArgb(55, 65, 85), 1);
            g.DrawLine(penEje, rect.Left, baseLine, rect.Right, baseLine);

            Color[] colores = {
                Color.FromArgb(155, 89, 182), // Q200 Púrpura
                Color.FromArgb(52, 152, 219),  // Q100 Azul
                Color.FromArgb(26, 188, 156),  // Q50 Verdeazulado
                Color.FromArgb(46, 204, 113),  // Q20 Verde
                Color.FromArgb(241, 196, 15),  // Q10 Amarillo
                Color.FromArgb(230, 126, 34),  // Q5 Naranja
                Color.FromArgb(231, 76, 60)    // Q1 Rojo
            };

            using var fontItem = new Font("Segoe UI", 8.5f);
            using var brushTexto = new SolidBrush(Color.FromArgb(200, 215, 230));

            for (int i = 0; i < items; i++)
            {
                int denom = denominaciones[i];
                int cant = BilletesPorDenominacion.TryGetValue(denom, out int c) ? c : 0;

                float x = rect.Left + (i * anchoColumna) + (anchoColumna - anchoBarra) / 2;
                float alto = (float)((cant / (float)maxCant) * (baseLine - rect.Top - 25));

                using var brush = new SolidBrush(colores[i % colores.Length]);
                g.FillRectangle(brush, x, baseLine - alto, anchoBarra, alto);

                // Cantidad arriba
                string txtCant = $"{cant}";
                var szCant = g.MeasureString(txtCant, fontItem);
                g.DrawString(txtCant, fontItem, brushTexto, x + (anchoBarra / 2) - (szCant.Width / 2), baseLine - alto - 15);

                // Denominación abajo
                string txtDenom = $"Q{denom}";
                var szDenom = g.MeasureString(txtDenom, fontItem);
                g.DrawString(txtDenom, fontItem, brushTexto, x + (anchoBarra / 2) - (szDenom.Width / 2), baseLine + 4);
            }
        }
    }
}
