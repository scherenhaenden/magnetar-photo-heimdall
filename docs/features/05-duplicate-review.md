# Revisión de candidatos duplicados

Esta funcionalidad encuentra, explica y permite resolver copias redundantes sin
perder fotografías. Su resultado no es un borrado: es un **plan de limpieza
reversible** que el usuario revisa y aprueba. Un grupo se considera duplicado
exacto únicamente cuando todas sus ubicaciones tienen el mismo hash completo de
contenido BLAKE3. Coincidir en nombre, tamaño, fecha, imagen visible o hash
rápido nunca basta para borrar ni para ocultar un archivo.

## Lo que ve el usuario

- Un resumen por biblioteca, origen y espacio potencialmente recuperable:
  grupos pendientes, copias exactas, grupos descartados y acciones ya
  ejecutadas que todavía se pueden deshacer.
- Una lista de grupos con una miniatura representativa, número de copias,
  tamaño recuperable, estado de verificación y señales disponibles. Se puede
  filtrar por biblioteca, carpeta, formato, fecha, tamaño, origen local/remoto
  y confianza; ordenar por ahorro, fecha o número de copias; y buscar por ruta
  o nombre.
- Una vista de comparación donde cada miembro muestra preview, ruta completa,
  volumen/servidor, tamaño, dimensiones, fecha de captura/modificación,
  formato, metadatos relevantes, hash rápido y hash BLAKE3 completo, además de
  avisos (sin acceso, cambió desde el escaneo, hash pendiente, RAW asociado,
  etc.). Los datos que son evidencia se diferencian visualmente de las meras
  pistas.
- Acciones explícitas: elegir un *keeper*, excluir un miembro, marcar el grupo
  como «no son duplicados», posponerlo, solicitar verificación completa o crear
  un plan. Las decisiones manuales se conservan y se pueden revocar.

## Descubrimiento y evidencia

1. El escáner normaliza identidad de archivo y recoge tamaño, fechas,
   extensión, dimensiones y una huella rápida de bloques del archivo. Agrupa
   primero por tamaño para ahorrar I/O; es solo un prefiltro.
2. Para candidatos del mismo tamaño calcula una huella rápida versionada. Si
   coincide, agenda BLAKE3 completo, preferentemente en segundo plano y con
   límites de concurrencia por volumen o conexión.
3. Solo hashes completos iguales forman un grupo de duplicados exactos. Si
   difieren, el grupo se descarta automáticamente como igualdad; puede quedar
   como «similares visualmente» si en el futuro se habilita ese análisis, siempre
   separado y sin acciones destructivas automáticas.
4. Antes de planificar o ejecutar se vuelve a comprobar identidad, tamaño,
   mtime y, cuando corresponda, BLAKE3. Un archivo cambiado, sustituido o no
   accesible invalida esa operación y requiere reescaneo.

Los algoritmos, versiones de hash, hora de cálculo y resultado se guardan como
evidencia auditable. El producto no reutiliza la lógica antigua de `Photos`
que agrupaba por longitud, calculaba MD5 y borraba directamente una copia.

## Elegir el keeper

Heimdall propone un keeper, nunca lo impone. La recomendación debe explicar sus
criterios y permitir cambiar cada uno: conservar el archivo en biblioteca
preferida, el que tenga mejor metadato/fecha GPS, mayor resolución o profundidad
de color, formato preferido, ruta más estable, copia ya respaldada, o el elegido
manualmente. Por defecto evita seleccionar una ubicación remota frágil si existe
una copia equivalente en la biblioteca principal, pero no cambia nada sin la
decisión del usuario.

El usuario puede mantener más de una copia por política (por ejemplo, una local
y una de respaldo), excluir carpetas concretas y fijar reglas de keeper por
biblioteca. Las reglas solo generan propuestas: la pantalla siempre enseña qué
miembros se conservarán y cuáles entrarán en cuarentena.

## RAW, JPEG y archivos acompañantes

Un RAW y su JPEG, TIFF, XMP sidecar, Live Photo, vídeo asociado o archivo de
edición no son copias equivalentes aunque compartan nombre, fecha o apariencia.
La revisión los representa como una **familia de activos** y protege la relación
entre ellos. Un hash idéntico en dos RAW sí puede ser duplicado exacto; un RAW y
un JPEG no lo son por definición.

Antes de proponer una acción, el sistema detecta pares y sidecars por reglas de
nombre, metadatos y relaciones importadas. Si se mueve o pone en cuarentena una
copia RAW, también se advierte qué acompañantes quedan en la otra ubicación. La
política por defecto impide separar un sidecar de su principal y exige una
decisión explícita para cualquier excepción.

## Bibliotecas locales, montadas y remotas

Cada ubicación conserva su procedencia: ruta local, volumen montado, recurso de
red (SMB/NFS/AFP donde aplique) o agente SSH. En macOS y Linux, los mounts se
tratan como raíces de biblioteca normales pero se identifican con volumen,
UUID/cuando esté disponible, punto de montaje y disponibilidad. En Windows se
admiten letras de unidad, UNC (`\\servidor\\recurso`) y unidades de red; las rutas
se guardan junto con un identificador de proveedor/volumen para que cambiar la
letra no convierta artificialmente todas las fotos en nuevas.

Para SSH, el cliente no necesita montar el servidor. Se conecta a un agente
Heimdall compatible que escanea y calcula hashes **en el servidor**, devuelve un
inventario paginado y evidencia firmada/versionada, y ejecuta únicamente planes
confirmados. El agente publica capacidades (rutas autorizadas, BLAKE3, previews,
cuarentena, espacio disponible), mantiene un journal local y nunca acepta rutas
ni comandos shell arbitrarios. Las credenciales permanecen en el almacén seguro
del sistema; la clave del host se fija o confirma conforme a la política del
usuario. Si un mount o servidor está desconectado, los grupos quedan incompletos,
no se infiere que la copia desapareció y no se ejecutan operaciones sobre él.

## Plan, ejecución y deshacer

Al aprobar un grupo, Heimdall crea una operación declarativa con keeper,
miembros candidatos, precondiciones y destino de cuarentena, no un comando de
sistema. La ejecución por ubicación es:

1. Revalidar evidencia e identidad de cada archivo.
2. Si hay que preservar una copia antes de limpiar, copiar y comprobar BLAKE3.
3. Mover la copia no keeper a una cuarentena del mismo volumen cuando sea
   posible, conservando ruta original y metadatos en el journal.
4. Registrar resultado, errores y correlación local/remota; actualizar el
   catálogo solo después de confirmar la operación.

No hay borrado permanente en este flujo. Deshacer restaura desde cuarentena a
su ruta original o, si esta está ocupada, propone una ruta alternativa sin
sobrescribir. La purga de cuarentena es una función distinta con retención,
espacio afectado y confirmación reforzada. En un servidor SSH, el agente aplica
el mismo protocolo y devuelve el journal para que el cliente muestre un único
historial coherente.

## Modelo de datos recomendado

- `DuplicateCandidate`: versión del detector, estado (`pending`, `verifying`,
  `exact`, `not_duplicate`, `stale`, `incomplete`), activos miembros y señales.
- `ContentEvidence`: activo/ubicación, algoritmo y versión, hash, tamaño,
  identidad de archivo, fecha de cálculo y validez.
- `AssetFamily` y `AssetRelationship`: principal, RAW/JPEG, sidecar, Live Photo
  u otra asociación; impiden operaciones que rompan la familia por defecto.
- `DuplicateDecision`: keeper(s), exclusiones, usuario/regla que tomó la
  decisión, motivo y fecha.
- `CleanupPlan`, `PlanItem` y `OperationJournal`: precondiciones, pasos,
  ubicación de cuarentena, resultados, deshacer y correlación del agente remoto.

Los estados y decisiones son independientes de la ruta: una ruta puede cambiar,
pero la evidencia debe volver a validarse antes de cualquier operación.

## Rendimiento y fiabilidad

El análisis se reanuda tras interrupciones, limita lectura en discos lentos,
montajes y SSH, prioriza las fotos visibles o grupos con más ahorro, y permite
pausar. Las miniaturas y hashes se almacenan en caché con versión e identidad de
origen, pero se invalidan ante cambios. Las transferencias SSH son de
metadatos/hashes; previews se solicitan bajo demanda y con límites de tamaño.
La UI comunica progreso real (archivos, bytes, grupos confirmados), coste
estimado y errores recuperables sin bloquear la biblioteca completa.
