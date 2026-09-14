using System.Collections.Generic;

namespace Cajero.BLL
{
    public class RespuestaTransaccion
    {
        public bool Exito { get; set; }
        public string? Mensaje { get; set; }
        public Dictionary<int, int> BilletesDesglosados { get; set; }

        public RespuestaTransaccion()
        {
            BilletesDesglosados = new Dictionary<int, int>();
        }
    }
}
