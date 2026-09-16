using System.Globalization;
using Cajero.DAL.Helpers;
using Cajero.DAL.Models;
using Cajero.DAL.Models.Enums;
using Cajero.DAL.Repositories.Interfaces;

namespace Cajero.DAL.Repositories
{
    /// <summary>
    /// Implementación de IUsuarioRepository encargada de leer, escribir y actualizar
    /// los registros de usuarios en Usuarios.csv utilizando StreamReader y StreamWriter.
    /// </summary>
    public class UsuarioRepository : IUsuarioRepository
    {
        private readonly CsvFileContext _context;

        public UsuarioRepository(CsvFileContext? context = null)
        {
            _context = context ?? new CsvFileContext();
        }

        public List<Usuario> ObtenerTodos()
        {
            var lineas = _context.LeerTodasLasLineas(_context.RutaUsuarios, _context.LockUsuarios);
            var usuarios = new List<Usuario>();

            // Omitir fila de encabezado
            for (int i = 1; i < lineas.Count; i++)
            {
                string linea = lineas[i];
                if (string.IsNullOrWhiteSpace(linea)) continue;

                var usuario = ParsearUsuario(linea);
                if (usuario != null)
                {
                    usuarios.Add(usuario);
                }
            }

            return usuarios;
        }

        public Usuario? ObtenerPorTarjeta(string numeroTarjeta)
        {
            if (string.IsNullOrWhiteSpace(numeroTarjeta))
                return null;

            return ObtenerTodos()
                .FirstOrDefault(u => u.NumeroTarjeta.Trim().Equals(numeroTarjeta.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public Usuario? ObtenerPorRfid(string rfidUid)
        {
            if (string.IsNullOrWhiteSpace(rfidUid))
                return null;

            return ObtenerTodos()
                .FirstOrDefault(u => u.RfidUID.Trim().Equals(rfidUid.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public bool ExisteTarjeta(string numeroTarjeta)
        {
            return ObtenerPorTarjeta(numeroTarjeta) != null;
        }

        public bool ExisteRfid(string rfidUid)
        {
            return ObtenerPorRfid(rfidUid) != null;
        }

        public void Insertar(Usuario usuario)
        {
            ArgumentNullException.ThrowIfNull(usuario);

            if (ExisteTarjeta(usuario.NumeroTarjeta))
            {
                throw new InvalidOperationException($"Ya existe un usuario registrado con la tarjeta {usuario.NumeroTarjeta}.");
            }

            if (!string.IsNullOrWhiteSpace(usuario.RfidUID) && ExisteRfid(usuario.RfidUID))
            {
                throw new InvalidOperationException($"El UID RFID {usuario.RfidUID} ya está asignado a otro usuario.");
            }

            string linea = SerializarUsuario(usuario);
            _context.AgregarLinea(_context.RutaUsuarios, linea, _context.LockUsuarios);
        }

        public void Actualizar(Usuario usuario)
        {
            ArgumentNullException.ThrowIfNull(usuario);

            lock (_context.LockUsuarios)
            {
                var lineas = _context.LeerTodasLasLineas(_context.RutaUsuarios, _context.LockUsuarios);
                if (lineas.Count == 0)
                {
                    throw new InvalidOperationException("El archivo Usuarios.csv no contiene datos.");
                }

                bool encontrado = false;
                for (int i = 1; i < lineas.Count; i++)
                {
                    string linea = lineas[i];
                    if (string.IsNullOrWhiteSpace(linea)) continue;

                    string[] partes = linea.Split(CsvFileContext.Separador);
                    if (partes.Length > 0 && partes[0].Trim().Equals(usuario.NumeroTarjeta.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        lineas[i] = SerializarUsuario(usuario);
                        encontrado = true;
                        break;
                    }
                }

                if (!encontrado)
                {
                    throw new KeyNotFoundException($"No se encontró ningún usuario con la tarjeta {usuario.NumeroTarjeta} para actualizar.");
                }

                _context.EscribirTodasLasLineas(_context.RutaUsuarios, lineas, _context.LockUsuarios);
            }
        }

        #region Métodos de Mapeo CSV

        private static Usuario? ParsearUsuario(string linea)
        {
            string[] partes = linea.Split(CsvFileContext.Separador);
            if (partes.Length < 10)
                return null;

            try
            {
                string numeroTarjeta = partes[0].Trim();
                string pin = partes[1].Trim();
                string nombreCompleto = partes[2].Trim();
                string rfidUID = partes[3].Trim();

                decimal saldo = decimal.Parse(partes[4].Trim(), CultureInfo.InvariantCulture);
                decimal limiteDiarioRetiro = decimal.Parse(partes[5].Trim(), CultureInfo.InvariantCulture);
                decimal montoRetiradoHoy = decimal.Parse(partes[6].Trim(), CultureInfo.InvariantCulture);
                DateTime ultimaFechaRetiro = DateTime.Parse(partes[7].Trim(), CultureInfo.InvariantCulture);

                if (!Enum.TryParse(partes[8].Trim(), true, out RolUsuario rol))
                {
                    rol = RolUsuario.Cliente;
                }

                if (!Enum.TryParse(partes[9].Trim(), true, out EstadoUsuario estado))
                {
                    estado = EstadoUsuario.Activo;
                }

                return new Usuario(
                    numeroTarjeta,
                    pin,
                    nombreCompleto,
                    rfidUID,
                    saldo,
                    limiteDiarioRetiro,
                    montoRetiradoHoy,
                    ultimaFechaRetiro,
                    rol,
                    estado
                );
            }
            catch
            {
                // Línea con formato corrupto
                return null;
            }
        }

        private static string SerializarUsuario(Usuario usuario)
        {
            return string.Join(CsvFileContext.Separador,
                usuario.NumeroTarjeta,
                usuario.PinHash,
                usuario.NombreCompleto,
                usuario.RfidUID,
                usuario.Saldo.ToString("F2", CultureInfo.InvariantCulture),
                usuario.LimiteDiarioRetiro.ToString("F2", CultureInfo.InvariantCulture),
                usuario.MontoRetiradoHoy.ToString("F2", CultureInfo.InvariantCulture),
                usuario.UltimaFechaRetiro.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                usuario.Rol.ToString(),
                usuario.Estado.ToString()
            );
        }

        #endregion
    }
}
