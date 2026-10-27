using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Cajero.Protocolo
{
    public static class ProtocoloHelper
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static string Serializar(object mensaje)
        {
            string json = JsonSerializer.Serialize(mensaje, mensaje.GetType(), _jsonOptions);
            // Aseguramos que sea una sola línea terminada con \n
            return json.Replace("\r", "").Replace("\n", "") + "\n";
        }

        public static byte[] SerializarABytes(object mensaje)
        {
            string texto = Serializar(mensaje);
            return Encoding.UTF8.GetBytes(texto);
        }

        public static T? Deserializar<T>(string lineaJson)
        {
            if (string.IsNullOrWhiteSpace(lineaJson)) return default;
            string limpia = lineaJson.Trim();
            return JsonSerializer.Deserialize<T>(limpia, _jsonOptions);
        }

        public static MensajeBase? DeserializarBase(string lineaJson)
        {
            return Deserializar<MensajeBase>(lineaJson);
        }
    }
}
