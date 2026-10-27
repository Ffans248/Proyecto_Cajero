-- ============================================================================
-- SCRIPT DE BASE DE DATOS: BD_CajeroRed
-- FASE II: Red de Cajeros Automáticos Cliente-Servidor con SQL Server
-- ============================================================================

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'BD_CajeroRed')
BEGIN
    CREATE DATABASE BD_CajeroRed;
END
GO

USE BD_CajeroRed;
GO

-- ============================================================================
-- 1. TABLAS DEL SISTEMA
-- ============================================================================

-- Tabla de Parámetros del Sistema
IF OBJECT_ID('dbo.Parametro', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Parametro (
        IdParametro INT IDENTITY(1,1) PRIMARY KEY,
        Clave VARCHAR(50) NOT NULL UNIQUE,
        Valor VARCHAR(100) NOT NULL,
        Descripcion VARCHAR(255) NULL
    );
END
GO

-- Tabla de Usuarios
IF OBJECT_ID('dbo.Usuario', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Usuario (
        IdUsuario INT IDENTITY(1,1) PRIMARY KEY,
        Nombre VARCHAR(100) NOT NULL,
        Saldo DECIMAL(18,2) NOT NULL DEFAULT 0.00 CHECK (Saldo >= 0),
        LimiteDiario DECIMAL(18,2) NOT NULL DEFAULT 2000.00 CHECK (LimiteDiario > 0),
        Estado VARCHAR(20) NOT NULL DEFAULT 'Activo' CHECK (Estado IN ('Activo', 'Bloqueado')),
        IntentosFallidos INT NOT NULL DEFAULT 0 CHECK (IntentosFallidos >= 0),
        FechaCreacion DATETIME NOT NULL DEFAULT GETDATE(),
        Activo BIT NOT NULL DEFAULT 1 -- Baja lógica
    );
END
GO

-- Tabla de Tarjetas
IF OBJECT_ID('dbo.Tarjeta', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Tarjeta (
        IdTarjeta INT IDENTITY(1,1) PRIMARY KEY,
        NumeroTarjeta VARCHAR(16) NOT NULL UNIQUE CHECK (LEN(NumeroTarjeta) = 16 AND NumeroTarjeta NOT LIKE '%[^0-9]%'),
        PinHash VARCHAR(64) NOT NULL,
        PinSalt VARCHAR(36) NOT NULL,
        IdUsuario INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(IdUsuario),
        Activa BIT NOT NULL DEFAULT 1,
        RequiereCambioPin BIT NOT NULL DEFAULT 0,
        FechaAsignacion DATETIME NOT NULL DEFAULT GETDATE(),
        FechaBaja DATETIME NULL
    );
END
GO

-- Tabla de Mapeo RFID (ESP32 a Tarjeta de 16 dígitos)
IF OBJECT_ID('dbo.MapeoRfid', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MapeoRfid (
        IdMapeo INT IDENTITY(1,1) PRIMARY KEY,
        UidRfid VARCHAR(50) NOT NULL UNIQUE,
        NumeroTarjeta VARCHAR(16) NOT NULL FOREIGN KEY REFERENCES dbo.Tarjeta(NumeroTarjeta),
        Descripcion VARCHAR(100) NULL,
        FechaRegistro DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- Tabla de Administradores
IF OBJECT_ID('dbo.Administrador', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Administrador (
        IdAdmin INT IDENTITY(1,1) PRIMARY KEY,
        Usuario VARCHAR(50) NOT NULL UNIQUE,
        PasswordHash VARCHAR(64) NOT NULL,
        PasswordSalt VARCHAR(36) NOT NULL,
        Rol VARCHAR(20) NOT NULL DEFAULT 'Administrador' CHECK (Rol IN ('Administrador', 'Supervisor')),
        Estado VARCHAR(20) NOT NULL DEFAULT 'Activo' CHECK (Estado IN ('Activo', 'Inactivo')),
        FechaCreacion DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- Tabla de Cajeros
IF OBJECT_ID('dbo.Cajero', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cajero (
        IdCajero INT IDENTITY(1,1) PRIMARY KEY,
        Codigo VARCHAR(20) NOT NULL UNIQUE, -- ATM-01, ATM-02...
        Nombre VARCHAR(100) NOT NULL,
        Ubicacion VARCHAR(150) NOT NULL,
        IP VARCHAR(50) NOT NULL DEFAULT '127.0.0.1',
        Estado VARCHAR(30) NOT NULL DEFAULT 'Desconectado' 
            CHECK (Estado IN ('En línea', 'Fuera de servicio', 'Efectivo bajo', 'Desconectado')),
        Inicializado BIT NOT NULL DEFAULT 0,
        FechaUltimaInicializacion DATE NULL,
        UltimoHeartbeat DATETIME NULL,
        Activo BIT NOT NULL DEFAULT 1
    );
END
GO

-- Tabla de Denominaciones de Billetes (Quetzales)
IF OBJECT_ID('dbo.Denominacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Denominacion (
        IdDenominacion INT PRIMARY KEY, -- 200, 100, 50, 20, 10, 5, 1
        Valor INT NOT NULL UNIQUE CHECK (Valor IN (200, 100, 50, 20, 10, 5, 1))
    );
END
GO

-- Tabla de Inventario de Billetes por Cajero
IF OBJECT_ID('dbo.InventarioCajero', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventarioCajero (
        IdInventario INT IDENTITY(1,1) PRIMARY KEY,
        IdCajero INT NOT NULL FOREIGN KEY REFERENCES dbo.Cajero(IdCajero),
        IdDenominacion INT NOT NULL FOREIGN KEY REFERENCES dbo.Denominacion(IdDenominacion),
        Cantidad INT NOT NULL DEFAULT 0 CHECK (Cantidad >= 0),
        CONSTRAINT UQ_Cajero_Denominacion UNIQUE (IdCajero, IdDenominacion)
    );
END
GO

-- Tabla de Carga de Efectivo
IF OBJECT_ID('dbo.CargaEfectivo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CargaEfectivo (
        IdCarga INT IDENTITY(1,1) PRIMARY KEY,
        IdCajero INT NOT NULL FOREIGN KEY REFERENCES dbo.Cajero(IdCajero),
        Tipo VARCHAR(20) NOT NULL CHECK (Tipo IN ('Inicializacion', 'CargaAdicional')),
        Total DECIMAL(18,2) NOT NULL CHECK (Total > 0),
        IdAdmin INT NULL FOREIGN KEY REFERENCES dbo.Administrador(IdAdmin),
        Fecha DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- Detalle de Carga de Efectivo
IF OBJECT_ID('dbo.CargaEfectivoDetalle', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CargaEfectivoDetalle (
        IdCargaDetalle INT IDENTITY(1,1) PRIMARY KEY,
        IdCarga INT NOT NULL FOREIGN KEY REFERENCES dbo.CargaEfectivo(IdCarga) ON DELETE CASCADE,
        IdDenominacion INT NOT NULL FOREIGN KEY REFERENCES dbo.Denominacion(IdDenominacion),
        Cantidad INT NOT NULL CHECK (Cantidad > 0),
        Subtotal DECIMAL(18,2) NOT NULL
    );
END
GO

-- Tabla de Transacciones
IF OBJECT_ID('dbo.Transaccion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Transaccion (
        IdTransaccion INT IDENTITY(1,1) PRIMARY KEY,
        IdUsuario INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(IdUsuario),
        IdTarjeta INT NOT NULL FOREIGN KEY REFERENCES dbo.Tarjeta(IdTarjeta),
        IdCajero INT NOT NULL FOREIGN KEY REFERENCES dbo.Cajero(IdCajero),
        Tipo VARCHAR(20) NOT NULL CHECK (Tipo IN ('RETIRO', 'DEPOSITO', 'CONSULTA', 'CAMBIO_PIN')),
        Monto DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        CodigoRespuesta VARCHAR(10) NOT NULL, -- 00, 51, 61, 55, etc.
        MotivoRechazo VARCHAR(255) NULL,
        Fecha DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- Detalle de Transacciones (Billetes)
IF OBJECT_ID('dbo.TransaccionDetalle', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TransaccionDetalle (
        IdTransaccionDetalle INT IDENTITY(1,1) PRIMARY KEY,
        IdTransaccion INT NOT NULL FOREIGN KEY REFERENCES dbo.Transaccion(IdTransaccion) ON DELETE CASCADE,
        IdDenominacion INT NOT NULL FOREIGN KEY REFERENCES dbo.Denominacion(IdDenominacion),
        Cantidad INT NOT NULL CHECK (Cantidad > 0),
        Subtotal DECIMAL(18,2) NOT NULL
    );
END
GO

-- Historial de Cambios de PIN
IF OBJECT_ID('dbo.CambioPin', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CambioPin (
        IdCambioPin INT IDENTITY(1,1) PRIMARY KEY,
        IdUsuario INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(IdUsuario),
        IdCajero INT NULL FOREIGN KEY REFERENCES dbo.Cajero(IdCajero),
        Fecha DATETIME NOT NULL DEFAULT GETDATE(),
        Origen VARCHAR(20) NOT NULL CHECK (Origen IN ('Usuario', 'Administrador')),
        Resultado VARCHAR(30) NOT NULL
    );
END
GO

-- Historial de Cambios de Límite Diario
IF OBJECT_ID('dbo.HistorialLimite', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HistorialLimite (
        IdHistorial INT IDENTITY(1,1) PRIMARY KEY,
        IdUsuario INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(IdUsuario),
        LimiteAnterior DECIMAL(18,2) NOT NULL,
        LimiteNuevo DECIMAL(18,2) NOT NULL,
        IdAdmin INT NULL FOREIGN KEY REFERENCES dbo.Administrador(IdAdmin),
        Fecha DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- Sesiones de Usuario
IF OBJECT_ID('dbo.Sesion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Sesion (
        IdSesion INT IDENTITY(1,1) PRIMARY KEY,
        IdCajero INT NOT NULL FOREIGN KEY REFERENCES dbo.Cajero(IdCajero),
        IdUsuario INT NULL FOREIGN KEY REFERENCES dbo.Usuario(IdUsuario),
        NumeroTarjeta VARCHAR(16) NOT NULL,
        FechaInicio DATETIME NOT NULL DEFAULT GETDATE(),
        FechaFin DATETIME NULL,
        Resultado VARCHAR(50) NOT NULL,
        IntentosFallidos INT NOT NULL DEFAULT 0
    );
END
GO

-- Tabla de Tokens de 6 Dígitos
IF OBJECT_ID('dbo.Token', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Token (
        IdToken INT IDENTITY(1,1) PRIMARY KEY,
        Codigo VARCHAR(6) NOT NULL,
        IdUsuario INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(IdUsuario),
        IdCajero INT NOT NULL FOREIGN KEY REFERENCES dbo.Cajero(IdCajero),
        FechaCreacion DATETIME NOT NULL DEFAULT GETDATE(),
        FechaExpiracion DATETIME NOT NULL,
        Usado BIT NOT NULL DEFAULT 0
    );
END
GO

-- Tabla de Alertas
IF OBJECT_ID('dbo.Alerta', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Alerta (
        IdAlerta INT IDENTITY(1,1) PRIMARY KEY,
        Tipo VARCHAR(50) NOT NULL, -- Efectivo bajo, Cajero desconectado, Tarjeta bloqueada, Retiro rechazado
        IdCajero INT NULL FOREIGN KEY REFERENCES dbo.Cajero(IdCajero),
        Descripcion VARCHAR(255) NOT NULL,
        Fecha DATETIME NOT NULL DEFAULT GETDATE(),
        Atendida BIT NOT NULL DEFAULT 0,
        FechaAtendida DATETIME NULL,
        IdAdminAtendio INT NULL FOREIGN KEY REFERENCES dbo.Administrador(IdAdmin)
    );
END
GO

-- Tabla de Bitácora de Auditoría
IF OBJECT_ID('dbo.Bitacora', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bitacora (
        IdBitacora INT IDENTITY(1,1) PRIMARY KEY,
        Origen VARCHAR(30) NOT NULL CHECK (Origen IN ('Cajero', 'Servidor', 'Administrador')),
        Accion VARCHAR(100) NOT NULL,
        Detalle VARCHAR(MAX) NOT NULL,
        Fecha DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- ============================================================================
-- 2. VISTAS DEL DASHBOARD
-- ============================================================================

-- Vista: Resumen Diario de Operaciones
CREATE OR ALTER VIEW dbo.vw_ResumenDiario
AS
SELECT 
    (SELECT COUNT(*) FROM dbo.Cajero WHERE Estado = 'En línea' AND Activo = 1) AS CajerosEnLinea,
    (SELECT COUNT(*) FROM dbo.Cajero WHERE Activo = 1) AS TotalCajeros,
    (SELECT ISNULL(SUM(ic.Cantidad * d.Valor), 0) 
     FROM dbo.InventarioCajero ic 
     JOIN dbo.Denominacion d ON ic.IdDenominacion = d.IdDenominacion
     JOIN dbo.Cajero c ON ic.IdCajero = c.IdCajero AND c.Activo = 1) AS EfectivoTotalRed,
    (SELECT ISNULL(SUM(Monto), 0) 
     FROM dbo.Transaccion 
     WHERE Tipo = 'RETIRO' AND CodigoRespuesta = '00' AND CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE)) AS TotalRetiradoHoy,
    (SELECT ISNULL(SUM(Monto), 0) 
     FROM dbo.Transaccion 
     WHERE Tipo = 'DEPOSITO' AND CodigoRespuesta = '00' AND CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE)) AS TotalDepositadoHoy,
    (SELECT CASE WHEN COUNT(DISTINCT IdUsuario) = 0 THEN 0 
                 ELSE ISNULL(SUM(Monto) / COUNT(DISTINCT IdUsuario), 0) END 
     FROM dbo.Transaccion 
     WHERE Tipo = 'DEPOSITO' AND CodigoRespuesta = '00' AND CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE)) AS PromedioDepositadoPorUsuario,
    (SELECT COUNT(*) 
     FROM dbo.Transaccion 
     WHERE CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE)) AS NumeroTransaccionesHoy,
    (SELECT COUNT(*) 
     FROM dbo.CambioPin 
     WHERE CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE)) AS CambiosPinHoy,
    (SELECT COUNT(*) 
     FROM dbo.Transaccion 
     WHERE CodigoRespuesta <> '00' AND CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE)) AS OperacionesRechazadasHoy,
    (SELECT COUNT(*) 
     FROM dbo.Alerta 
     WHERE Atendida = 0) AS AlertasActivas;
GO

-- Vista: Estado Actual de Cajeros e Inventario
CREATE OR ALTER VIEW dbo.vw_EstadoCajeros
AS
SELECT 
    c.IdCajero,
    c.Codigo,
    c.Nombre,
    c.Ubicacion,
    c.IP,
    c.Estado,
    c.Inicializado,
    c.UltimoHeartbeat,
    ISNULL(SUM(ic.Cantidad * d.Valor), 0) AS EfectivoTotal,
    ISNULL(MAX(CASE WHEN d.Valor = 200 THEN ic.Cantidad END), 0) AS Billetes200,
    ISNULL(MAX(CASE WHEN d.Valor = 100 THEN ic.Cantidad END), 0) AS Billetes100,
    ISNULL(MAX(CASE WHEN d.Valor = 50 THEN ic.Cantidad END), 0) AS Billetes50,
    ISNULL(MAX(CASE WHEN d.Valor = 20 THEN ic.Cantidad END), 0) AS Billetes20,
    ISNULL(MAX(CASE WHEN d.Valor = 10 THEN ic.Cantidad END), 0) AS Billetes10,
    ISNULL(MAX(CASE WHEN d.Valor = 5 THEN ic.Cantidad END), 0) AS Billetes5,
    ISNULL(MAX(CASE WHEN d.Valor = 1 THEN ic.Cantidad END), 0) AS Billetes1,
    (SELECT TOP 1 t.Fecha FROM dbo.Transaccion t WHERE t.IdCajero = c.IdCajero ORDER BY t.Fecha DESC) AS UltimaTransaccion
FROM dbo.Cajero c
LEFT JOIN dbo.InventarioCajero ic ON c.IdCajero = ic.IdCajero
LEFT JOIN dbo.Denominacion d ON ic.IdDenominacion = d.IdDenominacion
WHERE c.Activo = 1
GROUP BY c.IdCajero, c.Codigo, c.Nombre, c.Ubicacion, c.IP, c.Estado, c.Inicializado, c.UltimoHeartbeat;
GO

-- Vista: Retirado Hoy Por Usuario (Límite Global)
CREATE OR ALTER VIEW dbo.vw_RetiradoHoyPorUsuario
AS
SELECT 
    u.IdUsuario,
    u.Nombre,
    u.LimiteDiario,
    ISNULL(SUM(t.Monto), 0) AS RetiradoHoy,
    (u.LimiteDiario - ISNULL(SUM(t.Monto), 0)) AS DisponibleHoy
FROM dbo.Usuario u
LEFT JOIN dbo.Transaccion t ON u.IdUsuario = t.IdUsuario 
    AND t.Tipo = 'RETIRO' 
    AND t.CodigoRespuesta = '00' 
    AND CAST(t.Fecha AS DATE) = CAST(GETDATE() AS DATE)
WHERE u.Activo = 1
GROUP BY u.IdUsuario, u.Nombre, u.LimiteDiario;
GO

-- Vista: Cambios de PIN Hoy
CREATE OR ALTER VIEW dbo.vw_CambiosPinHoy
AS
SELECT 
    cp.IdCambioPin,
    u.Nombre AS Usuario,
    c.Codigo AS Cajero,
    cp.Fecha,
    cp.Origen,
    cp.Resultado
FROM dbo.CambioPin cp
JOIN dbo.Usuario u ON cp.IdUsuario = u.IdUsuario
LEFT JOIN dbo.Cajero c ON cp.IdCajero = c.IdCajero
WHERE CAST(cp.Fecha AS DATE) = CAST(GETDATE() AS DATE);
GO

-- ============================================================================
-- 3. TRIGGERS DE AUDITORÍA
-- ============================================================================

-- Trigger: Registrar cambios de límite diario en Usuario
CREATE OR ALTER TRIGGER dbo.trg_Usuario_LimiteDiario
ON dbo.Usuario
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(LimiteDiario)
    BEGIN
        INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
        SELECT 
            'Servidor',
            'Modificación de Límite Diario',
            CONCAT('Usuario ID: ', i.IdUsuario, ' (', i.Nombre, ') cambió su límite de Q ', d.LimiteDiario, ' a Q ', i.LimiteDiario),
            GETDATE()
        FROM inserted i
        JOIN deleted d ON i.IdUsuario = d.IdUsuario
        WHERE i.LimiteDiario <> d.LimiteDiario;
    END
END;
GO

-- Trigger: Registrar cambios en Tarjeta (alta, baja, bloqueo)
CREATE OR ALTER TRIGGER dbo.trg_Tarjeta_Cambio
ON dbo.Tarjeta
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    -- Inserciones
    IF EXISTS (SELECT * FROM inserted) AND NOT EXISTS (SELECT * FROM deleted)
    BEGIN
        INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
        SELECT 
            'Servidor',
            'Nueva Tarjeta Asignada',
            CONCAT('Tarjeta **** ', RIGHT(i.NumeroTarjeta, 4), ' asignada al Usuario ID: ', i.IdUsuario),
            GETDATE()
        FROM inserted i;
    END

    -- Actualizaciones de estado activo
    IF UPDATE(Activa)
    BEGIN
        INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
        SELECT 
            'Servidor',
            CASE WHEN i.Activa = 1 THEN 'Activación de Tarjeta' ELSE 'Baja de Tarjeta' END,
            CONCAT('Tarjeta **** ', RIGHT(i.NumeroTarjeta, 4), ' estado activo: ', i.Activa),
            GETDATE()
        FROM inserted i
        JOIN deleted d ON i.IdTarjeta = d.IdTarjeta
        WHERE i.Activa <> d.Activa;
    END
END;
GO

-- ============================================================================
-- 4. PROCEDIMIENTOS ALMACENADOS OBLIGATORIOS
-- ============================================================================

-- 1. sp_RegistrarCajero
CREATE OR ALTER PROCEDURE dbo.sp_RegistrarCajero
    @Codigo VARCHAR(20),
    @NombreEquipo VARCHAR(100),
    @IP VARCHAR(50),
    @Version VARCHAR(20),
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.Cajero WHERE Codigo = @Codigo AND Activo = 1)
        BEGIN
            SET @CodigoRespuesta = '91';
            SET @Mensaje = CONCAT('El cajero ', @Codigo, ' no está dado de alta en el servidor.');
            RETURN;
        END

        -- Verificar si ya está conectado
        DECLARE @EstadoActual VARCHAR(30);
        SELECT @EstadoActual = Estado FROM dbo.Cajero WHERE Codigo = @Codigo;

        IF @EstadoActual = 'En línea'
        BEGIN
            -- Ya conectado (permitimos reconexión actualizando IP y heartbeat)
            UPDATE dbo.Cajero 
            SET IP = @IP, UltimoHeartbeat = GETDATE(), Estado = 'En línea'
            WHERE Codigo = @Codigo;

            INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
            VALUES ('Cajero', 'Reconexión de Cajero', CONCAT('Cajero ', @Codigo, ' reconectado desde IP ', @IP, ' (Equipo: ', @NombreEquipo, ')'), GETDATE());
        END
        ELSE
        BEGIN
            UPDATE dbo.Cajero 
            SET IP = @IP, UltimoHeartbeat = GETDATE(), Estado = 'En línea'
            WHERE Codigo = @Codigo;

            INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
            VALUES ('Cajero', 'Registro de Cajero', CONCAT('Cajero ', @Codigo, ' conectado con éxito desde IP ', @IP, ' Versión: ', @Version), GETDATE());
        END

        SET @CodigoRespuesta = '00';
        SET @Mensaje = 'Cajero registrado y en línea.';
    END TRY
    BEGIN CATCH
        SET @CodigoRespuesta = '96';
        SET @Mensaje = CONCAT('Error del sistema: ', ERROR_MESSAGE());
    END CATCH
END;
GO

-- 2. sp_Heartbeat
CREATE OR ALTER PROCEDURE dbo.sp_Heartbeat
    @Codigo VARCHAR(20),
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        DECLARE @EstadoAnterior VARCHAR(30);
        SELECT @EstadoAnterior = Estado FROM dbo.Cajero WHERE Codigo = @Codigo;

        IF @EstadoAnterior IS NULL
        BEGIN
            SET @CodigoRespuesta = '91';
            SET @Mensaje = 'Cajero no registrado.';
            RETURN;
        END

        UPDATE dbo.Cajero 
        SET UltimoHeartbeat = GETDATE(),
            Estado = CASE WHEN Estado = 'Desconectado' THEN 'En línea' ELSE Estado END
        WHERE Codigo = @Codigo;

        IF @EstadoAnterior = 'Desconectado'
        BEGIN
            INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
            VALUES ('Cajero', 'Cajero Reconectado', CONCAT('Cajero ', @Codigo, ' reanudó señales de Heartbeat.'), GETDATE());
        END

        SET @CodigoRespuesta = '00';
        SET @Mensaje = 'Heartbeat recibido correctamente.';
    END TRY
    BEGIN CATCH
        SET @CodigoRespuesta = '96';
        SET @Mensaje = ERROR_MESSAGE();
    END CATCH
END;
GO

-- 3. sp_ValidarCredenciales (Login)
CREATE OR ALTER PROCEDURE dbo.sp_ValidarCredenciales
    @NumeroTarjeta VARCHAR(16),
    @Pin VARCHAR(4),
    @CodigoCajero VARCHAR(20),
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT,
    @IdUsuario INT OUTPUT,
    @NombreUsuario VARCHAR(100) OUTPUT,
    @RequiereCambioPin BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SET @IdUsuario = 0;
        SET @NombreUsuario = '';
        SET @RequiereCambioPin = 0;

        -- Validar si el cajero existe y está disponible
        DECLARE @IdCajero INT, @EstadoCajero VARCHAR(30), @Inicializado BIT;
        SELECT @IdCajero = IdCajero, @EstadoCajero = Estado, @Inicializado = Inicializado 
        FROM dbo.Cajero WHERE Codigo = @CodigoCajero;

        IF @IdCajero IS NULL OR @EstadoCajero = 'Fuera de servicio'
        BEGIN
            SET @CodigoRespuesta = '91';
            SET @Mensaje = 'Cajero fuera de servicio o no disponible.';
            RETURN;
        END

        -- Validar tarjeta
        DECLARE @IdTarjeta INT, @PinHash VARCHAR(64), @PinSalt VARCHAR(36), @TarjetaActiva BIT;
        DECLARE @EstadoUsuario VARCHAR(20), @IntentosFallidos INT, @UsuarioActivo BIT;

        SELECT 
            @IdTarjeta = t.IdTarjeta,
            @IdUsuario = t.IdUsuario,
            @PinHash = t.PinHash,
            @PinSalt = t.PinSalt,
            @TarjetaActiva = t.Activa,
            @RequiereCambioPin = t.RequiereCambioPin,
            @NombreUsuario = u.Nombre,
            @EstadoUsuario = u.Estado,
            @IntentosFallidos = u.IntentosFallidos,
            @UsuarioActivo = u.Activo
        FROM dbo.Tarjeta t
        JOIN dbo.Usuario u ON t.IdUsuario = u.IdUsuario
        WHERE t.NumeroTarjeta = @NumeroTarjeta;

        IF @IdTarjeta IS NULL
        BEGIN
            SET @CodigoRespuesta = '55';
            SET @Mensaje = 'Tarjeta no válida.';
            RETURN;
        END

        IF @TarjetaActiva = 0 OR @UsuarioActivo = 0 OR @EstadoUsuario = 'Bloqueado' OR @IntentosFallidos >= 3
        BEGIN
            SET @CodigoRespuesta = '75';
            SET @Mensaje = 'Tarjeta bloqueada o inactiva por intentos fallidos. Contacte a un administrador.';
            RETURN;
        END

        -- Validar PIN con SHA2_256
        DECLARE @CalculatedHash VARCHAR(64);
        SET @CalculatedHash = CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CONCAT(@Pin, @PinSalt)), 2);

        IF @CalculatedHash <> @PinHash
        BEGIN
            -- Incrementar intentos fallidos
            UPDATE dbo.Usuario 
            SET IntentosFallidos = IntentosFallidos + 1,
                Estado = CASE WHEN IntentosFallidos + 1 >= 3 THEN 'Bloqueado' ELSE Estado END
            WHERE IdUsuario = @IdUsuario;

            DECLARE @NuevosIntentos INT;
            SELECT @NuevosIntentos = IntentosFallidos FROM dbo.Usuario WHERE IdUsuario = @IdUsuario;

            INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
            VALUES ('Cajero', 'Intento Fallido de PIN', 
                    CONCAT('Tarjeta **** ', RIGHT(@NumeroTarjeta, 4), ' en ', @CodigoCajero, '. Intento fallido #', @NuevosIntentos), GETDATE());

            IF @NuevosIntentos >= 3
            BEGIN
                -- Generar Alerta
                INSERT INTO dbo.Alerta (Tipo, IdCajero, Descripcion, Fecha)
                VALUES ('Tarjeta Bloqueada', @IdCajero, CONCAT('La tarjeta **** ', RIGHT(@NumeroTarjeta, 4), ' ha sido bloqueada tras 3 intentos fallidos.'), GETDATE());

                SET @CodigoRespuesta = '75';
                SET @Mensaje = 'Tarjeta bloqueada por 3 intentos fallidos consecutivos en la red.';
            END
            ELSE
            BEGIN
                SET @CodigoRespuesta = '55';
                SET @Mensaje = CONCAT('PIN incorrecto. Intento ', @NuevosIntentos, ' de 3.');
            END
            RETURN;
        END

        -- Credenciales correctas: Reiniciar contador de intentos fallidos
        UPDATE dbo.Usuario SET IntentosFallidos = 0 WHERE IdUsuario = @IdUsuario;

        SET @CodigoRespuesta = '00';
        SET @Mensaje = 'Credenciales válidas. Se generará el token de seguridad.';
    END TRY
    BEGIN CATCH
        SET @CodigoRespuesta = '96';
        SET @Mensaje = ERROR_MESSAGE();
    END CATCH
END;
GO

-- 4. sp_GenerarToken
CREATE OR ALTER PROCEDURE dbo.sp_GenerarToken
    @IdUsuario INT,
    @CodigoCajero VARCHAR(20),
    @TokenGenerado VARCHAR(6) OUTPUT,
    @FechaExpiracion DATETIME OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        DECLARE @IdCajero INT;
        SELECT @IdCajero = IdCajero FROM dbo.Cajero WHERE Codigo = @CodigoCajero;

        -- Invalidar tokens previos sin usar para este usuario y cajero
        UPDATE dbo.Token 
        SET Usado = 1 
        WHERE IdUsuario = @IdUsuario AND IdCajero = @IdCajero AND Usado = 0;

        -- Generar número aleatorio de 6 dígitos entre 100000 y 999999
        DECLARE @Random INT;
        SET @Random = FLOOR(RAND(CHECKSUM(NEWID())) * 900000) + 100000;
        SET @TokenGenerado = CAST(@Random AS VARCHAR(6));

        -- Vigencia por parámetro (por defecto 120 segundos / 2 minutos)
        DECLARE @SegundosVigencia INT = 120;
        SELECT @SegundosVigencia = CAST(Valor AS INT) FROM dbo.Parametro WHERE Clave = 'VigenciaTokenSegundos';

        SET @FechaExpiracion = DATEADD(SECOND, @SegundosVigencia, GETDATE());

        INSERT INTO dbo.Token (Codigo, IdUsuario, IdCajero, FechaCreacion, FechaExpiracion, Usado)
        VALUES (@TokenGenerado, @IdUsuario, @IdCajero, GETDATE(), @FechaExpiracion, 0);

        INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
        VALUES ('Servidor', 'Generación de Token', 
                CONCAT('Token de seguridad generado para usuario ID: ', @IdUsuario, ' en ', @CodigoCajero, ' (Expira en ', @SegundosVigencia, 's)'), GETDATE());
    END TRY
    BEGIN CATCH
        SET @TokenGenerado = '';
        THROW;
    END CATCH
END;
GO

-- 5. sp_ValidarToken
CREATE OR ALTER PROCEDURE dbo.sp_ValidarToken
    @CodigoCajero VARCHAR(20),
    @NumeroTarjeta VARCHAR(16),
    @Token VARCHAR(6),
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT,
    @IdUsuario INT OUTPUT,
    @NombreUsuario VARCHAR(100) OUTPUT,
    @Saldo DECIMAL(18,2) OUTPUT,
    @LimiteDiario DECIMAL(18,2) OUTPUT,
    @DisponibleHoy DECIMAL(18,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SET @IdUsuario = 0;
        SET @NombreUsuario = '';
        SET @Saldo = 0.00;
        SET @LimiteDiario = 0.00;
        SET @DisponibleHoy = 0.00;

        DECLARE @IdCajero INT;
        SELECT @IdCajero = IdCajero FROM dbo.Cajero WHERE Codigo = @CodigoCajero;

        SELECT @IdUsuario = t.IdUsuario, @NombreUsuario = u.Nombre, @Saldo = u.Saldo, @LimiteDiario = u.LimiteDiario
        FROM dbo.Tarjeta t
        JOIN dbo.Usuario u ON t.IdUsuario = u.IdUsuario
        WHERE t.NumeroTarjeta = @NumeroTarjeta AND t.Activa = 1;

        IF @IdUsuario = 0
        BEGIN
            SET @CodigoRespuesta = '55';
            SET @Mensaje = 'Usuario no encontrado.';
            RETURN;
        END

        -- Validar token existente para este usuario y cajero
        DECLARE @IdToken INT, @FechaExpiracion DATETIME, @Usado BIT;
        SELECT TOP 1 
            @IdToken = IdToken,
            @FechaExpiracion = FechaExpiracion,
            @Usado = Usado
        FROM dbo.Token
        WHERE Codigo = @Token AND IdUsuario = @IdUsuario AND IdCajero = @IdCajero
        ORDER BY FechaCreacion DESC;

        IF @IdToken IS NULL OR @Usado = 1 OR GETDATE() > @FechaExpiracion
        BEGIN
            -- Incrementar intento fallido en usuario
            UPDATE dbo.Usuario 
            SET IntentosFallidos = IntentosFallidos + 1,
                Estado = CASE WHEN IntentosFallidos + 1 >= 3 THEN 'Bloqueado' ELSE Estado END
            WHERE IdUsuario = @IdUsuario;

            INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
            VALUES ('Cajero', 'Token Inválido o Expirado', 
                    CONCAT('Intento de validar token fallido en ', @CodigoCajero, ' para tarjeta **** ', RIGHT(@NumeroTarjeta, 4)), GETDATE());

            SET @CodigoRespuesta = '56';
            SET @Mensaje = 'Token inválido o expirado (vigencia máxima 2 minutos).';
            RETURN;
        END

        -- Marcar token como consumido (un solo uso)
        UPDATE dbo.Token SET Usado = 1 WHERE IdToken = @IdToken;

        -- Registrar inicio de sesión
        INSERT INTO dbo.Sesion (IdCajero, IdUsuario, NumeroTarjeta, FechaInicio, Resultado)
        VALUES (@IdCajero, @IdUsuario, @NumeroTarjeta, GETDATE(), 'Acceso Exitoso');

        -- Calcular disponible hoy
        DECLARE @RetiradoHoy DECIMAL(18,2);
        SELECT @RetiradoHoy = ISNULL(SUM(Monto), 0)
        FROM dbo.Transaccion
        WHERE IdUsuario = @IdUsuario AND Tipo = 'RETIRO' AND CodigoRespuesta = '00'
          AND CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE);

        SET @DisponibleHoy = @LimiteDiario - @RetiradoHoy;
        IF @DisponibleHoy < 0 SET @DisponibleHoy = 0.00;

        SET @CodigoRespuesta = '00';
        SET @Mensaje = 'Token validado con éxito. Sesión iniciada.';
    END TRY
    BEGIN CATCH
        SET @CodigoRespuesta = '96';
        SET @Mensaje = ERROR_MESSAGE();
    END CATCH
END;
GO

-- 6. sp_Retiro (Atómico, con control de concurrencia y límite diario global)
CREATE OR ALTER PROCEDURE dbo.sp_Retiro
    @CodigoCajero VARCHAR(20),
    @NumeroTarjeta VARCHAR(16),
    @Monto DECIMAL(18,2),
    @DesgloseJson NVARCHAR(MAX), -- Formato: '{"200":1, "100":2, ...}'
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT,
    @IdTransaccion INT OUTPUT,
    @NuevoSaldo DECIMAL(18,2) OUTPUT,
    @DisponibleHoy DECIMAL(18,2) OUTPUT,
    @DesgloseAlternativoJson NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @IdTransaccion = 0;
    SET @NuevoSaldo = 0.00;
    SET @DisponibleHoy = 0.00;
    SET @DesgloseAlternativoJson = NULL;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Obtener y bloquear cajero
        DECLARE @IdCajero INT, @EstadoCajero VARCHAR(30), @Inicializado BIT;
        SELECT @IdCajero = IdCajero, @EstadoCajero = Estado, @Inicializado = Inicializado 
        FROM dbo.Cajero WITH (UPDLOCK, ROWLOCK)
        WHERE Codigo = @CodigoCajero;

        IF @IdCajero IS NULL OR @EstadoCajero = 'Fuera de servicio'
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '91';
            SET @Mensaje = 'Cajero fuera de servicio o no inicializado.';
            RETURN;
        END

        -- Obtener y bloquear usuario y tarjeta
        DECLARE @IdUsuario INT, @IdTarjeta INT, @SaldoActual DECIMAL(18,2), @LimiteDiario DECIMAL(18,2), @EstadoUsuario VARCHAR(20);
        SELECT 
            @IdUsuario = u.IdUsuario,
            @IdTarjeta = t.IdTarjeta,
            @SaldoActual = u.Saldo,
            @LimiteDiario = u.LimiteDiario,
            @EstadoUsuario = u.Estado
        FROM dbo.Usuario u WITH (UPDLOCK, ROWLOCK)
        JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario
        WHERE t.NumeroTarjeta = @NumeroTarjeta AND t.Activa = 1;

        IF @IdUsuario IS NULL OR @EstadoUsuario <> 'Activo'
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '75';
            SET @Mensaje = 'Tarjeta inactiva o bloqueada.';
            RETURN;
        END

        -- 1. Validar Saldo del Usuario
        IF @SaldoActual < @Monto
        BEGIN
            INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, Fecha)
            VALUES (@IdUsuario, @IdTarjeta, @IdCajero, 'RETIRO', @Monto, '51', 'Saldo insuficiente en la cuenta', GETDATE());
            SET @IdTransaccion = SCOPE_IDENTITY();

            COMMIT TRANSACTION;
            SET @CodigoRespuesta = '51';
            SET @Mensaje = 'Saldo insuficiente para realizar el retiro.';
            SET @NuevoSaldo = @SaldoActual;
            RETURN;
        END

        -- 2. Validar Límite Diario Global (en todos los cajeros y tarjetas del usuario hoy)
        DECLARE @RetiradoHoy DECIMAL(18,2);
        SELECT @RetiradoHoy = ISNULL(SUM(t.Monto), 0)
        FROM dbo.Transaccion t WITH (UPDLOCK)
        WHERE t.IdUsuario = @IdUsuario 
          AND t.Tipo = 'RETIRO' 
          AND t.CodigoRespuesta = '00' 
          AND CAST(t.Fecha AS DATE) = CAST(GETDATE() AS DATE);

        IF (@RetiradoHoy + @Monto) > @LimiteDiario
        BEGIN
            DECLARE @DispRestante DECIMAL(18,2) = @LimiteDiario - @RetiradoHoy;
            IF @DispRestante < 0 SET @DispRestante = 0.00;

            INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, Fecha)
            VALUES (@IdUsuario, @IdTarjeta, @IdCajero, 'RETIRO', @Monto, '61', 
                    CONCAT('Excede límite diario. Retirado hoy: Q ', @RetiradoHoy, ' / Límite: Q ', @LimiteDiario), GETDATE());
            SET @IdTransaccion = SCOPE_IDENTITY();

            COMMIT TRANSACTION;
            SET @CodigoRespuesta = '61';
            SET @Mensaje = CONCAT('Excede el límite diario disponible. Disponible hoy: Q ', @DispRestante);
            SET @NuevoSaldo = @SaldoActual;
            SET @DisponibleHoy = @DispRestante;
            RETURN;
        END

        -- 3. Parsear el desglose solicitado
        CREATE TABLE #DesgloseSolicitado (
            Denominacion INT,
            Cantidad INT
        );

        INSERT INTO #DesgloseSolicitado (Denominacion, Cantidad)
        SELECT CAST([key] AS INT), CAST([value] AS INT)
        FROM OPENJSON(@DesgloseJson);

        -- Verificar que la suma del desglose coincida con el monto
        DECLARE @SumaDesglose DECIMAL(18,2);
        SELECT @SumaDesglose = ISNULL(SUM(Denominacion * Cantidad), 0) FROM #DesgloseSolicitado;

        IF @SumaDesglose <> @Monto
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'El desglose de billetes no coincide con el monto total solicitado.';
            RETURN;
        END

        -- 4. Verificar inventario del cajero para el desglose solicitado
        DECLARE @FaltanBilletes BIT = 0;
        IF EXISTS (
            SELECT 1 
            FROM #DesgloseSolicitado ds
            JOIN dbo.InventarioCajero ic WITH (UPDLOCK) ON ic.IdCajero = @IdCajero AND ic.IdDenominacion = ds.Denominacion
            WHERE ic.Cantidad < ds.Cantidad
        )
        BEGIN
            SET @FaltanBilletes = 1;
        END

        -- Si no hay suficiente inventario para el desglose pedido:
        IF @FaltanBilletes = 1
        BEGIN
            -- Calcular si el cajero puede cubrir el monto total con otro desglose
            -- Algoritmo voraz (greedy) de mayor a menor denominación
            DECLARE @Restante INT = CAST(@Monto AS INT);
            DECLARE @Cant200 INT = 0, @Cant100 INT = 0, @Cant50 INT = 0, @Cant20 INT = 0, @Cant10 INT = 0, @Cant5 INT = 0, @Cant1 INT = 0;
            DECLARE @Disp200 INT, @Disp100 INT, @Disp50 INT, @Disp20 INT, @Disp10 INT, @Disp5 INT, @Disp1 INT;

            SELECT @Disp200 = ISNULL(SUM(CASE WHEN IdDenominacion = 200 THEN Cantidad END), 0),
                   @Disp100 = ISNULL(SUM(CASE WHEN IdDenominacion = 100 THEN Cantidad END), 0),
                   @Disp50  = ISNULL(SUM(CASE WHEN IdDenominacion = 50  THEN Cantidad END), 0),
                   @Disp20  = ISNULL(SUM(CASE WHEN IdDenominacion = 20  THEN Cantidad END), 0),
                   @Disp10  = ISNULL(SUM(CASE WHEN IdDenominacion = 10  THEN Cantidad END), 0),
                   @Disp5   = ISNULL(SUM(CASE WHEN IdDenominacion = 5   THEN Cantidad END), 0),
                   @Disp1   = ISNULL(SUM(CASE WHEN IdDenominacion = 1   THEN Cantidad END), 0)
            FROM dbo.InventarioCajero WHERE IdCajero = @IdCajero;

            -- 200
            IF @Restante >= 200 AND @Disp200 > 0
            BEGIN
                SET @Cant200 = @Restante / 200;
                IF @Cant200 > @Disp200 SET @Cant200 = @Disp200;
                SET @Restante = @Restante - (@Cant200 * 200);
            END
            -- 100
            IF @Restante >= 100 AND @Disp100 > 0
            BEGIN
                SET @Cant100 = @Restante / 100;
                IF @Cant100 > @Disp100 SET @Cant100 = @Disp100;
                SET @Restante = @Restante - (@Cant100 * 100);
            END
            -- 50
            IF @Restante >= 50 AND @Disp50 > 0
            BEGIN
                SET @Cant50 = @Restante / 50;
                IF @Cant50 > @Disp50 SET @Cant50 = @Disp50;
                SET @Restante = @Restante - (@Cant50 * 50);
            END
            -- 20
            IF @Restante >= 20 AND @Disp20 > 0
            BEGIN
                SET @Cant20 = @Restante / 20;
                IF @Cant20 > @Disp20 SET @Cant20 = @Disp20;
                SET @Restante = @Restante - (@Cant20 * 20);
            END
            -- 10
            IF @Restante >= 10 AND @Disp10 > 0
            BEGIN
                SET @Cant10 = @Restante / 10;
                IF @Cant10 > @Disp10 SET @Cant10 = @Disp10;
                SET @Restante = @Restante - (@Cant10 * 10);
            END
            -- 5
            IF @Restante >= 5 AND @Disp5 > 0
            BEGIN
                SET @Cant5 = @Restante / 5;
                IF @Cant5 > @Disp5 SET @Cant5 = @Disp5;
                SET @Restante = @Restante - (@Cant5 * 5);
            END
            -- 1
            IF @Restante >= 1 AND @Disp1 > 0
            BEGIN
                SET @Cant1 = @Restante;
                IF @Cant1 > @Disp1 SET @Cant1 = @Disp1;
                SET @Restante = @Restante - @Cant1;
            END

            IF @Restante = 0
            BEGIN
                -- Construir JSON alternativo
                SET @DesgloseAlternativoJson = CONCAT(
                    '{"200":', @Cant200, 
                    ',"100":', @Cant100, 
                    ',"50":', @Cant50, 
                    ',"20":', @Cant20, 
                    ',"10":', @Cant10, 
                    ',"5":', @Cant5, 
                    ',"1":', @Cant1, '}'
                );
            END

            -- Registrar intento rechazado por falta de billetes
            INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, Fecha)
            VALUES (@IdUsuario, @IdTarjeta, @IdCajero, 'RETIRO', @Monto, '92', 'Billetes solicitados no disponibles', GETDATE());
            SET @IdTransaccion = SCOPE_IDENTITY();

            -- Generar Alerta
            INSERT INTO dbo.Alerta (Tipo, IdCajero, Descripcion, Fecha)
            VALUES ('Retiro Rechazado por Billetes', @IdCajero, 
                    CONCAT('Retiro de Q ', @Monto, ' no pudo ser satisfecho con el desglose solicitado en ', @CodigoCajero), GETDATE());

            COMMIT TRANSACTION;
            SET @CodigoRespuesta = '92';
            SET @Mensaje = 'El cajero no tiene los billetes necesarios para el desglose pedido.';
            SET @NuevoSaldo = @SaldoActual;
            RETURN;
        END

        -- 5. Aplicar Retiro Exitoso
        -- Descontar saldo usuario
        UPDATE dbo.Usuario 
        SET Saldo = Saldo - @Monto 
        WHERE IdUsuario = @IdUsuario;

        -- Descontar inventario de billetes en el cajero
        UPDATE ic
        SET ic.Cantidad = ic.Cantidad - ds.Cantidad
        FROM dbo.InventarioCajero ic
        JOIN #DesgloseSolicitado ds ON ic.IdDenominacion = ds.Denominacion
        WHERE ic.IdCajero = @IdCajero;

        -- Registrar transacción aprobada
        INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, Fecha)
        VALUES (@IdUsuario, @IdTarjeta, @IdCajero, 'RETIRO', @Monto, '00', NULL, GETDATE());
        SET @IdTransaccion = SCOPE_IDENTITY();

        -- Registrar detalle
        INSERT INTO dbo.TransaccionDetalle (IdTransaccion, IdDenominacion, Cantidad, Subtotal)
        SELECT @IdTransaccion, Denominacion, Cantidad, (Denominacion * Cantidad)
        FROM #DesgloseSolicitado
        WHERE Cantidad > 0;

        -- Actualizar saldo y disponible de salida
        SET @NuevoSaldo = @SaldoActual - @Monto;
        SET @DisponibleHoy = @LimiteDiario - (@RetiradoHoy + @Monto);

        -- Verificar si el efectivo del cajero cayó por debajo del umbral bajo
        DECLARE @EfectivoRestanteCajero DECIMAL(18,2), @UmbralBajo DECIMAL(18,2) = 1000.00;
        SELECT @UmbralBajo = CAST(Valor AS DECIMAL(18,2)) FROM dbo.Parametro WHERE Clave = 'UmbralEfectivoBajo';

        SELECT @EfectivoRestanteCajero = ISNULL(SUM(ic.Cantidad * d.Valor), 0)
        FROM dbo.InventarioCajero ic
        JOIN dbo.Denominacion d ON ic.IdDenominacion = d.IdDenominacion
        WHERE ic.IdCajero = @IdCajero;

        IF @EfectivoRestanteCajero < @UmbralBajo
        BEGIN
            UPDATE dbo.Cajero SET Estado = 'Efectivo bajo' WHERE IdCajero = @IdCajero AND Estado = 'En línea';

            INSERT INTO dbo.Alerta (Tipo, IdCajero, Descripcion, Fecha)
            VALUES ('Efectivo Bajo', @IdCajero, 
                    CONCAT('El efectivo en ', @CodigoCajero, ' es de Q ', @EfectivoRestanteCajero, ' (menor al umbral de Q ', @UmbralBajo, ')'), GETDATE());
        END

        COMMIT TRANSACTION;
        SET @CodigoRespuesta = '00';
        SET @Mensaje = 'Retiro aprobado exitosamente.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @CodigoRespuesta = '96';
        SET @Mensaje = CONCAT('Error del sistema en retiro: ', ERROR_MESSAGE());
    END CATCH
END;
GO

-- 7. sp_Deposito (Atómico, valida capacidad máxima Q 30,000.00)
CREATE OR ALTER PROCEDURE dbo.sp_Deposito
    @CodigoCajero VARCHAR(20),
    @NumeroTarjeta VARCHAR(16),
    @Monto DECIMAL(18,2),
    @DesgloseJson NVARCHAR(MAX),
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT,
    @IdTransaccion INT OUTPUT,
    @NuevoSaldo DECIMAL(18,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @IdTransaccion = 0;
    SET @NuevoSaldo = 0.00;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Obtener cajero con bloqueo
        DECLARE @IdCajero INT, @EstadoCajero VARCHAR(30);
        SELECT @IdCajero = IdCajero, @EstadoCajero = Estado 
        FROM dbo.Cajero WITH (UPDLOCK, ROWLOCK) 
        WHERE Codigo = @CodigoCajero;

        IF @IdCajero IS NULL OR @EstadoCajero = 'Fuera de servicio'
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '91';
            SET @Mensaje = 'Cajero no disponible o fuera de servicio.';
            RETURN;
        END

        -- Validar capacidad máxima de efectivo en el cajero (Q 30,000.00)
        DECLARE @EfectivoActualCajero DECIMAL(18,2), @CapacidadMaxima DECIMAL(18,2) = 30000.00;
        SELECT @CapacidadMaxima = CAST(Valor AS DECIMAL(18,2)) FROM dbo.Parametro WHERE Clave = 'CapacidadMaximaCajero';

        SELECT @EfectivoActualCajero = ISNULL(SUM(ic.Cantidad * d.Valor), 0)
        FROM dbo.InventarioCajero ic WITH (UPDLOCK)
        JOIN dbo.Denominacion d ON ic.IdDenominacion = d.IdDenominacion
        WHERE ic.IdCajero = @IdCajero;

        IF (@EfectivoActualCajero + @Monto) > @CapacidadMaxima
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '93';
            SET @Mensaje = CONCAT('Excede la capacidad de efectivo del cajero (Capacidad: Q ', @CapacidadMaxima, ', Actual: Q ', @EfectivoActualCajero, ').');
            RETURN;
        END

        -- Obtener usuario y tarjeta
        DECLARE @IdUsuario INT, @IdTarjeta INT, @SaldoActual DECIMAL(18,2);
        SELECT @IdUsuario = u.IdUsuario, @IdTarjeta = t.IdTarjeta, @SaldoActual = u.Saldo
        FROM dbo.Usuario u WITH (UPDLOCK, ROWLOCK)
        JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario
        WHERE t.NumeroTarjeta = @NumeroTarjeta AND t.Activa = 1;

        IF @IdUsuario IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '75';
            SET @Mensaje = 'Tarjeta inactiva o inexistente.';
            RETURN;
        END

        -- Parsear desglose
        CREATE TABLE #DesgloseDeposito (
            Denominacion INT,
            Cantidad INT
        );

        INSERT INTO #DesgloseDeposito (Denominacion, Cantidad)
        SELECT CAST([key] AS INT), CAST([value] AS INT)
        FROM OPENJSON(@DesgloseJson);

        -- Incrementar saldo usuario
        UPDATE dbo.Usuario 
        SET Saldo = Saldo + @Monto 
        WHERE IdUsuario = @IdUsuario;

        -- Incrementar inventario del cajero
        UPDATE ic
        SET ic.Cantidad = ic.Cantidad + dd.Cantidad
        FROM dbo.InventarioCajero ic
        JOIN #DesgloseDeposito dd ON ic.IdDenominacion = dd.Denominacion
        WHERE ic.IdCajero = @IdCajero;

        -- Registrar transacción
        INSERT INTO dbo.Transaccion (IdUsuario, IdTarjeta, IdCajero, Tipo, Monto, CodigoRespuesta, MotivoRechazo, Fecha)
        VALUES (@IdUsuario, @IdTarjeta, @IdCajero, 'DEPOSITO', @Monto, '00', NULL, GETDATE());
        SET @IdTransaccion = SCOPE_IDENTITY();

        -- Registrar detalle
        INSERT INTO dbo.TransaccionDetalle (IdTransaccion, IdDenominacion, Cantidad, Subtotal)
        SELECT @IdTransaccion, Denominacion, Cantidad, (Denominacion * Cantidad)
        FROM #DesgloseDeposito
        WHERE Cantidad > 0;

        SET @NuevoSaldo = @SaldoActual + @Monto;

        -- Si el cajero estaba en 'Efectivo bajo' y ahora superó el umbral, restaurar a 'En línea'
        DECLARE @UmbralBajo DECIMAL(18,2) = 1000.00;
        SELECT @UmbralBajo = CAST(Valor AS DECIMAL(18,2)) FROM dbo.Parametro WHERE Clave = 'UmbralEfectivoBajo';
        IF (@EfectivoActualCajero + @Monto) >= @UmbralBajo AND @EstadoCajero = 'Efectivo bajo'
        BEGIN
            UPDATE dbo.Cajero SET Estado = 'En línea' WHERE IdCajero = @IdCajero;
        END

        COMMIT TRANSACTION;
        SET @CodigoRespuesta = '00';
        SET @Mensaje = 'Depósito procesado exitosamente.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @CodigoRespuesta = '96';
        SET @Mensaje = CONCAT('Error del sistema en depósito: ', ERROR_MESSAGE());
    END CATCH
END;
GO

-- 8. sp_CambiarPin
CREATE OR ALTER PROCEDURE dbo.sp_CambiarPin
    @NumeroTarjeta VARCHAR(16),
    @PinActual VARCHAR(4),
    @PinNuevo VARCHAR(4),
    @Origen VARCHAR(20), -- 'Usuario' o 'Administrador'
    @CodigoCajero VARCHAR(20),
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF LEN(@PinNuevo) <> 4 OR @PinNuevo LIKE '%[^0-9]%'
        BEGIN
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'El nuevo PIN debe constar exactamente de 4 dígitos numéricos.';
            RETURN;
        END

        IF @PinActual = @PinNuevo
        BEGIN
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'El nuevo PIN debe ser diferente al actual.';
            RETURN;
        END

        DECLARE @IdTarjeta INT, @IdUsuario INT, @PinHash VARCHAR(64), @PinSalt VARCHAR(36);
        SELECT @IdTarjeta = IdTarjeta, @IdUsuario = IdUsuario, @PinHash = PinHash, @PinSalt = PinSalt
        FROM dbo.Tarjeta
        WHERE NumeroTarjeta = @NumeroTarjeta AND Activa = 1;

        IF @IdTarjeta IS NULL
        BEGIN
            SET @CodigoRespuesta = '75';
            SET @Mensaje = 'Tarjeta inactiva o no encontrada.';
            RETURN;
        END

        -- Si el origen es Usuario, validar el PIN actual
        IF @Origen = 'Usuario'
        BEGIN
            DECLARE @CalculatedHash VARCHAR(64);
            SET @CalculatedHash = CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CONCAT(@PinActual, @PinSalt)), 2);

            IF @CalculatedHash <> @PinHash
            BEGIN
                SET @CodigoRespuesta = '55';
                SET @Mensaje = 'PIN actual incorrecto.';
                RETURN;
            END
        END

        -- Generar nuevo salt y hash para el nuevo PIN
        DECLARE @NuevoSalt VARCHAR(36) = NEWID();
        DECLARE @NuevoHash VARCHAR(64);
        SET @NuevoHash = CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CONCAT(@PinNuevo, @NuevoSalt)), 2);

        UPDATE dbo.Tarjeta 
        SET PinHash = @NuevoHash, PinSalt = @NuevoSalt, RequiereCambioPin = 0
        WHERE IdTarjeta = @IdTarjeta;

        DECLARE @IdCajero INT;
        SELECT @IdCajero = IdCajero FROM dbo.Cajero WHERE Codigo = @CodigoCajero;

        INSERT INTO dbo.CambioPin (IdUsuario, IdCajero, Fecha, Origen, Resultado)
        VALUES (@IdUsuario, @IdCajero, GETDATE(), @Origen, 'Exitoso');

        INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
        VALUES (
            CASE WHEN @Origen = 'Usuario' THEN 'Cajero' ELSE 'Administrador' END,
            'Cambio de PIN',
            CONCAT('Cambio de PIN exitoso para tarjeta **** ', RIGHT(@NumeroTarjeta, 4), ' (Origen: ', @Origen, ')'),
            GETDATE()
        );

        SET @CodigoRespuesta = '00';
        SET @Mensaje = 'PIN actualizado con éxito. El nuevo PIN ya está activo en toda la red.';
    END TRY
    BEGIN CATCH
        SET @CodigoRespuesta = '96';
        SET @Mensaje = ERROR_MESSAGE();
    END CATCH
END;
GO

-- 9. sp_InicializarCajero (Una vez al día, máximo Q 10,000.00)
CREATE OR ALTER PROCEDURE dbo.sp_InicializarCajero
    @CodigoCajero VARCHAR(20),
    @IdAdmin INT,
    @DesgloseJson NVARCHAR(MAX),
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @IdCajero INT, @FechaUltimaInicializacion DATE;
        SELECT @IdCajero = IdCajero, @FechaUltimaInicializacion = FechaUltimaInicializacion
        FROM dbo.Cajero WITH (UPDLOCK)
        WHERE Codigo = @CodigoCajero AND Activo = 1;

        IF @IdCajero IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '91';
            SET @Mensaje = 'Cajero no encontrado.';
            RETURN;
        END

        IF @FechaUltimaInicializacion = CAST(GETDATE() AS DATE)
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'Este cajero ya fue inicializado el día de hoy. Utilice la opción de Agregar Efectivo.';
            RETURN;
        END

        CREATE TABLE #DesgloseInit (
            Denominacion INT,
            Cantidad INT
        );

        INSERT INTO #DesgloseInit (Denominacion, Cantidad)
        SELECT CAST([key] AS INT), CAST([value] AS INT)
        FROM OPENJSON(@DesgloseJson);

        DECLARE @TotalInit DECIMAL(18,2), @MaxInit DECIMAL(18,2) = 10000.00;
        SELECT @MaxInit = CAST(Valor AS DECIMAL(18,2)) FROM dbo.Parametro WHERE Clave = 'MaximoInicializacion';
        SELECT @TotalInit = ISNULL(SUM(Denominacion * Cantidad), 0) FROM #DesgloseInit;

        IF @TotalInit <= 0
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'El monto de inicialización debe ser mayor a cero.';
            RETURN;
        END

        IF @TotalInit > @MaxInit
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '96';
            SET @Mensaje = CONCAT('El monto de inicialización (Q ', @TotalInit, ') supera el máximo permitido de Q ', @MaxInit);
            RETURN;
        END

        -- Actualizar inventario (reemplaza o carga inicial)
        MERGE dbo.InventarioCajero AS target
        USING #DesgloseInit AS source
        ON (target.IdCajero = @IdCajero AND target.IdDenominacion = source.Denominacion)
        WHEN MATCHED THEN
            UPDATE SET target.Cantidad = source.Cantidad
        WHEN NOT MATCHED THEN
            INSERT (IdCajero, IdDenominacion, Cantidad)
            VALUES (@IdCajero, source.Denominacion, source.Cantidad);

        -- Registrar CargaEfectivo
        INSERT INTO dbo.CargaEfectivo (IdCajero, Tipo, Total, IdAdmin, Fecha)
        VALUES (@IdCajero, 'Inicializacion', @TotalInit, @IdAdmin, GETDATE());
        DECLARE @IdCarga INT = SCOPE_IDENTITY();

        INSERT INTO dbo.CargaEfectivoDetalle (IdCarga, IdDenominacion, Cantidad, Subtotal)
        SELECT @IdCarga, Denominacion, Cantidad, (Denominacion * Cantidad)
        FROM #DesgloseInit
        WHERE Cantidad > 0;

        -- Marcar cajero como inicializado
        UPDATE dbo.Cajero 
        SET Inicializado = 1, 
            FechaUltimaInicializacion = CAST(GETDATE() AS DATE),
            Estado = CASE WHEN Estado = 'Desconectado' THEN Estado ELSE 'En línea' END
        WHERE IdCajero = @IdCajero;

        INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
        VALUES ('Administrador', 'Inicialización de Cajero', 
                CONCAT('Cajero ', @CodigoCajero, ' inicializado con Q ', @TotalInit, ' por Admin ID: ', @IdAdmin), GETDATE());

        COMMIT TRANSACTION;
        SET @CodigoRespuesta = '00';
        SET @Mensaje = CONCAT('Cajero ', @CodigoCajero, ' inicializado con éxito por Q ', @TotalInit);
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @CodigoRespuesta = '96';
        SET @Mensaje = ERROR_MESSAGE();
    END CATCH
END;
GO

-- 10. sp_AgregarEfectivo (Solo si está inicializado, no supera Q 30,000.00)
CREATE OR ALTER PROCEDURE dbo.sp_AgregarEfectivo
    @CodigoCajero VARCHAR(20),
    @IdAdmin INT,
    @DesgloseJson NVARCHAR(MAX),
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @IdCajero INT, @Inicializado BIT;
        SELECT @IdCajero = IdCajero, @Inicializado = Inicializado
        FROM dbo.Cajero WITH (UPDLOCK)
        WHERE Codigo = @CodigoCajero AND Activo = 1;

        IF @IdCajero IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '91';
            SET @Mensaje = 'Cajero no encontrado.';
            RETURN;
        END

        IF @Inicializado = 0
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '91';
            SET @Mensaje = 'No se puede agregar efectivo a un cajero que aún no ha sido inicializado hoy.';
            RETURN;
        END

        CREATE TABLE #DesgloseAdd (
            Denominacion INT,
            Cantidad INT
        );

        INSERT INTO #DesgloseAdd (Denominacion, Cantidad)
        SELECT CAST([key] AS INT), CAST([value] AS INT)
        FROM OPENJSON(@DesgloseJson);

        DECLARE @TotalAdd DECIMAL(18,2), @EfectivoActual DECIMAL(18,2), @CapacidadMaxima DECIMAL(18,2) = 30000.00;
        SELECT @CapacidadMaxima = CAST(Valor AS DECIMAL(18,2)) FROM dbo.Parametro WHERE Clave = 'CapacidadMaximaCajero';
        SELECT @TotalAdd = ISNULL(SUM(Denominacion * Cantidad), 0) FROM #DesgloseAdd;

        SELECT @EfectivoActual = ISNULL(SUM(ic.Cantidad * d.Valor), 0)
        FROM dbo.InventarioCajero ic
        JOIN dbo.Denominacion d ON ic.IdDenominacion = d.IdDenominacion
        WHERE ic.IdCajero = @IdCajero;

        IF @TotalAdd <= 0
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'El monto a agregar debe ser mayor a cero.';
            RETURN;
        END

        IF (@EfectivoActual + @TotalAdd) > @CapacidadMaxima
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '93';
            SET @Mensaje = CONCAT('La recarga superaría la capacidad máxima de Q ', @CapacidadMaxima, '. Total resultante sería Q ', (@EfectivoActual + @TotalAdd));
            RETURN;
        END

        -- Incrementar inventario
        UPDATE ic
        SET ic.Cantidad = ic.Cantidad + da.Cantidad
        FROM dbo.InventarioCajero ic
        JOIN #DesgloseAdd da ON ic.IdDenominacion = da.Denominacion
        WHERE ic.IdCajero = @IdCajero;

        -- Registrar carga
        INSERT INTO dbo.CargaEfectivo (IdCajero, Tipo, Total, IdAdmin, Fecha)
        VALUES (@IdCajero, 'CargaAdicional', @TotalAdd, @IdAdmin, GETDATE());
        DECLARE @IdCarga INT = SCOPE_IDENTITY();

        INSERT INTO dbo.CargaEfectivoDetalle (IdCarga, IdDenominacion, Cantidad, Subtotal)
        SELECT @IdCarga, Denominacion, Cantidad, (Denominacion * Cantidad)
        FROM #DesgloseAdd
        WHERE Cantidad > 0;

        INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
        VALUES ('Administrador', 'Recarga de Efectivo', 
                CONCAT('Recarga de Q ', @TotalAdd, ' agregada a ', @CodigoCajero, ' por Admin ID: ', @IdAdmin), GETDATE());

        COMMIT TRANSACTION;
        SET @CodigoRespuesta = '00';
        SET @Mensaje = CONCAT('Efectivo agregado exitosamente. Total nuevo en cajero: Q ', (@EfectivoActual + @TotalAdd));
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @CodigoRespuesta = '96';
        SET @Mensaje = ERROR_MESSAGE();
    END CATCH
END;
GO

-- 11. sp_CambiarTarjeta (Historial y conserva lo retirado en el día)
CREATE OR ALTER PROCEDURE dbo.sp_CambiarTarjeta
    @IdUsuario INT,
    @NuevaTarjeta VARCHAR(16),
    @PinNuevo VARCHAR(4),
    @IdAdmin INT,
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF LEN(@NuevaTarjeta) <> 16 OR @NuevaTarjeta LIKE '%[^0-9]%'
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'El número de tarjeta debe constar exactamente de 16 dígitos.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.Tarjeta WHERE NumeroTarjeta = @NuevaTarjeta)
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'El número de tarjeta ya existe en el sistema.';
            RETURN;
        END

        -- Dar de baja la tarjeta actual del usuario
        DECLARE @TarjetaAnterior VARCHAR(16);
        SELECT @TarjetaAnterior = NumeroTarjeta FROM dbo.Tarjeta WHERE IdUsuario = @IdUsuario AND Activa = 1;

        UPDATE dbo.Tarjeta 
        SET Activa = 0, FechaBaja = GETDATE()
        WHERE IdUsuario = @IdUsuario AND Activa = 1;

        -- Crear nueva tarjeta con hash y salt
        DECLARE @NuevoSalt VARCHAR(36) = NEWID();
        DECLARE @NuevoHash VARCHAR(64);
        SET @NuevoHash = CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CONCAT(@PinNuevo, @NuevoSalt)), 2);

        INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa, RequiereCambioPin, FechaAsignacion)
        VALUES (@NuevaTarjeta, @NuevoHash, @NuevoSalt, @IdUsuario, 1, 0, GETDATE());

        -- Restablecer intentos fallidos del usuario si estaba bloqueado
        UPDATE dbo.Usuario SET Estado = 'Activo', IntentosFallidos = 0 WHERE IdUsuario = @IdUsuario;

        INSERT INTO dbo.Bitacora (Origen, Accion, Detalle, Fecha)
        VALUES ('Administrador', 'Reemplazo de Tarjeta', 
                CONCAT('Usuario ID: ', @IdUsuario, ' cambió tarjeta anterior (**** ', RIGHT(ISNULL(@TarjetaAnterior, '0000'), 4), 
                       ') por nueva tarjeta (**** ', RIGHT(@NuevaTarjeta, 4), ') conservando historial y límite diario.'), GETDATE());

        COMMIT TRANSACTION;
        SET @CodigoRespuesta = '00';
        SET @Mensaje = 'Tarjeta reemplazada exitosamente. Historial y límite del usuario conservados.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @CodigoRespuesta = '96';
        SET @Mensaje = ERROR_MESSAGE();
    END CATCH
END;
GO

-- 12. sp_CambiarLimite (Con registro en HistorialLimite)
CREATE OR ALTER PROCEDURE dbo.sp_CambiarLimite
    @IdUsuario INT,
    @NuevoLimite DECIMAL(18,2),
    @IdAdmin INT,
    @CodigoRespuesta VARCHAR(10) OUTPUT,
    @Mensaje VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @NuevoLimite <= 0
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'El nuevo límite diario debe ser mayor a cero.';
            RETURN;
        END

        DECLARE @LimiteAnterior DECIMAL(18,2);
        SELECT @LimiteAnterior = LimiteDiario FROM dbo.Usuario WHERE IdUsuario = @IdUsuario AND Activo = 1;

        IF @LimiteAnterior IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SET @CodigoRespuesta = '96';
            SET @Mensaje = 'Usuario no encontrado.';
            RETURN;
        END

        UPDATE dbo.Usuario SET LimiteDiario = @NuevoLimite WHERE IdUsuario = @IdUsuario;

        INSERT INTO dbo.HistorialLimite (IdUsuario, LimiteAnterior, LimiteNuevo, IdAdmin, Fecha)
        VALUES (@IdUsuario, @LimiteAnterior, @NuevoLimite, @IdAdmin, GETDATE());

        COMMIT TRANSACTION;
        SET @CodigoRespuesta = '00';
        SET @Mensaje = CONCAT('Límite diario actualizado de Q ', @LimiteAnterior, ' a Q ', @NuevoLimite);
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @CodigoRespuesta = '96';
        SET @Mensaje = ERROR_MESSAGE();
    END CATCH
END;
GO

-- 13. sp_ConsultaUsuario (Fase I & II)
CREATE OR ALTER PROCEDURE dbo.sp_ConsultaUsuario
    @IdUsuario INT = NULL,
    @NumeroTarjeta VARCHAR(16) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    IF @IdUsuario IS NULL AND @NumeroTarjeta IS NOT NULL
    BEGIN
        SELECT @IdUsuario = IdUsuario FROM dbo.Tarjeta WHERE NumeroTarjeta = @NumeroTarjeta;
    END

    SELECT 
        u.IdUsuario,
        u.Nombre,
        u.Saldo,
        u.LimiteDiario,
        u.Estado,
        t.NumeroTarjeta,
        ISNULL(SUM(CASE WHEN tr.Tipo = 'RETIRO' AND tr.CodigoRespuesta = '00' 
                             AND CAST(tr.Fecha AS DATE) = CAST(GETDATE() AS DATE) 
                        THEN tr.Monto ELSE 0 END), 0) AS RetiradoHoy,
        (u.LimiteDiario - ISNULL(SUM(CASE WHEN tr.Tipo = 'RETIRO' AND tr.CodigoRespuesta = '00' 
                                              AND CAST(tr.Fecha AS DATE) = CAST(GETDATE() AS DATE) 
                                         THEN tr.Monto ELSE 0 END), 0)) AS DisponibleHoy,
        (SELECT TOP 1 CONCAT(s.FechaInicio, ' en ', c.Codigo) 
         FROM dbo.Sesion s 
         JOIN dbo.Cajero c ON s.IdCajero = c.IdCajero 
         WHERE s.IdUsuario = u.IdUsuario 
         ORDER BY s.FechaInicio DESC) AS UltimoAcceso
    FROM dbo.Usuario u
    LEFT JOIN dbo.Tarjeta t ON u.IdUsuario = t.IdUsuario AND t.Activa = 1
    LEFT JOIN dbo.Transaccion tr ON u.IdUsuario = tr.IdUsuario
    WHERE u.IdUsuario = @IdUsuario AND u.Activo = 1
    GROUP BY u.IdUsuario, u.Nombre, u.Saldo, u.LimiteDiario, u.Estado, t.NumeroTarjeta;
END;
GO

-- 14. sp_UltimasTransacciones (Últimas N operaciones del usuario en cualquier cajero)
CREATE OR ALTER PROCEDURE dbo.sp_UltimasTransacciones
    @NumeroTarjeta VARCHAR(16),
    @TopN INT = 5
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @IdUsuario INT;
    SELECT @IdUsuario = IdUsuario FROM dbo.Tarjeta WHERE NumeroTarjeta = @NumeroTarjeta;

    SELECT TOP (@TopN)
        t.IdTransaccion,
        t.Fecha,
        c.Codigo AS Cajero,
        t.Tipo,
        t.Monto,
        t.CodigoRespuesta,
        t.MotivoRechazo
    FROM dbo.Transaccion t
    JOIN dbo.Cajero c ON t.IdCajero = c.IdCajero
    WHERE t.IdUsuario = @IdUsuario
    ORDER BY t.Fecha DESC;
END;
GO

-- ============================================================================
-- 5. SEGURIDAD: USUARIO DE SQL SERVER PARA LA APLICACIÓN
-- ============================================================================
IF NOT EXISTS (SELECT name FROM sys.server_principals WHERE name = 'usr_cajero')
BEGIN
    CREATE LOGIN usr_cajero WITH PASSWORD = N'CajeroSecure2026!', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;
END
GO

IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = 'usr_cajero')
BEGIN
    CREATE USER usr_cajero FOR LOGIN usr_cajero;
END
GO

-- Otorgar permisos mínimos requeridos (EXECUTE en SPs, SELECT en Vistas)
GRANT EXECUTE TO usr_cajero;
GRANT SELECT ON SCHEMA::dbo TO usr_cajero;
GO

-- ============================================================================
-- 6. DATOS DE PRUEBA (SEED DATA)
-- ============================================================================

-- Denominaciones
MERGE dbo.Denominacion AS target
USING (VALUES (200), (100), (50), (20), (10), (5), (1)) AS source(Valor)
ON target.IdDenominacion = source.Valor
WHEN NOT MATCHED THEN
    INSERT (IdDenominacion, Valor) VALUES (source.Valor, source.Valor);
GO

-- Parámetros iniciales
MERGE dbo.Parametro AS target
USING (VALUES 
    ('PuertoServidor', '5000', 'Puerto TCP donde escucha el servidor'),
    ('UmbralEfectivoBajo', '1000.00', 'Monto en Q para disparar alerta de efectivo bajo'),
    ('VigenciaTokenSegundos', '120', 'Duración en segundos del token temporal (2 minutos)'),
    ('MaximoInicializacion', '10000.00', 'Límite máximo de inicialización diaria por cajero'),
    ('CapacidadMaximaCajero', '30000.00', 'Capacidad total de efectivo permitida en cada cajero')
) AS source(Clave, Valor, Descripcion)
ON target.Clave = source.Clave
WHEN NOT MATCHED THEN
    INSERT (Clave, Valor, Descripcion) VALUES (source.Clave, source.Valor, source.Descripcion);
GO

-- Administradores
-- Contraseña 'Admin123!' con salt 'SALTADMIN1'
-- Contraseña 'Super123!' con salt 'SALTSUPER1'
IF NOT EXISTS (SELECT 1 FROM dbo.Administrador WHERE Usuario = 'admin')
BEGIN
    INSERT INTO dbo.Administrador (Usuario, PasswordHash, PasswordSalt, Rol, Estado)
    VALUES (
        'admin', 
        CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', 'Admin123!SALTADMIN1'), 2), 
        'SALTADMIN1', 
        'Administrador', 
        'Activo'
    );
END

IF NOT EXISTS (SELECT 1 FROM dbo.Administrador WHERE Usuario = 'supervisor')
BEGIN
    INSERT INTO dbo.Administrador (Usuario, PasswordHash, PasswordSalt, Rol, Estado)
    VALUES (
        'supervisor', 
        CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', 'Super123!SALTSUPER1'), 2), 
        'SALTSUPER1', 
        'Supervisor', 
        'Activo'
    );
END
GO

-- 5 Cajeros Oficiales (ATM-01 a ATM-05)
MERGE dbo.Cajero AS target
USING (VALUES
    ('ATM-01', 'Cajero Central Zona 1', 'Edificio Central Nivel 1', '127.0.0.1'),
    ('ATM-02', 'Cajero Sucursal Pradera', 'C.C. La Pradera Local 102', '127.0.0.1'),
    ('ATM-03', 'Cajero Campus Universitario', 'Plaza de Estudiantes', '127.0.0.1'),
    ('ATM-04', 'Cajero Miraflores', 'C.C. Miraflores Nivel 2', '127.0.0.1'),
    ('ATM-05', 'Cajero Roosevelt', 'Calzada Roosevelt 22-01', '127.0.0.1')
) AS source(Codigo, Nombre, Ubicacion, IP)
ON target.Codigo = source.Codigo
WHEN NOT MATCHED THEN
    INSERT (Codigo, Nombre, Ubicacion, IP, Estado, Inicializado, FechaUltimaInicializacion)
    VALUES (source.Codigo, source.Nombre, source.Ubicacion, source.IP, 'Desconectado', 1, CAST(GETDATE() AS DATE));
GO

-- Cargar inventario inicial para cada cajero (Total Q 8,450.00 por cajero)
-- 200x20 = 4000, 100x25 = 2500, 50x20 = 1000, 20x25 = 500, 10x30 = 300, 5x20 = 100, 1x50 = 50
INSERT INTO dbo.InventarioCajero (IdCajero, IdDenominacion, Cantidad)
SELECT c.IdCajero, d.IdDenominacion, 
    CASE d.Valor
        WHEN 200 THEN 20
        WHEN 100 THEN 25
        WHEN 50  THEN 20
        WHEN 20  THEN 25
        WHEN 10  THEN 30
        WHEN 5   THEN 20
        WHEN 1   THEN 50
    END
FROM dbo.Cajero c
CROSS JOIN dbo.Denominacion d
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.InventarioCajero ic 
    WHERE ic.IdCajero = c.IdCajero AND ic.IdDenominacion = d.IdDenominacion
);
GO

-- 8 Usuarios con sus respectivas Tarjetas (16 dígitos) y PINs (4 dígitos)
-- Claves de prueba: PINs hashed con SHA-256 + salt
IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE Nombre = 'Carlos Alberto Méndez')
BEGIN
    INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos)
    VALUES ('Carlos Alberto Méndez', 5000.00, 2000.00, 'Activo', 0);
    DECLARE @u1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa)
    VALUES ('1111222233334444', CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', '1234SALT01'), 2), 'SALT01', @u1, 1);
    -- Mapeo de UID de hardware RFID físico (ESP32)
    INSERT INTO dbo.MapeoRfid (UidRfid, NumeroTarjeta, Descripcion)
    VALUES ('99A90BA4', '1111222233334444', 'Llavero RFID Azul de Carlos Méndez');
END

IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE Nombre = 'Ana Lucía López')
BEGIN
    INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos)
    VALUES ('Ana Lucía López', 8500.00, 1500.00, 'Activo', 0);
    DECLARE @u2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa)
    VALUES ('2222333344445555', CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', '4321SALT02'), 2), 'SALT02', @u2, 1);
    -- Mapeo de tarjeta blanca RFID física
    INSERT INTO dbo.MapeoRfid (UidRfid, NumeroTarjeta, Descripcion)
    VALUES ('A77B2F63', '2222333344445555', 'Tarjeta Blanca RFID de Ana López');
END

IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE Nombre = 'Juan Fernando Pérez')
BEGIN
    INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos)
    VALUES ('Juan Fernando Pérez', 3200.00, 1000.00, 'Activo', 0);
    DECLARE @u3 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa)
    VALUES ('3333444455556666', CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', '1111SALT03'), 2), 'SALT03', @u3, 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE Nombre = 'María José Gómez')
BEGIN
    INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos)
    VALUES ('María José Gómez', 12000.00, 3000.00, 'Activo', 0);
    DECLARE @u4 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa)
    VALUES ('4444555566667777', CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', '2222SALT04'), 2), 'SALT04', @u4, 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE Nombre = 'Luis Rodolfo Morales')
BEGIN
    INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos)
    VALUES ('Luis Rodolfo Morales', 500.00, 500.00, 'Activo', 0);
    DECLARE @u5 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa)
    VALUES ('5555666677778888', CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', '3333SALT05'), 2), 'SALT05', @u5, 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE Nombre = 'Sofía Alejandra Castillo')
BEGIN
    INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos)
    VALUES ('Sofía Alejandra Castillo', 15000.00, 2500.00, 'Activo', 0);
    DECLARE @u6 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa)
    VALUES ('6666777788889999', CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', '4444SALT06'), 2), 'SALT06', @u6, 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE Nombre = 'Diego Alejandro Ruiz')
BEGIN
    INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos)
    VALUES ('Diego Alejandro Ruiz', 750.00, 800.00, 'Activo', 0);
    DECLARE @u7 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa)
    VALUES ('7777888899990000', CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', '5555SALT07'), 2), 'SALT07', @u7, 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE Nombre = 'Elena Beatriz Morales')
BEGIN
    INSERT INTO dbo.Usuario (Nombre, Saldo, LimiteDiario, Estado, IntentosFallidos)
    VALUES ('Elena Beatriz Morales', 6000.00, 1200.00, 'Activo', 0);
    DECLARE @u8 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Tarjeta (NumeroTarjeta, PinHash, PinSalt, IdUsuario, Activa)
    VALUES ('8888999900001111', CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', '6666SALT08'), 2), 'SALT08', @u8, 1);
END
GO

PRINT 'Base de datos BD_CajeroRed configurada exitosamente.';
GO
