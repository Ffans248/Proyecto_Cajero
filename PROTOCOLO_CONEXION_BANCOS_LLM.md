# PROMPT & ESPECIFICACIÓN TÉCNICA PARA AGENTES DE CÓDIGO (Claude / GPT / Antigravity / Cursor)
## Conexión de Cajeros (ATMs) y Bancos a la Base de Datos Maestra Central en Red Local

> **INSTRUCCIÓN PARA EL ESTUDIANTE DE OTRO CAJERO O BANCO:**
> Copia y pega todo este documento directamente en tu agente de código (Claude, ChatGPT, Cursor, Antigravity, Copilot, etc.) con el mensaje:
> *"Implementa en mi proyecto este protocolo de conexión a la Base de Datos Maestra alojada en `10.219.19.9` para que mi Cajero/Banco sea detectado automáticamente en la red y consulte/actualice todos los usuarios, saldos, límites de bancos y tokens de transferencia en la BD Maestra."*

---

## 1. ARQUITECTURA CENTRALIZADA (BASE DE DATOS MAESTRA EN ESTE EQUIPO)

Todos los **Cajeros Automáticos (ATMs)** y **Bancos** de la red consultan a **una única Base de Datos Maestra** alojada en el equipo principal:

| Parámetro del Host Maestro | Valor Exacto |
| :--- | :--- |
| **Red Wi-Fi (SSID)** | **`uwu`** (Subred `10.219.19.0/24`, Gateway `10.219.19.209`) |
| **IP del Servidor de BD Maestra (Wi-Fi)** | **`10.219.19.9`** *(En Radmin VPN: **`26.121.70.247`**)* |
| **Puerto TCP del Gateway de BD Maestra** | **`5000`** (`10.219.19.9:5000` - JSON de una línea terminada en `\n`, `UTF-8`) |
| **Puerto UDP de Autodescubrimiento (Cajeros y Bancos)** | **`5005`** (`10.219.19.9:5005` - Broadcast UDP `UTF-8`) |
| **Motor SQL Server Maestro en el Host** | Instancia: `10.219.19.9\SQLEXPRESS` \| Base de Datos: **`BD_CajeroRed`** \| Login SQL: `cajero_red` / Password: `Cajero2026!` |

> **VENTAJA PARA TU AGENTE LLM:**
> Tu cajero o banco puede consultar la Base de Datos Maestra de **dos maneras** (recomendamos implementar el **Gateway TCP 5000** porque no requiere instalar drivers de SQL Server ni configurar dominios de Windows entre laptops):
> 1. **Vía Gateway TCP de BD Maestra (`10.219.19.9:5000`)**: Envías un JSON por línea (`\n`) y el Servidor Maestro lee/escribe directamente en la BD Maestra y registra tu Cajero como **`🟢 CONECTADO A BD MAESTRA`**.
> 2. **Vía Conexión SQL Directa (`BD_CajeroRed`)**: Usando las tablas estándar `dbo.Usuario`, `dbo.Cajero`, `dbo.BancoRed`, `dbo.TransferenciaBancaria` y `dbo.Transaccion`.

---

## 2. CÓMO HACER QUE TU CAJERO (ATM) Y TU BANCO SEAN DETECTADOS EN LA RED

Para que el equipo maestro (`10.219.19.9`) detecte automáticamente tu Cajero (`ATM-02`, `ATM-03`, `ATM-04`, etc.) y tu Banco (`BANC02`, `BANC03`, `BANC04`, etc.), tu aplicación debe hacer **dos cosas**:

### Paso 2.1: Anunciarse al iniciar y cada 10 segundos (Heartbeat a `10.219.19.9:5000`)
Conéctate por TCP a **`10.219.19.9:5000`** y envía una línea JSON terminada en `\n`:

```json
{"tipo":"HEARTBEAT","cajero":"ATM-02","nombreCajero":"Cajero Sucursal Norte - Grupo 2","bancoOrigen":"BANC02","efectivoDisponible":10000.00,"idMensaje":"HB001","timestamp":"2026-10-29 11:30:00"}
```

La BD Maestra registrará inmediatamente a `ATM-02` con tu IP real como **`🟢 CONECTADO A BD MAESTRA`** y te responderá:
```json
{"tipo":"RESP_OK","idMensaje":"HB001","codigo":"00","mensaje":"Conectado a BD Maestra en 10.219.19.9:5000","datos":{"codigoBanco":"BANC01","nombreBanco":"NEXUSBANK (BD Maestra)","bdMaestraIp":"10.219.19.9","bdMaestraSql":"localhost\\SQLEXPRESS (BD_CajeroRed)","cupoDisponibleHoy":25000.00,"estado":"EN_LINEA"}}
```

### Paso 2.2: Responder al Escaneo UDP Broadcast en el Puerto `5005` (Opcional pero recomendado)
Escucha en `0.0.0.0:5005` (UDP). Cuando recibas un paquete que empiece con `DISCOVER_BANCO_ATM|` o `DISCOVER_CAJERO_ATM|`, responde por UDP al `RemoteEndPoint` remitente con:
```text
HERE_CAJERO_ATM|<TuCodigoCajero>|<TuNombreCajero>|<TuCodigoBanco>|<TuIpLocal>|5000|<TuEfectivoActual>
```
Ejemplo:
```text
HERE_CAJERO_ATM|ATM-02|Cajero Grupo 2|BANC02|10.219.19.14|5000|10000.00
```

---

## 3. CONSULTAS A LA BASE DE DATOS MAESTRA (`10.219.19.9:5000` TCP)

Todas las operaciones de tu cajero deben enviarse como **una línea JSON terminada en `\n` (`UTF-8`)** a `10.219.19.9:5000`. En todas las peticiones incluye `"cajero": "ATM-02"` (o tu número de cajero) para que la BD Maestra actualice tu estado en tiempo real.

### 3.1 Autenticar Usuario / Consultar Saldo en la BD Maestra (`CONSULTAR_SALDO`)
- **Envías a `10.219.19.9:5000`**:
  ```json
  {"tipo":"CONSULTAR_SALDO","cajero":"ATM-02","bancoOrigen":"BANC02","idMensaje":"Q001","numeroTarjeta":"1234567890123456","pin":"1234"}
  ```
- **Recibes de la BD Maestra (`codigo: "00"` si es válido, `"14"` si no existe, `"55"` si el PIN es incorrecto)**:
  ```json
  {"tipo":"RESP_OK","idMensaje":"Q001","codigo":"00","mensaje":"Consulta exitosa en BD Maestra.","datos":{"numeroTarjeta":"1234567890123456","nombre":"Juan Perez","saldoActual":5000.00,"limiteDiario":2000.00,"estado":"Activo"}}
  ```

### 3.2 Verificar si Existe la Cuenta Destino antes de Transferir (`IB_VERIFICAR_CUENTA`)
- **Envías a `10.219.19.9:5000`**:
  ```json
  {"tipo":"IB_VERIFICAR_CUENTA","cajero":"ATM-02","idMensaje":"V001","bancoOrigen":"BANC02","bancoDestino":"BANC01","numeroCuenta":"1234567890123456"}
  ```
  *(Cuentas precargadas en la BD Maestra para pruebas: en `BANC01`: `1234567890123456` Juan Perez, `1111222233334444` Carlos Mendez, `2222333344445555` Ana Lopez; en `BANC02`: `2002000001` Roberto Gómez, `2002000002` María Fernanda; en `BANC03`: `3003000001` Luis Alberto; en `BANC04`: `4004000001` Diego Ramirez).*
- **Recibes de la BD Maestra**:
  ```json
  {"tipo":"RESP_OK","idMensaje":"V001","codigo":"00","mensaje":"Cuenta verificada en la BD Maestra.","datos":{"existe":true,"codigoBanco":"BANC01","nombreBanco":"NEXUSBANK (Nuestro Banco)","numeroCuenta":"1234567890123456","nombreTitular":"Juan Perez","cupoDisponibleBanco":25000.00,"cupoDisponibleBancoOrigen":25000.00}}
  ```

### 3.3 Ejecutar Transferencia (Interna o Interbancaria) en la BD Maestra (`IB_TRANSFERIR`)
Al confirmar la transferencia, envía a `10.219.19.9:5000`:
- **Envías a `10.219.19.9:5000`**:
  ```json
  {"tipo":"IB_TRANSFERIR","cajero":"ATM-02","idMensaje":"T001","tokenTransferencia":"TRF-20261029-482910","bancoOrigen":"BANC01","cuentaOrigen":"1234567890123456","nombreOrigen":"Juan Perez","bancoDestino":"BANC02","cuentaDestino":"2002000001","nombreDestino":"Roberto Gómez","monto":500.00,"concepto":"Transferencia desde ATM-02"}
  ```
- **Qué hace automáticamente la BD Maestra en `10.219.19.9`**:
  1. Verifica fondos de `cuentaOrigen` y verifica que ambos bancos (`bancoOrigen` y `bancoDestino`) tengan límite diario disponible (`>= monto`).
  2. Resta `monto` al saldo de `cuentaOrigen` y lo suma al saldo de `cuentaDestino`.
  3. **Resta `monto` coordinadamente al límite diario de los dos bancos** (`bancoOrigen` y `bancoDestino`).
  4. **Guarda el `tokenTransferencia` (`TRF-YYYYMMDD-XXXXXX`)** en el registro de Nuestro Banco, del Otro Banco y en el historial del Usuario.
- **Recibes de la BD Maestra**:
  ```json
  {"tipo":"RESP_OK","idMensaje":"T001","codigo":"00","mensaje":"Transferencia procesada en BD Maestra y Token registrado en ambos bancos y usuario.","datos":{"tokenTransferencia":"TRF-20261029-482910","bancoOrigen":"BANC01","bancoDestino":"BANC02","cuentaDestino":"2002000001","nombreDestino":"Roberto Gómez","monto":500.00,"nuevoSaldo":4500.00,"cupoRestanteBancoOrigen":24500.00,"cupoRestanteBancoDestino":24500.00}}
  ```

### 3.4 Consultar Catálogo Completo de Bancos, Cajeros y Tokens en la BD Maestra (`DB_QUERY_ESTADO`)
- **Envías a `10.219.19.9:5000`**:
  ```json
  {"tipo":"DB_QUERY_ESTADO","cajero":"ATM-02","idMensaje":"S001"}
  ```
- **Recibes**: La lista actualizada de todos los `bancos` (con sus límites diarios restantes), todos los `cajeros` conectados a la red y las últimas `transferencias` con sus tokens.
