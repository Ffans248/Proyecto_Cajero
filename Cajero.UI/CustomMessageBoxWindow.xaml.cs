using System.Windows;
using System.Windows.Media;

namespace Cajero.UI
{
    public partial class CustomMessageBoxWindow : Window
    {
        public CustomMessageBoxWindow(string message, string title, MessageBoxImage image)
        {
            InitializeComponent();
            
            lblTitle.Text = title;
            txtMessage.Text = message;

            ConfigurarIcono(image);
        }

        private void ConfigurarIcono(MessageBoxImage image)
        {
            switch (image)
            {
                case MessageBoxImage.Error:
                    lblIcon.Text = "✖";
                    lblIcon.Foreground = (Brush)FindResource("AccentDangerBrush");
                    break;
                case MessageBoxImage.Warning:
                    lblIcon.Text = "⚠";
                    lblIcon.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Orange/Yellow
                    break;
                case MessageBoxImage.Information:
                default:
                    lblIcon.Text = "ℹ";
                    lblIcon.Foreground = (Brush)FindResource("AccentPrimaryBrush");
                    break;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
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
