# 11 — Salud, diagnóstico y saneamiento de bibliotecas

## Propósito

La vista de Salud responde a una pregunta práctica: **¿puedo confiar en el
catálogo, los archivos y los planes que voy a aplicar?** Detecta problemas de
lectura, metadatos, ubicaciones, nombres, derivados, duplicados y operaciones
pendientes. Propone correcciones explicadas, pero un diagnóstico nunca cambia
un archivo por sí mismo.

Sustituye las rutinas antiguas de saneamiento de media directories, limpieza de
extensiones, eliminación de `Thumbs.db`/`.directory` y reparación de nombres o
timestamps por un modelo seguro: seleccionar, previsualizar, aprobar un plan,
ejecutar con journal y deshacer. Incluso los archivos claramente prescindibles
se mueven antes a cuarentena; no hay borrado directo.

## Áreas de diagnóstico

| Área | Detecta | Remediación posible |
| --- | --- | --- |
| Accesibilidad | Archivo ilegible, permiso denegado, ruta offline, error de I/O. | Reintentar, reconectar, actualizar permisos, marcar inaccesible. |
| Integridad | Tamaño/hash cambió, archivo truncado, formato corrupto, sidecar inconsistente. | Rehash, restaurar desde backup, aislar para revisión. |
| Catálogo | Ubicación obsoleta, asset ausente, duplicado de registro, preview rota. | Reescanear, reconciliar, regenerar derivado. |
| Metadatos | EXIF/XMP ausente, fecha ambigua, GPS malformado, sidecar no asociado. | Proponer fuente de fecha, asociar sidecar, volver a extraer. |
| Organización | Nombre inválido/awkward, extensión discordante, colisión de destino, ruta demasiado larga. | Crear plan de rename/move con precondiciones. |
| Contenido auxiliar | `Thumbs.db`, `.directory`, temporales, basura conocida. | Cuarentena mediante plan de limpieza. |
| Duplicados | Exactos, probables o variantes en conflicto. | Delegar a la funcionalidad de duplicados; no auto-eliminar. |
| Operaciones/herramientas | Plan interrumpido, journal pendiente, fallo de extractor/preview. | Reanudar, rollback, reparar o abrir diagnóstico. |

Una condición se clasifica como `Info`, `Aviso`, `Error` o `Crítico`. La severidad
es explicable y se calcula por impacto: por ejemplo, un preview no generado es
un aviso, mientras que una operación a medias con riesgo de pérdida es crítica.

## Subfunciones

### 1. Health check incremental y completo

Cada scan publica señales de salud sin volver a leer innecesariamente toda la
biblioteca. El usuario puede lanzar:

- **Comprobación rápida:** disponibilidad de raíz, permisos, volumen/mount,
  coherencia de rutas y operaciones pendientes.
- **Validación de catálogo:** compara inventario actual con ubicaciones,
  derivados y metadatos persistidos.
- **Verificación de integridad:** reabre archivos seleccionados y recalcula
  hashes de acuerdo con la política; es más lenta y se programa para discos
  externos/red.
- **Diagnóstico completo:** ejecuta todas las reglas activas, con límites de
  E/S, exclusiones y progreso persistente.

Un check produce una revisión inmutable. La UI enseña qué reglas se ejecutaron,
cuándo, contra qué versión de catálogo y qué no se pudo comprobar. Un problema
no desaparece sólo porque la raíz esté offline: queda `No verificable` hasta que
exista evidencia nueva.

### 2. Bandeja de problemas y filtros

La pantalla central agrupa por severidad, raíz, tipo, fecha, estado y acción
recomendada. Cada fila muestra evidencia mínima: asset/ruta redactable, regla,
primer/último momento observado, revisión de scan, impacto, confianza y un
enlace a detalle. Los filtros permiten aislar “bloquea organizar”, “requiere
backup”, “solo derivados” o “repetido tras reintento”.

Un problema puede marcarse como `Resuelto`, `Ignorado temporalmente`,
`Ignorado por regla` o `Necesita revisión`. Ignorar requiere motivo y fecha de
revisión opcional; no borra evidencia ni suprime nuevas ocurrencias materialmente
distintas. Las exclusiones globales requieren una pantalla separada y explican
cuántos diagnósticos dejarán de producir.

### 3. Detalle y evidencia reproducible

El detalle conserva el identificador estable de asset/location, ruta mostrada
o redactada, regla y versión, estado del filesystem, huellas/tamaños conocidos,
metadatos relevantes, mensajes de herramienta y un historial cronológico. La
evidencia debe bastar para repetir el diagnóstico, pero jamás almacenar secretos
ni copiar el contenido completo de fotografías a los logs.

Para casos ambiguos se ofrecen comparaciones: metadata de archivo vs sidecar vs
nombre vs fecha de filesystem; hash previo vs actual; ubicación esperada vs
observada; y preview/derivado vs original. La UI distingue hechos observados de
inferencias y no propone una “reparación segura” si falta una fuente fiable.

### 4. Saneamiento mediante planes reversibles

Desde uno o varios diagnósticos, `Crear plan de corrección` agrupa acciones
compatibles y exige revisión. Acciones admitidas incluyen:

- regenerar previews, thumbnails, índices y otros derivados regenerables;
- reextraer metadatos y reparar asociaciones de sidecar sin modificar el
  original, salvo aprobación de una escritura explícita;
- renombrar, corregir extensión o reubicar según una regla visible;
- mover temporales y archivos auxiliares a cuarentena;
- restaurar un archivo corrupto o ausente desde un snapshot verificado;
- reconciliar el catálogo con cambios ya ocurridos fuera de Heimdall.

El plan incluye precondiciones (ruta/volumen, hash/tamaño, permisos, espacio),
conflictos, impacto y estrategia de undo. Las acciones se ejecutan con staging,
verificación, journal y checkpoints. Si cambia la precondición, se omite el
item y el plan queda parcial; nunca se fuerza una reparación sobre evidencia
antigua. Las operaciones sobre originales requieren raíz `Managed` y políticas
de cuarentena válidas.

### 5. Recuperación de operaciones interrumpidas

Al abrir la app y antes de un nuevo plan, un monitor revisa journals incompletos.
La UI muestra claramente si hay archivos en staging, cuarentena o estado
desconocido y ofrece `Reanudar`, `Verificar`, `Deshacer` o `Exportar evidencia`.
No hace rollback automático si no puede confirmar el estado del filesystem.

Una operación de salud no puede corregir un journal de otro plan sin vincularlo
explícitamente. El usuario siempre ve cuál fue el plan original, quién/cuándo lo
aprobó localmente y las consecuencias de cada opción de recuperación.

### 6. Reglas y configuración

Las reglas tienen ID, versión, severidad por defecto, alcance, coste estimado y
remediaciones soportadas. Se puede activar/desactivar una regla por raíz, fijar
umbrales (por ejemplo, ruta máxima o edad de preview) y programar checks. Las
reglas críticas de seguridad/integridad no pueden ocultarse globalmente sin una
confirmación informada; seguirán disponibles en un chequeo manual.

Las reglas de archivos auxiliares son conservadoras y configurables. `Thumbs.db`,
`.DS_Store`, `.directory` y temporales sólo se proponen si la ubicación, el
nombre y el tipo coinciden; nunca se eliminan por extensión genérica.

## UX recomendada

El panel superior muestra una puntuación explicable por raíz y cuatro cifras:
críticos, errores, avisos e items no verificables. No debe sugerir que una
biblioteca “está sana” si un volumen relevante está desconectado: el estado se
denomina **estado incompleto**.

El recorrido recomendado es:

1. Elegir alcance y tipo de comprobación, con coste y última revisión visibles.
2. Revisar grupos de problemas, comenzar por los que bloquean backups/planes.
3. Abrir evidencia y decidir si corregir, ignorar con motivo o posponer.
4. Generar un plan de corrección y revisar rutas, cambios, cuarentena y undo.
5. Ejecutar y ver progreso por item; revisar resultados y volver a verificar.

La UI separa `Corregir catálogo` (reversible local, sin tocar original) de
`Corregir archivos` (operación externa con mayor confirmación). Muestra previews
de rename/ruta, no sólo la regla, y enlaza a documentación cuando una reparación
requiere intervención manual.

## Local, mounts y agentes SSH

Para raíces locales o montadas, se utiliza el filesystem expuesto por el SO. Se
contemplan `/Volumes` de macOS; `/mnt`, `/media`, GVFS y FUSE de Linux; y letras,
UNC (`\\\\servidor\\Fotos`) y unidades asignadas en Windows. Diagnóstico registra
las capacidades reales: lectura, escritura, case sensitivity, timestamps,
symlinks/junctions, rename atómico y espacio. Nunca confunde una unidad
desconectada con archivos borrados.

En Windows, un proceso en segundo plano puede no ver una unidad mapeada por otra
sesión; se informa y se recomienda UNC o servicio/agente con identidad de acceso
documentada. macOS/Linux validan el mount para el mismo usuario que correrá el
check programado. Timeouts de red dejan resultados `No verificable`, no fallos
de integridad.

Para SSH, el cliente solicita al **agente Heimdall** checks y remediaciones
declaradas por protocolo versionado, no comandos shell. El agente trabaja sólo
en raíces permitidas, devuelve hallazgos/evidencia/health snapshots y realiza
planes en el servidor junto a sus datos. Así se evita transferir originales al
cliente sólo para comprobarlos. Host key fijada, permisos mínimos y referencias
a credenciales de almacén seguro son obligatorios.

## Modelo de dominio

| Entidad | Campos esenciales |
| --- | --- |
| `HealthCheckRun` | ID, raíz/alcance, reglas/versiones, inicio/fin, estado, snapshot de capacidades. |
| `HealthFinding` | ID, regla, severidad, asset/location, evidencia, confianza, primera/última vez, estado. |
| `FindingEvidence` | Revision scan, ruta redactada, hashes/tamaños, observaciones, error externo y timestamps. |
| `RemediationProposal` | Finding IDs, acciones, precondiciones, impacto, riesgos y estrategia de undo. |
| `HealthRemediationPlan` | Propuesta aprobada, operaciones, cuarentena, journal, resultado y vínculo a run. |
| `HealthRuleConfig` | Regla/version, raíz, activa, umbrales, schedule, exclusiones y overrides. |
| `HealthSnapshot` | Conexión, identidad de volumen/host, permisos, capacidad y latencia fechados. |

Los hallazgos se deduplican por regla, asset/location y firma de evidencia, pero
conservan recurrencias. El modelo nunca almacena contraseñas, claves SSH ni
contenido binario de fotos.

## Seguridad y privacidad

- Acciones sólo dentro de rutas autorizadas, canonicalizadas y protegidas contra
  traversal, symlink escape y cambios de mount durante ejecución.
- No se modifican originales desde una raíz `CatalogOnly`/`PlanOnly`; las raíces
  gestionadas requieren escritura comprobada y cuarentena operativa.
- Operaciones de red/SSH se cancelan de forma segura al perder identidad de host,
  volumen o permisos y deben validarse de nuevo antes de continuar.
- Diagnósticos y exports redactan rutas por defecto, limitan logs y no contienen
  secretos ni imágenes; detalle extendido es una opción local explícita.
- Las reparaciones de metadata preservan original/sidecar anterior para undo o
  generan un nuevo sidecar, según la política aprobada.

## Criterios de aceptación

- La vista identifica y explica accesibilidad, integridad, catálogo, metadata,
  nombres, derivados, auxiliares y operaciones incompletas sin modificar nada.
- Cada finding conserva evidencia, revisión y estado suficiente para reproducir
  el diagnóstico, con deduplicación y recurrencia visibles.
- Cualquier saneamiento produce plan, preflight, approval, staging/journal y
  undo; la limpieza de auxiliares usa cuarentena.
- Una raíz/mount offline queda `No verificable`, conserva catálogo y nunca
  produce falsos “archivos desaparecidos”; macOS, Linux y Windows están cubiertos.
- Checks SSH usan agente limitado a rutas autorizadas, con host key verificada,
  y no ejecutan shell arbitrario.
- Recuperación de journals incompletos exige decisión informada y no ejecuta
  rollback ciego.
- Logs, evidencia y exportaciones no filtran credenciales ni contenido sensible.
