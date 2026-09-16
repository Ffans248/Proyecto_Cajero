using Cajero.DAL.Models;

namespace Cajero.DAL.Repositories.Interfaces
{
    /// <summary>
    /// Contrato para la persistencia y consulta de usuarios en Usuarios.csv.
    /// </summary>
    public interface IUsuarioRepository
    {
        List<Usuario> ObtenerTodos();
        Usuario? ObtenerPorTarjeta(string numeroTarjeta);
        Usuario? ObtenerPorRfid(string rfidUid);
        void Insertar(Usuario usuario);
        void Actualizar(Usuario usuario);
        bool ExisteTarjeta(string numeroTarjeta);
        bool ExisteRfid(string rfidUid);
    }
}
