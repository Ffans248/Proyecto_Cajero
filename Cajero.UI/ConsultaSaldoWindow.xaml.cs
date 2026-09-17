using System.Windows;

namespace Cajero.UI
{
    public partial class ConsultaSaldoWindow : Window
    {
        public ConsultaSaldoWindow(decimal saldo, decimal disponible)
        {
            InitializeComponent();
            lblSaldo.Text = $"Q {saldo:N2}";
            lblLimite.Text = $"Q {disponible:N2}";
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
