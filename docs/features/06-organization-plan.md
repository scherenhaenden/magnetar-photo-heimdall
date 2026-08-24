# Organizar archivos por reglas, con plan revisable

Esta funcionalidad convierte una selección de fotos o una biblioteca completa en una estructura de carpetas y nombres coherente. Evoluciona la lógica de PhotoLibrarizer: organiza prioritariamente por fecha de captura y metadatos, pero con resultados deterministas, revisión explícita, ejecución durable y deshacer. No es un simple renombrado: para cada ubicación decide qué hacer, dónde, cómo evitar colisiones y cómo recuperar una operación interrumpida.

## Resultado para la persona usuaria

En **Organizar**, la persona elige una biblioteca, carpeta, álbum o selección, escoge una regla y genera una simulación. Una regla típica es:

```text
Fotos/{capture.year}/{capture.month:02}/{capture.day:02}/
{capture.datetime:yyyy_MM_dd_HH_mm_ss}_{camera.model}_{shortHash}{extension}
```

La previsualización muestra por fila origen, endpoint, destino resuelto, acción, tamaño, fecha empleada, tokens, justificación y avisos. Se puede filtrar, excluir, cambiar una fila, resolver conflictos y aprobar sólo los elementos seleccionados. Al aplicar se ven progreso, verificaciones, errores y el journal; el resultado queda en el historial con **Deshacer plan**.

| Acción | Efecto |
| --- | --- |
| Sin cambios | El activo ya está correctamente ubicado o tiene la misma identidad. |
| Copiar | Crea y verifica un destino sin tocar el origen. |
| Mover seguro | Confirma el destino y sólo entonces lleva el origen a cuarentena. |
| Transferir | Copia entre endpoints/hosts con recibo de integridad. |
| Revisión requerida | No hay cambios hasta que se resuelva el motivo. |

## Reglas, metadatos y subfunciones

Una regla es versionada, tiene nombre, ámbito (global o biblioteca), plantilla de carpeta/nombre, condiciones, fallbacks y política de colisiones. La evaluación es independiente del idioma, configuración regional y zona horaria del cliente: la misma instantánea y versión de regla produce el mismo destino en macOS, Linux, Windows y el agente remoto.

Los tokens se leen del catálogo normalizado, no se reinterpreta el nombre original a cada ejecución:

- **Captura:** `capture.datetime`, año, mes, día, zona horaria, fuente y confianza.
- **Archivo:** nombre original, stem, extensión, tamaño, tipo de medio, hash corto y hash completo.
- **Cámara:** fabricante, modelo, serie y objetivo.
- **Contenido:** rating, etiqueta de color, keywords, álbum y sidecar.
- **Contexto:** raíz de biblioteca, host, volumen y ruta relativa original.

Los valores se normalizan como rutas portables: se eliminan separadores/caracteres prohibidos, nombres reservados de Windows y espacios finales; se limita cada componente y se usa Unicode canónico. La UI siempre presenta la ruta final, no sólo la plantilla.

### Nombres configurables e identidad de contenido

El editor de reglas debe permitir que la persona configure por completo el
formato de carpetas y fichero, con presets editables y una prueba inmediata
sobre fotos reales. Por ejemplo, el patrón histórico
`yyyy-MM-dd-HH-mm-sss-md5hash` se expresa ahora como
`{captureDate:yyyy-MM-dd}-{captureTime:HH-mm-ss-fff}-{hash:12}`. `fff` son
milisegundos; no se usa `sss`, que no es un formato de tiempo estándar.

Además de los presets ya descritos, se entregan: fecha+hora, fecha+hora+cámara,
fecha+hora+contador, nombre original normalizado, y conservar árbol relativo.
La pantalla muestra el patrón, cada token resuelto, el nombre final y las
colisiones para una muestra y para el plan completo. El usuario puede elegir
longitud de hash (por ejemplo, 8, 12 o 16 caracteres) y el comportamiento si
todavía no hay hash: esperar, usar contador temporal o dejar el elemento para
revisión. Una regla aprobada guarda esa política exacta.

`{hash}` significa **BLAKE3 de contenido completo**, con algoritmo y versión
almacenados como evidencia. BLAKE3 sustituye MD5 para identificación, verificación
de copia y deduplicación: MD5 puede leerse sólo como evidencia histórica, pero
no se calcula para trabajo nuevo ni se usa como garantía de integridad. El hash
corto sólo desambigua el nombre; la comprobación siempre utiliza el BLAKE3
completo. Al planificar, una huella rápida BLAKE3 puede priorizar trabajo, pero
no prueba igualdad por sí sola.

La fecha se resuelve mediante política explícita: EXIF/XMP/QuickTime con zona horaria, después creación fiable y por último modificación del sistema. Una fecha inferida o sin zona horaria se marca. Cada regla define si usarla, enviar a `Sin_fecha/`, recurrir a fecha de archivo o pedir intervención. Nunca se inventa una fecha sin avisar.

Subfunciones recomendadas:

- presets: fecha clásica, Año/Mes, cámara por fecha y consolidación que mantiene ruta relativa;
- condiciones por medio, cámara, rango de fechas, rating, etiquetas o origen; la primera coincidencia gana y se explica;
- fallback para token vacío, hash pendiente o dato de cámara ausente;
- exclusiones de carpetas/extensiones, ocultos, derivados y activos ya organizados; quedan registradas como no-op;
- numeración estable tras un orden declarado; se asigna en el plan para que un reintento no renombre de forma distinta;
- tratamiento de grupos RAW+JPEG/HEIC y XMP: se mueven/copian juntos, con relación persistida y hash individual.

## Plan, revisión y conflictos

Generar un plan es sólo lectura. Captura snapshot de activos, rutas, atributos observados, hashes, tokens, versión de regla, tamaño, capacidades y estado de cada endpoint. Su estado es `draft`, `needs_review`, `approved`, `applying`, `completed`, `completed_with_errors`, `cancelled`, `undoing` o `undone`.

La pantalla de revisión permite agrupar por destino, fecha, host o conflicto; filtrar por tipo/acción/aviso; ver antes-después y compañeros; editar una excepción sin modificar el preset; y estimar espacio, permisos y cuarentena. Editar una regla o un metadato que afecte la ruta invalida la aprobación. Antes de ejecutar se revalidan origen y destino contra el snapshot; si han cambiado, la fila se convierte en conflicto.

Una colisión comprende tanto un archivo existente como dos filas que resuelven al mismo destino. Se compara también case-sensitivity y normalización Unicode para APFS, NTFS, SMB y ext4. Para cada una hay evidencia: rutas, tamaños, hashes y metadatos. Las políticas son:

- **Omitir:** conservar origen.
- **Reconocer idéntico:** sólo con hash completo; no-op o cuarentena del origen si fue aprobado.
- **Conservar ambos:** sufijo determinista (`_01`, hash corto o contador del plan).
- **Reemplazar:** sólo en zona gestionada, con destino recuperable y confirmación por fila; el destino anterior va a cuarentena.
- **Elegir otro destino / pendiente.**

Permiso insuficiente, ruta inválida/larga, espacio, lock, enlace roto, host desconectado o cambio desde el escaneo no se silencian: se registran con diagnóstico y acción sugerida. Las filas independientes pueden continuar si el usuario eligió tolerancia a fallos.

## Aplicar, durabilidad y deshacer

**Aplicar** exige confirmación que enumera copias, movimientos, transferencias, archivos en cuarentena, bytes y endpoints. Por defecto, en un endpoint nuevo se usa copiar y verificar; mover exige cuarentena recuperable.

Para cada fila:

1. Revalidar identidad/atributos del origen, permisos, espacio y precondiciones del destino.
2. Crear un destino temporal privado.
3. Copiar o transferir por streaming (reanudable si el protocolo lo soporta).
4. Verificar tamaño y hash completo; publicar mediante rename atómico cuando exista.
5. Escribir cada transición del journal antes de avanzar.
6. Para mover, mandar el origen a cuarentena de su propio endpoint con ruta original y retención.
7. Actualizar catálogo y ubicaciones.

Cancelar detiene filas nuevas y deja la actual segura. Tras caída de aplicación, host, red o montaje, Heimdall recupera journals incompletos y ofrece reanudar, revertir o revisar tras verificar temporales/destinos/cuarentena. No presupone éxito. Deshacer consulta el journal: elimina sólo el destino creado cuyo hash coincide, o restaura de cuarentena a la ruta exacta y plantea un conflicto si fue ocupada. La eliminación de cuarentena es un flujo separado, con retención y confirmación.

## Endpoints locales, montajes y Windows

Un **endpoint** tiene URI estable, identidad de host/volumen, raíz permitida, capacidades y referencia a credenciales en el almacén seguro. El modelo conserva una ruta canónica segmentada; no concatena rutas sin validar.

- **macOS:** APFS, discos externos y montajes en `/Volumes`, incluidos SMB/NFS. Se asocia UUID/identidad de volumen cuando esté disponible.
- **Linux:** rutas locales y montajes CIFS/SMB, NFS, SSHFS/FUSE en `/mnt`, `/media` o rutas definidas. Se detecta capacidad real (rename atómico, case sensitivity, enlaces, espacio).
- **Windows:** discos `C:\\…`, externos, UNC (`\\\\servidor\\recurso\\…`) y unidades mapeadas. Se conserva preferentemente UNC/identidad de volumen porque una unidad mapeada puede no ser visible para el proceso. Se respetan ACL, nombres reservados, rutas largas y semántica de NTFS/SMB.

Un montaje puede desconectarse o tener caché/semántica distinta. Se valida su disponibilidad e identidad antes y durante el plan; si desaparece, se pausa, nunca se redirige por coincidencia de nombre.

## SSH y agente de servidor

Para bibliotecas remotas, Heimdall puede trabajar por SSH o delegar en un **agente Heimdall** instalado en el servidor. El agente enumera, extrae metadatos, calcula hashes y ejecuta copias/renombres locales sin enviar terabytes al cliente. El cliente conserva UI, aprobaciones y el catálogo sincronizado necesario para planificar.

El agente no ejecuta shell arbitrario. Expone sobre túnel SSH un contrato versionado: capacidades, inventario incremental, metadatos, hash bajo demanda, plan remoto, ejecución aprobada, progreso, journal y recuperación. Cada solicitud incluye versión de protocolo, biblioteca, idempotency key, plan y raíces autorizadas. El agente rechaza rutas fuera de alcance o acciones no aprobadas.

La configuración SSH soporta host/puerto/usuario y huella de host fijada; claves o autenticación del sistema con secretos sólo en Keychain, Secret Service o Credential Manager; cifrado, timeout, reconexión y límites de ancho de banda; modo sólo lectura; y anuncio de versión, raíces, espacio y cuarentena. SFTP sin agente puede servir para transferencia controlada, pero Heimdall declara degradadas las acciones sin atomicidad, cuarentena fiable o inventario eficiente. Nunca crea comandos shell con rutas de usuario.

### Transferencias y sincronización de catálogo

Una transferencia host↔host son dos endpoints y un recibo de verificación: temporal en destino, hash confirmado, journal y sólo entonces política sobre el origen. Puede reanudarse por bloques si ambos lados aportan soporte e integridad; de otra forma reinicia de forma segura.

El agente entrega cambios incrementales por cursor: activos, ubicaciones vistas/desaparecidas, metadatos, hashes, operaciones y diagnósticos. El cliente confirma el cursor y, si expira o el servidor se reconstruye, solicita inventario completo y marca datos previos para reconciliar. La identidad se basa en IDs de activo/ubicación, versión/ETag y atributos observados, no sólo path. Planes concurrentes se protegen con precondiciones; los journals remoto y local comparten id de operación y al reconectar el cliente consulta el estado autoritativo, no repite acciones.

### Ritmo de trabajo y temperatura

Un plan consulta el controlador térmico antes de iniciar filas intensivas
(hashes, transcodificación, copias y previews) y entre bloques de trabajo. Si
el cliente o el endpoint remoto supera el umbral configurado, reduce primero
concurrencia y prioridad de tareas; en umbral crítico no inicia más bloques y
deja el plan en `paused_thermal`. La copia ya publicada no se deshace y la fila
en curso termina o se detiene sólo en un punto seguro. Al enfriarse durante un
periodo de histéresis, el usuario puede reanudar o activar la reanudación
automática.

El estado del plan indica qué máquina limita el ritmo, temperatura, umbral,
concurrencia anterior/nueva y próxima comprobación, sin ocultar ni atribuir al
cliente la temperatura de un servidor. El protocolo del agente SSH incorpora
telemetría y su política efectiva; el agente aplica el límite local incluso si
el cliente se desconecta. Véase [control térmico](12-thermal-workload-control.md).

## Modelo y UX

Entidades mínimas: `OrganizationRule`/`RuleVersion`, `OrganizationPlan`, `PlanItem`, `Endpoint`, `OperationJournal`, `RemoteSyncCursor` y `RemoteOperationReceipt`. Deben contener plantilla, excepciones, snapshot, destinos resueltos, precondiciones, decisiones de conflicto, hashes, temporales, cuarentena, recibos, errores, cursor y claves idempotentes. El journal es append-only; los paths y logs son sensibles y nunca incluyen secretos.

La UX separa claramente **Regla**, **Previsualizar**, **Conflictos** y **Aplicar/Historial**. Cada endpoint muestra conexión, permisos, espacio, cuarentena y última sincronización. En un plan mixto se desglosa qué archivos son locales, montados y remotos. Los botones describen la consecuencia concreta —«Copiar y verificar 128», «Mover 42 a cuarentena», «Enviar al agente remoto»— y las opciones peligrosas exigen confirmación adicional con rutas exactas. Sin red se preparan planes locales; los remotos quedan pausados y se revalidan al reconectar.

## Criterios de aceptación

- La misma regla produce el mismo destino normalizado en hosts compatibles y avisa incompatibilidades antes de aplicar.
- La simulación no modifica archivos; ninguna colisión se resuelve sin política explícita y evidencia.
- Todo destino confirmado tiene hash y journal recuperable; ningún movimiento borra directamente el origen.
- Una caída de cliente, agente, red o montaje permite recuperar sin duplicar operaciones ni perder cuarentena.
- El agente SSH queda limitado a raíces aprobadas, identidad de host verificada, secretos externos al catálogo y llamadas versionadas/idempotentes.
- El catálogo remoto indica su frescura y detecta cambios antes de ejecutar el plan.
