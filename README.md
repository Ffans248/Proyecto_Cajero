# 🏧 Proyecto Cajero Automático (ATM System)

![C#](https://img.shields.io/badge/C%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-5C2D91?style=for-the-badge&logo=.net&logoColor=white)
![WPF](https://img.shields.io/badge/WPF-blue?style=for-the-badge&logo=windows&logoColor=white)
![Arquitectura en Capas](https://img.shields.io/badge/Arquitectura-N--Capas-orange?style=for-the-badge)

> Un sistema completo de simulación de un Cajero Automático (ATM) desarrollado en C# utilizando **Windows Presentation Foundation (WPF)** para la interfaz gráfica y una robusta **Arquitectura en Capas (N-Tier)** para separar la lógica de negocio, el acceso a datos y la integración de hardware.

---

## 🚀 Características Principales

*   🔐 **Autenticación Segura:** Ingreso mediante PIN con encriptación (soporte para cambio de PIN) y lectura de tarjetas RFID.
*   💰 **Transacciones Financieras:** 
    *   Retiro de efectivo con gestión inteligente de billetes y validación de saldo.
    *   Depósito a cuentas.
    *   Consulta de saldo en tiempo real.
*   📋 **Historial de Movimientos:** Registro detallado de transacciones por usuario y reportes de estado del cajero.
*   ⚙️ **Panel de Administración:** Interfaz exclusiva para administradores que permite gestionar la bóveda, recargar billetes y supervisar las operaciones del cajero.
*   💾 **Persistencia de Datos:** Sistema de almacenamiento rápido basado en archivos `.csv` para Bóveda, Usuarios y Transacciones.

---

## 🏗️ Arquitectura del Sistema

El proyecto está diseñado bajo el patrón de **Arquitectura de 4 Capas** para garantizar la escalabilidad y el mantenimiento del código:

1.  🖥️ **Cajero.UI (Capa de Presentación):**
    *   Desarrollada en WPF, contiene las ventanas de interacción (Login, Menú Principal, Retiros, Depósitos, Panel de Admin, etc.).
    *   Implementa componentes reutilizables como `CustomMessageBox`.

2.  🧠 **Cajero.BLL (Capa de Lógica de Negocio):**
    *   Contiene las reglas del negocio: `GestorTransacciones`, `UsuarioLogica`, `ServicioAutenticacion`.
    *   Uso intensivo de **DTOs** (Data Transfer Objects) para pasar información segura entre la UI y la base de datos (ej. `UsuarioDto`, `EstadoBovedaDto`).

3.  🗄️ **Cajerro.DAL (Capa de Acceso a Datos):**
    *   Implementa el patrón **Repository** (`UsuarioRepository`, `BovedaRepository`, `TransaccionRepository`).
    *   Maneja la lectura/escritura mediante `GestorArchivosCSV` y la serialización de los modelos (`Usuario`, `Transaccion`, `Billete`).
    *   Incluye helpers de seguridad como `PinSecurityHelper`.

4.  📡 **Cajero.Hardware (Capa de Hardware):**
    *   Módulo para la integración de dispositivos físicos como el `LectorRFID`.

---

## 🛠️ Tecnologías Utilizadas

*   **Lenguaje:** C#
*   **Framework UI:** WPF (Windows Presentation Foundation)
*   **Base de Datos / Almacenamiento:** Archivos planos (CSV) implementando contexto personalizado (`CsvFileContext`).
*   **Entorno de Desarrollo:** Visual Studio / SQL Server Management Studio (SSMS).
*   **Diseño:** Interfaz estilizada con recursos XAML.

---

## ⚙️ Estructura del Proyecto

```text
📦 Proyecto_Cajero
 ┣ 📂 Cajero.UI         # Interfaces de usuario (XAML) y Code-Behind.
 ┣ 📂 Cajero.BLL        # Reglas de negocio, DTOs y Servicios (Interfaces y Clases).
 ┣ 📂 Cajerro.DAL       # Repositorios, Modelos, Enums y Acceso a CSV.
 ┣ 📂 Cajero.Hardware   # Clases para integración con Lector RFID.
 ┗ 📜 ProyectoCajero.sln # Solución principal del proyecto.
