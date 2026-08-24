# Contrato del agente remoto Heimdall — v1

Este documento describe el protocolo, los invariantes de seguridad y el modelo
operativo del agente Heimdall remoto accesible por SSH.

---

## 1. Principio fundamental: RPC tipado, nunca shell

El cliente **nunca** entrega texto de shell, comandos arbitrarios ni rutas
absolutas al agente. Toda la comunicación se realiza mediante mensajes RPC
tipados versionados (`IRemoteAgentRpcV1 v1.x`). El agente no expone ningún
método que ejecute texto de shell con los privilegios del proceso.

---

## 2. Versiones de protocolo

Cada mensaje lleva un campo `ProtocolVersion { Major, Minor }`.

| Regla | Detalle |
|---|---|
| `Major` igual | Cambios de Major indican ruptura de contrato. El agente rechaza Major distinto. |
| `Minor` del cliente ≤ `Minor` del agente | Extensiones backward-compatible bumpen Minor. |
| `GetCapabilities` primero | El cliente debe llamar a `GetCapabilities` antes de enviar carga y usar la versión publicada por el agente. |

---

## 3. Métodos RPC

| Método | Respuesta | Propósito |
|---|---|---|
| `GetCapabilitiesAsync` | `AgentCapabilities` | Descubrir roots, sensores, operaciones y concurrencia máxima. |
| `GetHealthAsync` | `AgentHealthSnapshot` | Comprobar disponibilidad y degradación del agente. |
| `GetThermalAsync` | `ThermalSnapshot` | Obtener lecturas térmicas tipadas o `Unavailable` explícito. |
| `SubmitOperationAsync` | `OperationReceipt` | Solicitar una unidad de trabajo (Scan/Hash/Preview/Export/Transfer). |
| `GetReceiptAsync` | `OperationReceipt` | Reconciliar trabajo tras reconexión; idempotente por `ReceiptId`. |

---

## 4. Límites de ruta — defensa en profundidad

`OperationRequest.RelativePath` es validado en **dos capas**:

1. **Contrato (cliente):** `RemotePath.ValidateRelative` rechaza en el punto de
   construcción del mensaje:
   - Rutas absolutas (`Path.IsPathRooted`).
   - Barras inversas (`\`).
   - Bytes nulos (`\0`).
   - Segmentos vacíos, `.` y `..`.
   - Longitud > 4 096 caracteres.

2. **Agente (servidor):** al resolver la ruta bajo el root configurado:
   - Vuelve a aplicar las mismas reglas.
   - Resuelve la ruta final con `Path.GetFullPath` o equivalente.
   - Verifica que la ruta resultante sea hija del root asignado.
   - Comprueba la contención *tras* resolver symlinks según la política local.

> **Ningún `RootId`** aceptado por el agente puede ser `..` ni contener `..`.
> El id de root tiene únicamente caracteres ASCII alfanuméricos, `-` y `_`.

---

## 5. Telemetría térmica y reducción de carga

### 5.1 Estados del controlador

```
Normal ──► Warning ──► High ──► Critical ──► Cooling ──► Normal
                                              │               ▲
                                              └───────────────┘
                                              (hysteresis satisfied)
```

| Estado | Concurrencia | Acepta nueva carga | Condición de entrada |
|---|---|---|---|
| `Normal` | `NormalConcurrency` | Sí | peak < WarningCelsius |
| `Warning` | `WarningConcurrency` | Sí | peak ≥ WarningCelsius |
| `High` | `HighConcurrency` | **No** | peak ≥ HighCelsius |
| `Critical` | **0** | **No** | peak ≥ CriticalCelsius |
| `Cooling` | **0** | **No** | saliendo de Critical, aún en histéresis |
| `Unavailable` | **0** | **No** | sin sensor, permiso denegado o lectura vencida |

### 5.2 Reanudación segura desde Critical

La salida de `Cooling` exige que **ambas** condiciones se cumplan
simultáneamente:

- **Temperatura:** `peak < WarningCelsius − HysteresisCelsius`
- **Tiempo:** la temperatura segura debe mantenerse de forma continua durante `MinimumCoolingDuration`

El controlador no puede forzarse a saltar `Cooling` desde el exterior.

### 5.3 Lecturas vencidas

Una lectura cuyo `ObservedAt` sea anterior a
`now − ReadingStalenessWindow`, futura o físicamente inválida se descarta. Si
la telemetría no es completamente fiable, el controlador la trata como
`Unavailable` y no inicia trabajo nuevo.

### 5.4 Plataformas sin sensor

`UnavailableThermalProvider` devuelve siempre un snapshot
`TelemetryAvailability.Unavailable` con lista de lecturas vacía y un motivo
textual. **Nunca fabrica grados Celsius.** El controlador no inicia trabajo
nuevo hasta volver a disponer de telemetría fiable.

---

## 6. Idempotencia y receipts

- Cada `OperationRequest` lleva un `IdempotencyKey` generado por el cliente
  (≤ 128 caracteres, opaco).
- El agente persiste el receipt en su **journal local** antes de contestar.
- Re-enviar la misma `IdempotencyKey` devuelve el receipt original sin
  duplicar trabajo.
- El cliente puede recuperar un receipt por `ReceiptId` en cualquier momento
  tras reconectar con `GetReceiptAsync`.
- Los receipts incluyen: `ReceiptId`, `IdempotencyKey`, `Status`, `AcceptedAt`,
  `EffectiveConcurrency` y `Reason` opcional.

---

## 7. Contrato de seguridad SSH

### 7.1 Autenticación del host — host key pinning

- El cliente **pineará** la clave pública del host SSH del agente.
- La huella digital se almacena en configuración local del cliente
  (nunca en SQLite, nunca en código fuente).
- Si la huella no coincide, la conexión se rechaza sin fallback.
- Se usa autenticación por clave pública del cliente; las contraseñas SSH
  están deshabilitadas en el servidor.

### 7.2 Secretos fuera de SQLite

| Secreto | Almacén |
|---|---|
| Clave privada SSH del cliente | macOS Keychain / sistema de secretos del SO |
| Huella del host SSH | Fichero de configuración del cliente (fuera de la BD) |
| Tokens de agente (si aplica) | Variable de entorno o fichero de credenciales con permisos 600 |

> El catálogo SQLite de Heimdall **no almacena credenciales** ni huellas SSH.

### 7.3 Roots permitidas

- El agente publica únicamente las roots configuradas por el operador en
  su fichero de configuración local (no en SQLite del cliente).
- El cliente descubre las roots con `GetCapabilities` y nunca construye
  rutas fuera de las roots publicadas.
- El operador puede añadir/retirar roots sin modificar el protocolo.

### 7.4 Journal local del agente

- El agente persiste cada `OperationRequest` aceptada y su `OperationReceipt`
  en un journal local (SQLite o append-only log) **antes** de responder al cliente.
- El journal permite recuperar el estado tras un reinicio inesperado.
- Entradas de journal: `ReceiptId`, `IdempotencyKey`, `RootId`,
  `RelativePath`, `Kind`, `Status`, `AcceptedAt`, `UpdatedAt`, `Reason`.

### 7.5 Recuperación idempotente

1. El cliente se reconecta por SSH.
2. Llama a `GetCapabilities` para verificar versión de protocolo.
3. Llama a `GetReceipt` con el `ReceiptId` o `IdempotencyKey` conocido.
4. Si el receipt devuelve `PausedThermal`, el cliente puede reenviar el
   mismo `OperationRequest` (misma `IdempotencyKey`) cuando el estado
   térmico lo permita.
5. El agente detecta la duplicación por `IdempotencyKey` y devuelve
   el receipt existente sin relanzar el trabajo.

---

## 8. Propuesta de integración en BusinessLogic (sin romper la arquitectura)

```
Magnetar.Photo.Heimdall.BusinessLogic
  └── IRemoteLibraryScanPort          ← nueva interfaz de puerto (hexagonal)
        │
        │  depende de
        ▼
Magnetar.Photo.Heimdall.RemoteContracts
  ├── IRemoteAgentRpcV1               ← contrato RPC (sin implementación de transporte)
  ├── ThermalWorkloadController       ← lógica pura de decisión
  └── WorkloadPolicy                  ← configuración de política térmica
```

### Pasos concretos

1. **Agregar `RemoteContracts` como referencia** en `BusinessLogic.csproj`
   (solo la capa de contratos, sin transporte SSH).
2. **Definir `IRemoteLibraryScanPort`** en `BusinessLogic`:
   ```csharp
   public interface IRemoteLibraryScanPort
   {
       Task<ScanResult> ScanRemoteAsync(
           string agentId, string rootId, string relativePath,
           CancellationToken cancellationToken = default);
   }
   ```
3. **Inyectar `ThermalWorkloadController`** en el servicio que use el puerto:
   el controlador decide el `RequestedConcurrency` del `OperationRequest`
   antes de enviarlo al agente.
4. **Implementar el adaptador SSH** en un proyecto nuevo
   `Magnetar.Photo.Heimdall.RemoteAgent.Ssh` que implemente
   `IRemoteAgentRpcV1` usando la biblioteca SSH elegida. Este proyecto
   **no es referenciado** por `BusinessLogic`; se registra en el contenedor
   de inyección de dependencias en `Host`.
5. **Registro en `Host`:**
   ```csharp
   services.AddSingleton<WorkloadPolicy>(agentPolicy);
   services.AddSingleton<ThermalWorkloadController>();
   services.AddScoped<IRemoteAgentRpcV1, SshRemoteAgentRpc>();
   services.AddScoped<IRemoteLibraryScanPort, RemoteLibraryScanAdapter>();
   ```

De este modo:
- `BusinessLogic` solo depende de `RemoteContracts` (tipos puros, sin I/O).
- El transporte SSH y la política SSH viven en capas externas.
- Los tests de `BusinessLogic` pueden instanciar un `ThermalWorkloadController`
  real sin I/O de red.
- La arquitectura hexagonal existente no se modifica.
