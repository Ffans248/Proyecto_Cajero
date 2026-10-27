using System;
using System.IO;
using System.Text.Json;

namespace Cajero.Cliente
{
    public class ConfiguracionCliente
    {
        public string ServidorIP { get; set; } = "127.0.0.1";
        public int ServidorPuerto { get; set; } = 5000;
        public string CodigoCajero { get; set; } = "ATM-01";
        public string PuertoCOM { get; set; } = "COM15";
        public int BaudRate { get; set; } = 9600;

        private const string ArchivoConfig = "config.json";

        public static ConfiguracionCliente Cargar()
        {
            try
            {
                if (File.Exists(ArchivoConfig))
                {
                    string json = File.ReadAllText(ArchivoConfig);
                    var conf = JsonSerializer.Deserialize<ConfiguracionCliente>(json);
                    if (conf != null) return conf;
                }
            }
            catch { }

            // Guardar configuración por defecto
            var defecto = new ConfiguracionCliente();
            Guardar(defecto);
            return defecto;
        }

        public static void Guardar(ConfiguracionCliente config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ArchivoConfig, json);
            }
            catch { }
        }
    }
}
