using System;
using System.Collections.Generic;
using Cajerro.DAL.Modelos;

namespace Cajerro.DAL.Interfaces
{
    /// <summary>
    /// Contrato para la capa de acceso a datos (DAL) del cajero automático.
    /// Permite desacoplar la lógica de negocio (BLL) de la implementación de persistencia concreta (CSV).
    /// </summary>
    public interface IGestorArchivos
    {
        #region Operaciones de Usuario

        List<Usuario> ObtenerTodosLosUsuarios();
        Usuario? ObtenerUsuarioPorTarjeta(string tarjeta);
        bool CrearNuevoUsuario(Usuario nuevo);
        bool ActualizarSaldo(string tarjeta, decimal nuevoSaldo);
        bool ActualizarUsuario(Usuario usuarioActualizado);

        #endregion

        #region Operaciones de Transacciones

        bool RegistrarTransaccion(Transaccion t);
        List<Transaccion> ObtenerHistorial(string tarjeta);
        List<Transaccion> ObtenerTodasLasTransacciones();

        #endregion

        #region Operaciones de Bóveda

        List<Billete> ObtenerInventarioBilletes();
        bool GuardarInventarioBilletes(List<Billete> inventarioActualizado);
        bool EstaCajeroInicializado();
        bool InicializarCajero(List<Billete> billetesIniciales);
        bool AgregarEfectivoBoveda(List<Billete> billetesRecarga);
        bool ReiniciarInicializacionDia();

        #endregion

        #region Bitácora Administrativa y Control

        bool RegistrarEnBitacora(string tipoEvento, string usuarioOTarjeta, string detalle);
        List<RegistroBitacora> ObtenerBitacora();
        bool ModificarNumeroTarjeta(string tarjetaActual, string nuevaTarjeta);
        List<string> ObtenerTarjetasVinculadas(string tarjeta);
        DateTime? ObtenerUltimoAcceso(string tarjeta);
        RegistroBitacora? ObtenerUltimoAccesoGeneral();

        #endregion

        #region Red Interbancaria, Cajeros en Red y Transferencias

        List<CajeroRedInfo> ObtenerCajerosRed();
        bool GuardarCajerosRed(List<CajeroRedInfo> cajeros);
        List<BancoRedInfo> ObtenerBancosRed();
        bool GuardarBancosRed(List<BancoRedInfo> bancos);
        bool ReiniciarCuposDiariosBancos();
        List<CuentaExternaInfo> ObtenerCuentasExternas(string? codigoBanco = null);
        bool GuardarCuentaExterna(CuentaExternaInfo cuenta);
        bool RegistrarTransferenciaCompartida(RegistroTransferenciaBancaria transferencia);
        List<RegistroTransferenciaBancaria> ObtenerTransferenciasCompartidas();

        #endregion
    }
}
