namespace Cajero.DAL.Models.Enums
{
    /// <summary>
    /// Define los diferentes tipos de operaciones registradas en el cajero automático.
    /// </summary>
    public enum TipoTransaccion
    {
        Retiro = 1,
        Deposito = 2,
        CambioPin = 3,
        ConsultaSaldo = 4,
        CargaBoveda = 5
    }
}
