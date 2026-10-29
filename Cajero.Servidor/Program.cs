using System;
using System.Windows.Forms;
using Cajero.Servidor.UI;

namespace Cajero.Servidor
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new FormLogin());
        }
    }
}
