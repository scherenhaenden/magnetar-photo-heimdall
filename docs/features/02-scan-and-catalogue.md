# Discover and catalogue media

## Propósito

Heimdall construye un catálogo local de los archivos observados sin cambiar un
solo archivo fuente. El catálogo es la base de la galería, búsquedas,
metadatos, detección de duplicados y planes de organización. Un *asset*
representa el contenido lógico; una *location* representa una aparición de ese
contenido en una ruta concreta. Por eso un mismo archivo verificado puede vivir
en varios discos, mounts o servidores sin perder historial ni crear fichas
duplicadas.

El escáner debe incorporar la lógica moderna de PhotoLibrarizer: recorrido
recursivo, extensiones configurables, inventario incremental y tolerancia a
errores. Heimdall la convierte en un servicio persistente, observable y seguro,
aplicable tanto a volúmenes locales como a almacenamiento remoto.

## Qué ve y hace el usuario

### Iniciar un escaneo

Desde una raíz de biblioteca el usuario pulsa **Escanear ahora**, escoge el
modo (rápido/incremental o completo) y, opcionalmente, ajusta los tipos de
archivo. La pantalla deja claro que el escaneo es solo lectura: no mueve,
renombra, genera sidecars ni corrige fechas.

1. Heimdall valida que la raíz esté accesible y muestra el tipo de conexión:
   local, volumen montado o agente SSH.
2. Muestra una estimación cuando puede obtenerla (número de directorios,
   elementos conocidos de un escaneo anterior y espacio disponible).
3. Mientras recorre, la vista enseña ruta actual, archivos/bytes procesados,
   velocidad, tiempo estimado, errores y una muestra de archivos encontrados.
4. El usuario puede pausar, reanudar o cancelar. Pausar conserva un checkpoint;
   cancelar conserva únicamente registros ya confirmados y marca la revisión
   como incompleta.
5. Al finalizar aparece un resumen: nuevos, modificados, sin cambios,
   desaparecidos, no compatibles, pospuestos y fallidos. Cada cifra abre la
   lista correspondiente y los errores llevan al diagnóstico exacto.

Una raíz puede tener escaneos programados o bajo demanda. Un escaneo completo
no bloquea la navegación: los resultados se hacen visibles por lotes atómicos.

### Alcance y filtros de descubrimiento

El usuario puede incluir o excluir extensiones, directorios y patrones. Los
valores iniciales contienen formatos de imagen, RAW, vídeo y audio soportados,
pero el listado es visible y editable; una extensión desconocida se cuenta como
no compatible en vez de desaparecer silenciosamente. Los filtros admiten:

- inclusiones/exclusiones por extensión y ruta relativa;
- ocultar archivos del sistema, cachés, paquetes de aplicación y directorios
  internos de Heimdall;
- seguir o no enlaces simbólicos, con detección de ciclos;
- tamaño mínimo/máximo y la opción de inventariar archivos no multimedia;
- exclusiones por reglas versionadas y una previsualización de qué quedaría
  fuera antes de ejecutar.

Cambiar el alcance no borra el historial. La siguiente revisión indica qué
ubicaciones están ahora fuera de política, sin confundirlas con archivos
eliminados del disco.

## Orígenes y ejecución remota

### Local y mounts

Una ruta local, un disco USB, un NAS montado y una carpeta SMB/NFS/AFP/FUSE se
tratan como una raíz de ruta normal siempre que el sistema operativo la haya
montado. macOS y Linux suelen exponer estos recursos como directorios; Windows
puede usar una letra de unidad (por ejemplo `Z:\\Fotos`) o una ruta UNC
(`\\servidor\\recurso\\Fotos`). Heimdall guarda el identificador de volumen y
la ruta canónica, no presupone un prefijo concreto.

Un mount es una dependencia externa: si se desmonta, cambia de letra o presenta
otra identidad, la raíz pasa a **offline/sospechosa** y no se considera vacía.
El programa nunca marca todas sus ubicaciones como desaparecidas hasta que el
origen vuelva a estar disponible y se complete una revisión válida. La UI
explica cómo reconectar el mount y permite reasociar una ruta nueva con la raíz
existente después de verificar identidad y una muestra de contenido.

### Agente SSH

Para un servidor no montado, Heimdall conecta por SSH a un **Heimdall Agent**
versionado que se ejecuta en el servidor y escanea rutas locales para ese
servidor. El cliente recibe inventario, checkpoints, diagnósticos y cambios del
agente; no necesita montar el disco ni descargar las fotos. El agente es el que
lee los metadatos y calcula hashes que se hayan solicitado, cerca de los datos.

La configuración visible incluye host, puerto, identidad del servidor, raíz(es)
remota(s), huella de clave, capacidad del agente y estado de conexión. La clave
del host se fija tras confirmarla (*host-key pinning*); credenciales y claves
privadas permanecen en el almacén seguro del sistema, nunca en el catálogo ni
en una exportación. El protocolo tiene versión, capacidades y límites de
compatibilidad para que un cliente nuevo no envíe instrucciones ambiguas a un
agente antiguo.

El agente expone operaciones de inventario de solo lectura, estado de trabajo y
recuperación desde checkpoint. Las futuras operaciones de organización se
mandarán como planes explícitos y auditables, no como órdenes shell ni rutas
interpoladas. Si se pierde SSH, el trabajo queda en estado recuperable: el
cliente puede reconectar y pedir el estado por identificador de ejecución, o
reanudar desde el último lote confirmado. Ningún reintento debe duplicar
ubicaciones ni reinterpretar un resultado parcial como borrado.

## Proceso de escaneo

1. **Preparar revisión.** Se crea una `ScanRun` con una instantánea de la
   configuración, capacidades del origen, cursor/checkpoint vacío y estado
   `Preparing`.
2. **Enumerar.** Se recorre de forma paginada; se normaliza cada ruta relativa
   según las reglas del origen y se registra identidad de archivo, tamaño,
   fechas y extensión. No se carga el archivo completo en memoria.
3. **Clasificar.** Se aplican las reglas de alcance y se reconocen los tipos de
   medio. Los archivos candidatos pasan a extracción posterior; los demás se
   contabilizan con su motivo.
4. **Comparar.** Para una ubicación conocida se comparan identidad, tamaño,
   marcas de tiempo y, cuando procede, una huella rápida. Solo los nuevos o
   cambiados se vuelven a analizar; el modo completo invalida esta optimización
   sin omitir la trazabilidad.
5. **Persistir por lotes.** Cada lote se escribe transaccionalmente con su
   secuencia de cursor. Así un apagón, un desmontaje o un corte SSH deja datos
   coherentes y recuperables.
6. **Conciliar.** Al terminar correctamente, las ubicaciones previamente
   conocidas pero no vistas se marcan `Missing` (no se borran). La conciliación
   solo ocurre si la raíz estuvo estable y se completó el alcance previsto.
7. **Publicar.** Se actualiza la revisión activa de la raíz y se emiten eventos
   para galería, indexación y cola de metadatos/miniaturas. El resumen conserva
   métricas y diagnósticos de esa ejecución.

Una comprobación rápida puede apoyarse en eventos del sistema de archivos
(FSEvents en macOS, inotify en Linux, USN Journal/ReadDirectoryChangesW en
Windows) cuando estén disponibles, pero esos eventos solo son una pista: un
escaneo periódico conserva la corrección.

## Sincronización del catálogo

El catálogo principal reside en el cliente Heimdall. Cada origen mantiene su
última revisión confirmada, un cursor remoto y una identidad estable. En SSH,
el agente conserva un inventario/estado de trabajo local y devuelve deltas
ordenados (`upsert location`, `observed`, `diagnostic`, `checkpoint`,
`completed`) con identificadores idempotentes. El cliente confirma la secuencia
aplicada; tras desconexión puede solicitar eventos desde la última secuencia.

El servidor nunca es una autoridad ciega sobre la identidad global de assets:
las ubicaciones entrantes se fusionan por hash de contenido verificado cuando
exista. Antes de tenerlo, se mantienen como candidatos separados para evitar
falsos duplicados. La sincronización transmite los metadatos necesarios para
catalogar, no miniaturas ni binarios, salvo una petición explícita posterior.

Los conflictos se muestran, no se resuelven ocultamente: una ruta puede haber
cambiado mientras un agente estaba desconectado, dos agentes pueden informar
del mismo contenido, y un reloj remoto puede ser incorrecto. Cada observación
guarda origen, momento de observación y confianza; el usuario puede forzar un
escaneo completo o reasociar una raíz.

## Modelo de datos mínimo

| Entidad | Campos y responsabilidad |
| --- | --- |
| `LibraryRoot` | Id, nombre, tipo de acceso (`Local`, `Mounted`, `SshAgent`), URI/ruta canónica, identidad de volumen/servidor, política de alcance, capacidades, estado y última revisión válida. |
| `ScanRun` | Id, raíz, modo, configuración congelada, estado, inicio/fin, cursor/checkpoint, contadores, versión de agente/protocolo y motivo de fallo/cancelación. |
| `Asset` | Id estable, tipo de medio, hash de contenido verificado opcional, firma rápida opcional, estado de análisis y fechas derivadas. No depende de una ruta. |
| `AssetLocation` | Id, asset, raíz, ruta relativa y clave normalizada, identidad de archivo/volumen, tamaño, fechas filesystem, extensión, estado (`Present`, `Missing`, `Excluded`, `Unreadable`) y última vez observada. |
| `ScanObservation` | Ejecución, ubicación/ruta, secuencia, resultado, atributos observados y marca temporal; permite auditoría e idempotencia. |
| `Diagnostic` | Ejecución/origen/ruta, severidad, código estable, mensaje seguro, excepción técnica opcional y acciones de recuperación. |
| `RemoteEndpoint` | Host, puerto, huella de host, identidad de agente, capacidades, protocolo y referencia a secreto en el keychain. |

La clave única de una ubicación es `(LibraryRootId, NormalizedRelativePath)`.
La comparación de rutas respeta las reglas del origen: no se asume que Windows
ni macOS/Linux tengan la misma sensibilidad a mayúsculas o Unicode.

## Errores y estados recuperables

El escáner continúa ante permisos denegados, archivo que desaparece, enlace
roto, nombre no representable, archivo bloqueado, metadatos corruptos, timeout,
montaje ausente, cuota llena, protocolo incompatible o SSH caído. Cada caso se
clasifica como advertencia recuperable, fallo de entrada o fallo de ejecución.
El resumen diferencia claramente “no observado porque el origen no estaba
disponible” de “confirmado como ausente”.

Los reintentos son acotados y con espera progresiva. Al recuperar conectividad,
Heimdall valida identidad y cursor antes de continuar. Si cambió la raíz o el
agente perdió su estado, se pide un escaneo completo; nunca se mezclan deltas de
dos identidades distintas.

## Rendimiento y límites

La enumeración se realiza en streaming, con concurrencia limitada y configurable
para no saturar discos, NAS, CPU ni enlace SSH. La prioridad es mantener el
equipo usable. Se separan las etapas de enumerar, analizar metadatos, generar
miniaturas y calcular hashes; las tres últimas pueden continuar en colas de
baja prioridad tras terminar el inventario.

Las rutas, estadísticas y firmas rápidas permiten evitar lecturas repetidas.
Los hashes completos se calculan bajo demanda o para candidatos a duplicado,
nunca para cada archivo automáticamente salvo una política explícita. La UI
presenta límites de ancho de banda/concurrencia por origen, consumo estimado y
la posibilidad de pausar trabajos intensivos.

## Seguridad y privacidad

Escanear es estrictamente de solo lectura. El proceso restringe su recorrido a
la raíz autorizada, normaliza rutas y no sigue enlaces fuera del alcance sin
permiso explícito. No ejecuta comandos remotos arbitrarios ni construye comandos
shell con nombres de archivo. Las credenciales SSH se usan con privilegios
mínimos y pueden revocarse desde la configuración.

El catálogo guarda rutas y metadatos localmente; la telemetría es opt-in y no
incluye rutas, nombres de fotos, claves ni datos EXIF. Los diagnósticos deben
redactar secretos. Toda comunicación cliente-agente viaja por SSH autenticado y
versionado, y cada respuesta se valida por tamaño, esquema y secuencia antes de
persistirla.
