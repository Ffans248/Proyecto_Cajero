using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Cajero.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // 1. Instanciar la capa de datos (El código de Martín)
            var gestorArchivos = new Cajerro.DAL.GestorArchivosCSV();

            // 2. Instanciar tu capa de negocio (Tu código)
            var usuarioLogica = new Cajero.BLL.UsuarioLogica(gestorArchivos);

            // 3. Prueba de lectura
            try
            {
                // Usa un número de tarjeta que hayas escrito dentro de Usuarios.csv
                var info = usuarioLogica.VerSaldo("1234567890123456");
                System.Windows.MessageBox.Show($"¡Conexión exitosa! Saldo actual: {info.Saldo}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error en la prueba: {ex.Message}");
            }
        }
    }
}