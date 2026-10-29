# Estándar de Red Interbancaria (SQL + Protocolo TCP/JSON)

Este documento define el **formato común de tablas SQL** y el **protocolo de comunicación TCP/JSON** para conectar los servidores bancarios de los diferentes grupos a través del router.

---

## 1. Tablas Comunes en SQL Server

Cada banco debe incluir estas tablas en su base de datos (ya incluidas automáticamente en `Database/Script_BD_CajeroRed.sql` y migradas automáticamente al iniciar `Cajero.Servidor`):

### 1.1 Número de Cuenta en `dbo.Usuario`
Cada usuario tiene un `NumeroCuenta VARCHAR(20) UNIQUE` (por ejemplo: `1001000001` para `BANC01`, `2002000001` para `BANC02`).

### 1.2 Tabla `dbo.BancoRed` (Catálogo de Bancos y Cupo Diario Coordinado)
```sql
CREATE TABLE dbo.BancoRed (
    IdBanco             INT IDENTITY(1,1) PRIMARY KEY,
    CodigoBanco          VARCHAR(15) NOT NULL UNIQUE,      -- Ej: 'BANC01', 'BANC02', 'BANC03'
    NombreBanco         NVARCHAR(100) NOT NULL,           -- Nombre del banco del grupo
    IP                  VARCHAR(50) NOT NULL DEFAULT '127.0.0.1', -- IP del servidor en el router
    Puerto              INT NOT NULL DEFAULT 5000,        -- Puerto TCP del servidor bancario
    EsPropio            BIT NOT NULL DEFAULT 0,           -- 1 = Nuestro banco local, 0 = Banco remoto
    LimiteDiario        DECIMAL(18,2) NOT NULL DEFAULT 25000.00, -- Cupo diario interbancario del banco
    TransferidoHoy      DECIMAL(18,2) NOT NULL DEFAULT 0.00,     -- Monto restado hoy al cupo del banco
    FechaUltimoReinicio DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    Activo              BIT NOT NULL DEFAULT 1,
    CadenaConexionBD    NVARCHAR(400) NULL
);
```

### 1.3 Tabla `dbo.TransferenciaBancaria` (Tabla Común de Registro de Tokens)
Tanto **nuestro banco** como **el otro banco** guardan el mismo `TokenTransferencia` en esta tabla común, mientras que el **usuario** lo recibe en su comprobante y en su historial (`dbo.Transaccion.TokenTransferencia`):

```sql
CREATE TABLE dbo.TransferenciaBancaria (
    IdTransferencia          INT IDENTITY(1,1) PRIMARY KEY,
    TokenTransferencia       VARCHAR(40) NOT NULL UNIQUE,  -- Ej: 'TRF-20261029-482910'
    TipoTransferencia        VARCHAR(20) NOT NULL,         -- 'INTERNA' | 'INTERBANCARIA'
    Direccion                VARCHAR(15) NOT NULL,         -- 'LOCAL' | 'SALIENTE' | 'ENTRANTE'
    BancoOrigen              VARCHAR(15) NOT NULL,         -- Ej: 'BANC01'
    NombreBancoOrigen        NVARCHAR(100) NULL,
    CuentaOrigen             VARCHAR(20) NOT NULL,         -- Cuenta del remitente
    NombreOrigen             NVARCHAR(100) NOT NULL,
    BancoDestino             VARCHAR(15) NOT NULL,         -- Ej: 'BANC02'
    NombreBancoDestino       NVARCHAR(100) NULL,
    CuentaDestino            VARCHAR(20) NOT NULL,         -- Cuenta del beneficiario
    NombreDestino            NVARCHAR(100) NOT NULL,
    Monto                    DECIMAL(18,2) NOT NULL,
    Concepto                 NVARCHAR(200) NULL,
    Estado                   VARCHAR(20) NOT NULL,         -- 'APROBADA' | 'RECHAZADA' | 'REVERTIDA'
    CodigoRespuesta          VARCHAR(10) NOT NULL DEFAULT '00',
    MensajeRespuesta         NVARCHAR(255) NULL,
    CupoRestanteBancoOrigen  DECIMAL(18,2) NULL,           -- Cupo restante del banco origen tras restar el monto
    CupoRestanteBancoDestino DECIMAL(18,2) NULL,           -- Cupo restante del banco destino tras restar el monto
    FechaCreacion            DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    FechaConfirmacion        DATETIME2 NULL
);
```

### 1.4 Procedimiento Común `dbo.sp_VerificarCuentaLocal`
Permite verificar si una cuenta existe en el banco antes de transferir:
```sql
EXEC dbo.sp_VerificarCuentaLocal @NumeroCuenta = '1001000001';
```

---

## 2. Protocolo TCP/JSON entre Bancos (Puerto 5000)

Los servidores se comunican enviando una línea JSON terminada en `\n` (`UTF-8`) al `IP:Puerto` registrado en `dbo.BancoRed`.

### 2.1 Verificar Cuenta antes de Transferir (`IB_VERIFICAR_CUENTA`)

**Solicitud (Banco Origen -> Banco Destino):**
```json
{
  "tipo": "IB_VERIFICAR_CUENTA",
  "cajero": "SERVER",
  "idMensaje": "a1b2c3d4",
  "timestamp": "2026-10-29T10:00:00",
  "bancoOrigen": "BANC01",
  "bancoDestino": "BANC02",
  "cuentaDestino": "2002000001",
  "montoConsulta": 500.00
}
```

**Respuesta (Banco Destino -> Banco Origen):**
```json
{
  "tipo": "IB_VERIFICAR_CUENTA_RESP",
  "idMensaje": "a1b2c3d4",
  "exito": true,
  "codigo": "00",
  "mensaje": "Cuenta verificada en el banco destino.",
  "timestamp": "2026-10-29T10:00:00",
  "datos": {
    "existe": true,
    "cuentaActiva": true,
    "codigoBanco": "BANC02",
    "nombreBanco": "Banco Industrial Estudiantil",
    "numeroCuenta": "2002000001",
    "nombreTitular": "Roberto Gómez",
    "bancoTieneCupo": true,
    "cupoDisponibleBanco": 25000.00
  }
}
```

### 2.2 Ejecutar Transferencia Coordinada con Token (`IB_TRANSFERIR`)

1. El **Banco Origen** genera el token único `TokenTransferencia` (formato `TRF-YYYYMMDD-XXXXXX`), verifica saldo del usuario origen y verifica que el cupo diario de ambos bancos alcance.
2. El **Banco Origen** envía `IB_TRANSFERIR` al **Banco Destino** con el `TokenTransferencia`.
3. El **Banco Destino** acredita el monto al usuario destino, **resta el monto de su propio límite diario y del registro del banco origen**, guarda el `TokenTransferencia` en `dbo.TransferenciaBancaria` y en `dbo.Transaccion` del usuario destino, y responde con `codigo = "00"`.
4. El **Banco Origen** debita el monto del usuario origen, **resta el monto de su límite diario y del límite del banco destino**, guarda el `TokenTransferencia` en `dbo.TransferenciaBancaria` y en `dbo.Transaccion` del usuario origen, y entrega el comprobante con el token al usuario en el cajero.

**Solicitud (Banco Origen -> Banco Destino):**
```json
{
  "tipo": "IB_TRANSFERIR",
  "cajero": "SERVER",
  "idMensaje": "e5f6g7h8",
  "timestamp": "2026-10-29T10:01:00",
  "tokenTransferencia": "TRF-20261029-482910",
  "bancoOrigen": "BANC01",
  "nombreBancoOrigen": "Banco Central Universitario (Nuestro Banco)",
  "cuentaOrigen": "1001000001",
  "nombreOrigen": "Juan Pérez",
  "bancoDestino": "BANC02",
  "cuentaDestino": "2002000001",
  "monto": 500.00,
  "concepto": "Pago interbancario"
}
```

**Respuesta (Banco Destino -> Banco Origen):**
```json
{
  "tipo": "IB_TRANSFERIR_RESP",
  "idMensaje": "e5f6g7h8",
  "exito": true,
  "codigo": "00",
  "mensaje": "Transferencia interbancaria recibida y acreditada.",
  "timestamp": "2026-10-29T10:01:00",
  "datos": {
    "aprobada": true,
    "tokenTransferencia": "TRF-20261029-482910",
    "bancoDestino": "BANC02",
    "nombreBancoDestino": "Banco Industrial Estudiantil",
    "cuentaDestino": "2002000001",
    "nombreDestinatario": "Roberto Gómez",
    "montoRecibido": 500.00,
    "cupoRestanteBancoOrigen": 24500.00,
    "cupoRestanteBancoDestino": 24500.00,
    "fechaConfirmacion": "2026-10-29 10:01:00"
  }
}
```

