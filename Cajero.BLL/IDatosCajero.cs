using System.Collections.Generic;

namespace Cajero.BLL
{
    public interface IDatosCajero
    {
        bool ExisteUsuario(string token);
        bool UsuarioActivo(string token);
        decimal ObtenerSaldo(string token);
        decimal ObtenerRetirosDelDia(string token);
        decimal ObtenerLimiteDiario(string token);
        Dictionary<int, int> ObtenerInventarioBilletes();
        
        void ActualizarSaldo(string token, decimal nuevoSaldo);
        void RegistrarTransaccion(string token, string tipo, decimal monto);
        void ActualizarInventarioBilletes(Dictionary<int, int> nuevosBilletes);
    }
}
