using System;
using System.Windows.Forms;
using Cajero.Cliente.UI;

namespace Cajero.Cliente
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new FormCajeroATM());
        }    
    }
}