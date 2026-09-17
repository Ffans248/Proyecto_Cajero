using System;
using Cajerro.DAL;

namespace Cajero.BLL
{
    public class ServicioAutenticacion
    {
        private GestorArchivosCSV _dal;

        public ServicioAutenticacion()
        {
            _dal = new GestorArchivosCSV();
        }

        public string ValidarAcceso(string uidRfid, string pin)
        {
            // Administrador asignado mediante código para usar NFC
            if (uidRfid == "0000002577992613")
            {
                if (pin == "0000") 
                    return "Administrador";
                else 
                    return "DENEGADO";
            }

            var usuario = _dal.ObtenerUsuarioPorTarjeta(uidRfid);

            if (usuario == null || usuario.PIN != pin)
            {
                return "DENEGADO";
            }

            if (usuario.NumeroTarjeta.ToUpper().Contains("ADM"))
            {
                return "Administrador";
            }

            return "Cliente";
        }
    }
}


