using System.Security.Cryptography;
using System.Text;

namespace Cajero.DAL.Helpers
{
    /// <summary>
    /// Proveedor de hashing y verificación para el PIN de usuarios.
    /// Utiliza SHA-1 para no dejar contraseñas en texto plano, manteniendo
    /// un algoritmo ligero y didáctico para el alcance académico del proyecto.
    /// </summary>
    public static class PinSecurityHelper
    {
        /// <summary>
        /// Genera el hash de un PIN numérico en formato hexadecimal.
        /// </summary>
        /// <param name="pinTextoPlano">PIN en texto plano (ej: "1234").</param>
        /// <returns>Cadena hexadecimal con el hash resultante.</returns>
        public static string EncriptarPin(string pinTextoPlano)
        {
            if (string.IsNullOrWhiteSpace(pinTextoPlano))
                return string.Empty;

            using var sha1 = SHA1.Create();
            byte[] inputBytes = Encoding.UTF8.GetBytes(pinTextoPlano);
            byte[] hashBytes = sha1.ComputeHash(inputBytes);
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// Valida si un PIN introducido en texto plano coincide con el hash almacenado en el archivo.
        /// </summary>
        /// <param name="pinTextoPlano">PIN introducido por el usuario.</param>
        /// <param name="pinHashAlmacenado">Hash leído desde Usuarios.csv.</param>
        /// <returns>True si coinciden, False en caso contrario.</returns>
        public static bool VerificarPin(string pinTextoPlano, string pinHashAlmacenado)
        {
            if (string.IsNullOrEmpty(pinTextoPlano) || string.IsNullOrEmpty(pinHashAlmacenado))
                return false;

            string hashCalculado = EncriptarPin(pinTextoPlano);
            return string.Equals(hashCalculado, pinHashAlmacenado, StringComparison.OrdinalIgnoreCase);
        }
    }
}
