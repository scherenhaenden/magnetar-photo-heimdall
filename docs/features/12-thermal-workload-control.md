# Control térmico de la carga de trabajo

Heimdall debe proteger tanto el equipo cliente como los servidores que alojan
bibliotecas. Escanear, calcular BLAKE3, generar previews RAW, exportar y copiar
muchos archivos puede elevar temperatura, agotar batería o provocar *thermal
throttling*. Esta función adapta la carga; no sustituye la protección de
hardware del sistema operativo.

## Lo que configura y ve el usuario

En **Rendimiento y temperatura**, la persona puede seleccionar una política
global y excepciones por biblioteca/endpoint:

- `Equilibrada` (predeterminada), `Silenciosa`, `Máximo rendimiento` o
  `Personalizada`;
- temperatura de aviso, de reducción y crítica; histéresis y tiempo mínimo de
  enfriamiento;
- concurrencia normal/mínima para scan, hash, previews, exportación y copias;
- límites de I/O y red, trabajo sólo con corriente, y horario de ejecución.

El tablero muestra por máquina: lectura más reciente, fuente, disponibilidad,
temperatura, estado térmico, política aplicada, tareas activas y causa de una
reducción/pausa. Si no existe sensor fiable, se indica explícitamente
`telemetría no disponible`; no se inventa una lectura ni se bloquea la función.

## Comportamiento del planificador

| Estado | Acción |
| --- | --- |
| Normal | Usa el límite de concurrencia configurado. |
| Aviso | Reduce progresivamente concurrencia y baja prioridad de hashes/previews. |
| Alto | Mantiene sólo trabajos esenciales, limita I/O/red y no inicia lotes pesados. |
| Crítico | Pausa nuevas unidades de trabajo de ese host/endoint (`paused_thermal`). |
| Enfriando | Espera histéresis; después reanuda gradualmente o pide aprobación, según política. |

Las unidades de trabajo son pequeñas y reanudables: un hash por archivo, una
preview, un bloque de transferencia o una fila segura del plan. El controlador
no mata un proceso a mitad de una publicación atómica ni deja una operación
ambigua. Registra el motivo, umbral, lectura, cambio de concurrencia y hora en
el journal de trabajo. Cancelar siempre sigue siendo una acción separada.

La carga se regula por **host real**, no por vista del cliente: una biblioteca
montada se limita según el cliente que hace el I/O; un agente SSH limita su
propio servidor. En un plan mixto, un servidor caliente no detiene trabajo
seguro en un disco local independiente.

## Fuentes de telemetría por plataforma

- **macOS:** adaptador de telemetría del sistema cuando exponga estado térmico o
  sensores; si Apple no ofrece una API pública y estable para una lectura, se
  usa el estado térmico disponible y se declara la limitación.
- **Linux:** sensores expuestos por `sysfs`/`hwmon` o servicio autorizado del
  sistema. Se registra qué sensor representa CPU, GPU o disco y la unidad.
- **Windows:** proveedor de telemetría compatible (por ejemplo, sensores del
  sistema/firmware accesibles con permisos concedidos). WMI no se presupone
  suficiente en todo hardware; la UI comunica ausencia o baja confianza.
- **Servidor SSH:** el agente Heimdall lee únicamente adaptadores permitidos
  por su administrador y publica valores tipados (`sensor`, `celsius`,
  `observedAt`, `confidence`, `thermalState`). No ejecuta comandos shell que
  envíe el cliente.

La temperatura es opcional y dependiente del hardware. Además, Heimdall puede
usar señales seguras como batería, alimentación, carga media, velocidad de
disco, errores térmicos del SO o *thermal state*, pero las etiqueta como tales:
no las presenta como grados Celsius.

## Contrato remoto, privacidad y seguridad

El `GET capabilities/health` del agente informa sensores disponibles, rangos,
precisión, política permitida y último estado; el cliente solicita snapshots o
recibe eventos acotados por el protocolo versionado. El agente conserva el
control final de sus propios procesos y no acepta umbrales que excedan el
máximo administrativo. Ante desconexión, mantiene su política local y devuelve
el journal al reconectar.

Las lecturas se consideran datos operativos: el catálogo guarda agregados y
eventos necesarios para explicar pausas, con retención configurable. No se
almacenan secretos, procesos ajenos, rutas no relacionadas ni inventario del
hardware del servidor. El permiso de acceso a sensores y al agente sigue el
principio de mínimo privilegio.

## Criterios de aceptación

- Una tarea intensiva reduce su concurrencia antes de la pausa crítica y puede
  reanudarse sin recalcular ni corromper resultados ya confirmados.
- Cliente y cada agente remoto se limitan independientemente y muestran su
  propia telemetría/frescura.
- Sin sensores, el producto funciona con el estado claramente indicado y una
  política conservadora configurable.
- Ninguna decisión térmica ejecuta shell arbitrario, borra archivos ni omite
  el journal de una operación.
