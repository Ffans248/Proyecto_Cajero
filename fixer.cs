using System;
using System.IO;
using System.Text;

class Program {
    static void Main() {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var win1252 = Encoding.GetEncoding(1252);
        
        string[] files = Directory.GetFiles(@"c:\Users\CompuFire\source\repos\Proyecto_Cajero\Cajero.UI", "*.*", SearchOption.AllDirectories);
        foreach(var f in files) {
            if (f.EndsWith(".xaml") || f.EndsWith(".cs")) {
                string text = File.ReadAllText(f);
                if (text.Contains("Ã")) {
                    byte[] bytes = win1252.GetBytes(text);
                    string fixedText = Encoding.UTF8.GetString(bytes);
                    File.WriteAllText(f, fixedText, new UTF8Encoding(true));
                    Console.WriteLine("Fixed: " + f);
                }
            }
        }
    }
}
