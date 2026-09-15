namespace Cajero.BLL.DTOs
{
    /// <summary>
    /// Encapsula el resultado de una operación en la capa de negocio sin datos adicionales.
    /// Proporciona un canal estandarizado para comunicar éxito o fracaso y mensajes de error a la UI.
    /// </summary>
    public class ResultadoOperacion
    {
        public bool Exito { get; set; }
        public string Mensaje { get; set; } = string.Empty;

        public static ResultadoOperacion Ok(string mensaje = "Operación realizada con éxito.")
        {
            return new ResultadoOperacion { Exito = true, Mensaje = mensaje };
        }

        public static ResultadoOperacion Fallo(string mensaje)
        {
            return new ResultadoOperacion { Exito = false, Mensaje = mensaje };
        }
    }

    /// <summary>
    /// Encapsula el resultado de una operación en la capa de negocio retornando datos fuertemente tipados.
    /// </summary>
    /// <typeparam name="T">Tipo del dato retornado en caso de éxito.</typeparam>
    public class ResultadoOperacion<T> : ResultadoOperacion
    {
        public T? Datos { get; set; }

        public static ResultadoOperacion<T> Ok(T datos, string mensaje = "Operación realizada con éxito.")
        {
            return new ResultadoOperacion<T>
            {
                Exito = true,
                Mensaje = mensaje,
                Datos = datos
            };
        }

        public new static ResultadoOperacion<T> Fallo(string mensaje)
        {
            return new ResultadoOperacion<T>
            {
                Exito = false,
                Mensaje = mensaje,
                Datos = default
            };
        }
    }
}
