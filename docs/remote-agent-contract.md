# Contrato del agente remoto (v1)

El cliente conecta por un túnel SSH que administra el operador. El túnel sólo
transporta RPC tipado: el cliente nunca entrega texto de shell, comandos ni
rutas absolutas al agente.

## Compatibilidad

Cada petición incluye `ProtocolVersion { major, minor }`. V1 requiere que el
`major` coincida; el agente puede aceptar un `minor` igual o anterior al suyo.
`GetCapabilities` expone la versión que sirve el agente antes de enviar carga.

## Métodos RPC

| Método | Resultado | Uso |
| --- | --- | --- |
| `GetCapabilities` | roots permitidos, sensores, trabajos y concurrencia máxima | Descubrimiento de política |
| `GetHealth` | estado y hora de observación | Disponibilidad del agente |
| `GetThermal` | lecturas tipadas o `Unavailable` | Control térmico |
| `SubmitWorkload` | receipt con concurrencia efectiva | Solicita scan/hash/preview/export/transfer |
| `GetReceipt` | receipt actualizado | Reconcilia trabajo tras reconectar |

## Límites de ruta

Un workload nombra un `RootId` publicado por el agente y un `RelativePath`.
El agente rechaza roots no publicados, paths absolutos, barras inversas,
segmentos vacíos, `.` y `..`. Resuelve la ruta sólo bajo el root configurado y
vuelve a comprobar la contención después de resolver enlaces según su política
local. No existe ningún RPC de ejecución de comandos.

## Térmico y receipts

Las lecturas contienen sensor, Celsius, hora, confianza y estado de origen. Si
no hay telemetría fiable se devuelve `Unavailable`, nunca una temperatura
inventada. Por host, el controlador pasa por Normal, Warning, High, Critical y
Cooling; en Critical devuelve `PausedThermal` y conserva un receipt. La salida
de Cooling exige temperatura por debajo de `warning - hysteresis` durante el
tiempo mínimo configurado. Los receipts son idempotentes mediante una clave de
cliente y registran estado, motivo, hora y concurrencia efectiva.
