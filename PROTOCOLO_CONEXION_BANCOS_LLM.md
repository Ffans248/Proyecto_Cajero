# PROMPT & ESPECIFICACIÓN TÉCNICA PARA AGENTES DE CÓDIGO (Claude / GPT / Antigravity / Cursor)
## Protocolo de Descubrimiento en Red Local (LAN/Wi-Fi) y Transferencias Interbancarias

> **INSTRUCCIÓN PARA EL USUARIO DE OTRO GRUPO:**
> Copia y pega todo este documento directamente en tu agente de código (Claude, ChatGPT, Cursor, Antigravity, Copilot, etc.) con el mensaje:
> *"Implementa este protocolo de descubrimiento y transferencias interbancarias en mi proyecto de cajero/banco para que mi banco sea visible automáticamente en la red y pueda hacer y recibir transferencias con los demás bancos."*

---

## 1. CONTEXTO ARQUITECTÓNICO PARA EL AGENTE LLM

Estamos interconectando múltiples proyectos de **Bancos / Cajeros Automáticos** de diferentes grupos estudiantiles dentro de la **misma red local (Wi-Fi / Router LAN o Radmin VPN)**.

Para que **todos los bancos se detecten automáticamente** y puedan operar juntos sin importar en qué lenguaje o framework esté hecho cada banco (C# .NET WPF/WinForms, Java, Python, Node.js, etc.), todos los nodos bancarios utilizan:
1. **Puerto UDP `5005` (Broadcast Discovery)**: Para autodescubrimiento inmediato en la subred local.
2. **Puerto TCP `5000` (Protocolo JSON por línea `\n`)**: Para:
   - `IB_PING` (Handshake de presencia y cupo disponible del banco).
   - `IB_VERIFICAR_CUENTA` (Verificar si el número de cuenta/tarjeta existe en el banco destino antes de transferir).
   - `IB_TRANSFERIR` (Ejecutar la transferencia interbancaria, guardar el `TokenTransferencia` compartido y restar el monto del límite diario de ambos bancos coordinadamente).

---

## 2. PARÁMETROS DE RED ACTUALES (PUNTO DE ENCUENTRO)

| Parámetro | Valor Estándar |
| :--- | :--- |
| **Red Wi-Fi Actual (SSID)** | `uwu` (Subred `10.219.19.0/24`, Gateway `10.219.19.209`) *(o la red LAN/Wi-Fi del salón o Radmin VPN `26.x.x.x`)* |
| **Nodo Banco `BANC01` (NEXUSBANK)** | IP actual en Wi-Fi `uwu`: **`10.219.19.9`** \| IP en Radmin VPN: **`26.121.70.247`** |
| **Puerto TCP Interbancario** | **`5000`** (`0.0.0.0:5000` - JSON terminado en salto de línea `\n`, codificación `UTF-8`) |
| **Puerto UDP Autodescubrimiento** | **`5005`** (`0.0.0.0:5005` - Texto plano separado por `|`, codificación `UTF-8`) |
| **Códigos de Banco sugeridos** | `BANC01` (NEXUSBANK), `BANC02`, `BANC03`, `BANC04`, etc. (Asignar un código único a tu grupo) |
| **Límite Diario por Banco** | `25000.00` (Se resta coordinadamente en el banco origen y en el banco destino en cada transferencia) |

> **IMPORTANTE (Firewall de Windows):**
> El agente debe asegurarse de que el servidor escuche en `IPAddress.Any` (`0.0.0.0`) y no solo en `127.0.0.1`, y abrir o permitir los puertos **TCP 5000** y **UDP 5005** en el Firewall de Windows:
> ```powershell
> netsh advfirewall firewall add rule name="Banco Interbancario TCP 5000" dir=in action=allow protocol=TCP localport=5000
> netsh advfirewall firewall add rule name="Banco Discovery UDP 5005" dir=in action=allow protocol=UDP localport=5005
> ```

---

## 3. MECANISMO 1: AUTODESCUBRIMIENTO POR UDP BROADCAST (PUERTO `5005`)

Para que un banco sea descubierto instantáneamente al presionar **"Escanear Red Local"**:

### 3.1 Qué se recibe por UDP en el puerto `5005`
Cualquier banco que busque otros bancos en la LAN envía un paquete UDP Broadcast a `255.255.255.255:5005` (y al broadcast de la subred, ej. `10.219.19.255:5005`) con este formato exacto en texto UTF-8:

```text
DISCOVER_BANCO_ATM|<CodigoBancoQueBusca>|<NombreBancoQueBusca>|<IpLocal>|<PuertoTcp>
```
*Ejemplo recibido:*
```text
DISCOVER_BANCO_ATM|BANC01|NEXUSBANK|10.219.19.9|5000
```

### 3.2 Qué debe responder tu banco por UDP al `RemoteEndPoint` remitente
Al recibir una cadena que comience con `DISCOVER_BANCO_ATM|`, tu banco debe responder inmediatamente por UDP al `RemoteEndPoint` que envió el paquete con el siguiente formato separado por `|`:

```text
HERE_BANCO_ATM|<TuCodigoBanco>|<TuNombreBanco>|<TuIpLocal>|<TuPuertoTcp>|<TuLimiteDiario>|<TuTransferidoHoy>
```
*Ejemplo de respuesta que debes enviar:*
```text
HERE_BANCO_ATM|BANC02|Banco Industrial Estudiantil|10.219.19.25|5000|25000.00|0.00
```

---

## 4. MECANISMO 2: PROTOCOLO TCP JSON POR LÍNEA (PUERTO `5000`)

Además de UDP `5005`, el escáner de red barre la subred local en el puerto **TCP `5000`** y envía peticiones JSON de **una sola línea terminada en `\n`** (`UTF-8`). Cada conexión TCP envía 1 línea JSON de solicitud y recibe 1 línea JSON de respuesta terminada en `\n`.

Tu servidor TCP en `0.0.0.0:5000` debe inspeccionar el campo `"tipo"` (case-insensitive) del JSON recibido y soportar estas **3 operaciones**:

---

### OPERACIÓN A: `IB_PING` (Detección de Banco y Consulta de Cupo)

Se usa cuando otro banco escanea la red por TCP `5000` o hace clic en **"Ping Banco"**.

#### JSON que recibe tu banco:
```json
{"tipo":"IB_PING","cajero":"SERVER","idMensaje":"A1B2C3D4","timestamp":"2026-10-29 11:00:00","bancoOrigen":"BANC01","nombreBancoOrigen":"NEXUSBANK"}
```

#### JSON que debe responder tu banco (terminado en `\n`):
```json
{"tipo":"RESP_OK","idMensaje":"A1B2C3D4","codigo":"00","mensaje":"Banco BANC02 en línea","timestamp":"2026-10-29 11:00:00","datos":{"codigoBanco":"BANC02","nombreBanco":"Banco Industrial Estudiantil","cupoDisponibleHoy":25000.00,"estado":"EN_LINEA"}}
```
> Con solo responder este JSON en el puerto TCP `5000`, tu banco aparecerá automáticamente como **`🟢 DETECTADO EN RED`** con tu IP, nombre y cupo disponible en todos los demás bancos.

---

### OPERACIÓN B: `IB_VERIFICAR_CUENTA` (Verificar si Existe el Usuario antes de Transferir)

Requisito funcional: *"Al momento de hacer transferencia interbancaria ingresar número de cuenta y verificar si existe el usuario del banco seleccionado"*.

#### JSON que recibe tu banco:
```json
{"tipo":"IB_VERIFICAR_CUENTA","cajero":"SERVER","idMensaje":"B2C3D4E5","timestamp":"2026-10-29 11:02:00","bancoOrigen":"BANC01","bancoDestino":"BANC02","numeroCuenta":"2002000001"}
```
*(Nota: el campo de la cuenta puede llegar como `"numeroCuenta"` o `"cuentaDestino"` — acepta cualquiera de los dos, y busca por número de cuenta o número de tarjeta en tu base de datos/CSV).*

#### JSON que debe responder tu banco si la cuenta SÍ existe (`codigo: "00"`):
```json
{"tipo":"RESP_OK","idMensaje":"B2C3D4E5","codigo":"00","mensaje":"Cuenta verificada en el banco destino.","timestamp":"2026-10-29 11:02:00","datos":{"existe":true,"codigoBanco":"BANC02","nombreBanco":"Banco Industrial Estudiantil","numeroCuenta":"2002000001","nombreTitular":"Roberto Gómez","cupoDisponibleBanco":25000.00}}
```

#### JSON que debe responder tu banco si la cuenta NO existe (`codigo: "14"`):
```json
{"tipo":"RESP_OK","idMensaje":"B2C3D4E5","codigo":"14","mensaje":"La cuenta no existe en este banco.","timestamp":"2026-10-29 11:02:00","datos":{"existe":false,"codigoBanco":"BANC02","nombreBanco":"Banco Industrial Estudiantil","numeroCuenta":"2002000001","nombreTitular":"","cupoDisponibleBanco":25000.00}}
```

---

### OPERACIÓN C: `IB_TRANSFERIR` (Transferencia con Token Compartido y Descuento Coordinado de Límites)

Requisito funcional:
1. *"Generar token para registro de transferencias al momento de realizar la transferencia, tanto como nuestro banco, el otro y el usuario tienen que guardar el token"*.
2. *"Los montos se restan al límite de los dos bancos coordinándose"*.

#### Flujo al enviar una transferencia (`Banco Origen -> Banco Destino`):
1. El **Banco Origen** genera el token con formato `TRF-YYYYMMDD-XXXXXX` (ej. `TRF-20261029-482910`).
2. Verifica que el usuario origen tenga saldo suficiente y que tanto el **Banco Origen** como el **Banco Destino** tengan cupo diario disponible suficiente (`DisponibleHoy >= monto`).
3. Envía `IB_TRANSFERIR` por TCP `5000` al **Banco Destino**.

#### JSON que recibe el Banco Destino:
```json
{"tipo":"IB_TRANSFERIR","cajero":"SERVER","idMensaje":"C3D4E5F6","timestamp":"2026-10-29 11:05:00","tokenTransferencia":"TRF-20261029-482910","bancoOrigen":"BANC01","cuentaOrigen":"1234567890123456","nombreOrigen":"Juan Perez","bancoDestino":"BANC02","cuentaDestino":"2002000001","nombreDestino":"Roberto Gómez","monto":500.00,"concepto":"Pago interbancario"}
```

#### Qué debe hacer el Banco Destino al recibir `IB_TRANSFERIR`:
1. Buscar al usuario local dueño de `cuentaDestino`. Si no existe, responder con `codigo: "14"`.
2. Verificar que el monto no exceda el cupo diario disponible del banco (`LimiteDiario - TransferidoHoy >= monto`). Si excede, responder con `codigo: "65"`.
3. **Sumar `monto` al saldo** del usuario destino.
4. **Restar `monto` del límite diario disponible de ambos bancos** en su tabla local de bancos (`TransferidoHoy += monto` tanto para el propio banco como para el registro de `bancoOrigen`).
5. **Guardar el `tokenTransferencia`** recibido en:
   - El registro de transferencias del banco (`TransferenciaBancaria` / CSV de transferencias).
   - El historial de transacciones del usuario destino (`Transaccion` / CSV de transacciones).
6. Responder con `codigo: "00"`:

#### JSON que responde el Banco Destino (`codigo: "00"`):
```json
{"tipo":"RESP_OK","idMensaje":"C3D4E5F6","codigo":"00","mensaje":"Transferencia interbancaria acreditada y token registrado.","timestamp":"2026-10-29 11:05:00","datos":{"tokenTransferencia":"TRF-20261029-482910","bancoDestino":"BANC02","cuentaDestino":"2002000001","nombreDestino":"Roberto Gómez","monto":500.00,"cupoRestanteBancoDestino":24500.00}}
```

---

## 5. ESQUEMA DE BASE DE DATOS / ARCHIVOS RECOMENDADO PARA TU AGENTE

Si tu proyecto usa **SQL Server**, crea o asegúrate de tener estas columnas/tablas compatibles:

```sql
-- 1. En tu tabla de Usuarios: asegurarte de tener NumeroCuenta (o usar NumeroTarjeta)
ALTER TABLE dbo.Usuario ADD NumeroCuenta VARCHAR(20) NULL;

-- 2. En tu tabla de Transacciones del usuario: guardar el TokenTransferencia
ALTER TABLE dbo.Transaccion ADD TokenTransferencia VARCHAR(40) NULL;

-- 3. Tabla de Bancos de la Red y Límites Coordinados
CREATE TABLE dbo.BancoRed (
    IdBanco             INT IDENTITY(1,1) PRIMARY KEY,
    CodigoBanco         VARCHAR(15) NOT NULL UNIQUE,      -- 'BANC01', 'BANC02', etc.
    NombreBanco         NVARCHAR(100) NOT NULL,
    IP                  VARCHAR(50) NOT NULL DEFAULT '127.0.0.1',
    Puerto              INT NOT NULL DEFAULT 5000,
    EsPropio            BIT NOT NULL DEFAULT 0,           -- 1 = Tu banco, 0 = Otro banco
    LimiteDiario        DECIMAL(18,2) NOT NULL DEFAULT 25000.00,
    TransferidoHoy      DECIMAL(18,2) NOT NULL DEFAULT 0.00,
    FechaUltimoReinicio DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    Activo              BIT NOT NULL DEFAULT 1
);

-- 4. Tabla de Registro de Transferencias y Tokens (Guardado por ambos bancos)
CREATE TABLE dbo.TransferenciaBancaria (
    IdTransferencia          INT IDENTITY(1,1) PRIMARY KEY,
    TokenTransferencia       VARCHAR(40) NOT NULL,        -- Ej: 'TRF-20261029-482910'
    TipoTransferencia        VARCHAR(20) NOT NULL,        -- 'INTERNA' | 'INTERBANCARIA'
    BancoOrigen              VARCHAR(15) NOT NULL,
    CuentaOrigen             VARCHAR(20) NOT NULL,
    NombreOrigen             NVARCHAR(100) NOT NULL,
    BancoDestino             VARCHAR(15) NOT NULL,
    CuentaDestino            VARCHAR(20) NOT NULL,
    NombreDestino            NVARCHAR(100) NOT NULL,
    Monto                    DECIMAL(18,2) NOT NULL,
    Concepto                 NVARCHAR(200) NULL,
    CupoRestanteBancoOrigen  DECIMAL(18,2) NULL,
    CupoRestanteBancoDestino DECIMAL(18,2) NULL,
    Estado                   VARCHAR(20) NOT NULL DEFAULT 'APROBADA',
    FechaCreacion            DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
```

---

## 6. CHECKLIST RÁPIDO DE VERIFICACIÓN ENTRE GRUPOS

1. Conectarse todos a la misma red Wi-Fi (actualmente **`uwu`**, subred `10.219.19.0/24`) o a la misma red de **Radmin VPN**.
2. Asignar un `CodigoBanco` distinto a cada grupo:
   - Grupo 1 (Diego): `BANC01` - `NEXUSBANK` (`10.219.19.9:5000`)
   - Grupo 2: `BANC02`
   - Grupo 3: `BANC03`
   - Grupo 4: `BANC04`
3. Iniciar la aplicación de cada banco (que levanta `UDP 5005` y `TCP 5000`).
4. Presionar **"📡 Escanear Red Local"**: los demás bancos aparecerán automáticamente como **`🟢 DETECTADO EN RED (<IP>:5000)`**.
5. Ingresar una cuenta del otro banco, presionar **"Verificar si Existe Usuario"**, y confirmar la transferencia verificando que el mismo `TokenTransferencia` (`TRF-YYYYMMDD-XXXXXX`) quede registrado en **ambos bancos** y en el **usuario**, y que el monto se haya descontado del límite diario de **ambos bancos**.

