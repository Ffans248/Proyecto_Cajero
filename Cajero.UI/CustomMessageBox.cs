using System.Windows;

namespace Cajero.UI
{
    public static class CustomMessageBox
    {
        public static void Show(string message, string title = "Notificación", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
        {
            // Ejecutar en el hilo de la UI si es necesario, pero como estamos en UI asumimos acceso directo.
            var window = new CustomMessageBoxWindow(message, title, image);
            
            // Si hay una ventana activa, asignarla como owner para que sea modal real
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            {
                // Buscar la ventana activa actual
                Window activeWindow = null;
                foreach (Window win in Application.Current.Windows)
                {
                    if (win.IsActive)
                    {
                        activeWindow = win;
                        break;
                    }
                }
                
                if (activeWindow != null && activeWindow != window)
                {
                    window.Owner = activeWindow;
                }
            }
            
            window.ShowDialog();
        }
    }
}
