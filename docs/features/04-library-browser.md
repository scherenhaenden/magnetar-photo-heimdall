# 04 — Explorar y encontrar activos

## Propósito

La pantalla de biblioteca es la superficie principal de inspección de Heimdall: permite ver fotos y vídeos sin alterar los originales, entender dónde existe cada activo y seleccionarlo para revisión, organización o exportación. La experiencia debe ser equivalente para disco local, NAS montado, volumen USB, ubicación remota indexada y servidor SSH.

No es un explorador de archivos que opere por accidente. El navegador consulta el catálogo local de Heimdall y cualquier cambio de archivos crea o alimenta un plan explícito, revisable y reversible.

## Conceptos que muestra

- **Activo**: identidad lógica de una foto, vídeo o archivo relacionado.
- **Ubicación**: copia concreta del activo, con origen (`local`, `mount`, `ssh-agent`, `offline`) y disponibilidad.
- **Raíz**: carpeta o endpoint registrado que una fuente puede escanear.
- **Colección**: vista guardada o conjunto gestionado; no mueve archivos por sí misma.
- **Diagnóstico**: problema visible y filtrable: archivo ausente, metadatos dañados, miniatura pendiente o credenciales SSH no disponibles.

Un activo puede tener varias ubicaciones. La tarjeta indica estado agregado y el detalle presenta cada copia; dos rutas con el mismo nombre nunca se consideran iguales sin identidad o hash verificable.

## Flujo UX

1. El usuario elige una raíz, colección, búsqueda guardada o la vista global.
2. Heimdall consulta el índice local y muestra inmediatamente las miniaturas cacheadas; los datos lentos se completan en segundo plano.
3. El usuario busca, filtra, ordena y selecciona uno o varios activos.
4. El panel de detalle permite inspeccionar preview, metadatos, ubicaciones, duplicados, diagnósticos e historial.
5. Las acciones de lectura ocurren directamente; las de contenido o ubicación abren un plan previsualizable y aprobable.

La interfaz conserva filtros, orden, zoom, selección y posición al abrir/cerrar detalles. Las actualizaciones de índice comunican progreso y admiten cancelación segura sin bloquear la navegación.

## Vistas y subfunciones

### Rejilla virtualizada

La vista predeterminada es una rejilla de miniaturas virtualizada: crea tarjetas sólo para el área visible y una zona de anticipación. Cada tarjeta muestra miniatura, tipo/RAW/vídeo, fecha efectiva, nombre, estado de revisión y señales para duplicado, offline, conflicto o diagnóstico. Soporta zoom de tarjeta, teclado, multiselección por rango, selección persistente entre páginas y menú contextual accesible.

Las miniaturas viven exclusivamente en la caché de Heimdall. Si faltan, aparecen placeholders estables y se genera una cola priorizada para elementos visibles; no se leen masivamente originales remotos para llenar la rejilla. Archivos ilegibles o no soportados siguen apareciendo como diagnósticos.

### Lista, grupos y colecciones

La misma consulta puede presentarse como lista compacta de auditoría, con columnas configurables: fecha, nombre, ruta, tamaño, cámara, tipo, hash, estado y ubicación. Se agrupa por día, mes, evento estimado, cámara, tipo, carpeta, raíz, estado de revisión o duplicados; los grupos son plegables y muestran conteos sin cambiar la consulta base.

Las colecciones manuales añaden referencias del catálogo. Las colecciones inteligentes guardan una consulta, se actualizan al cambiar el índice y explican los filtros que las componen.

### Detalle y comparación

El detalle muestra preview, metadatos normalizados y EXIF/XMP disponible, ubicaciones, hashes, sidecars, diagnósticos, relaciones de duplicado y operaciones del journal. El RAW se previsualiza mediante derivados/caché; si falta, se solicita bajo demanda sin tocar el original. Para vídeos muestra póster y datos técnicos antes de reproducción opcional.

La comparación coloca dos o más activos lado a lado, sincroniza zoom y permite alternar metadatos o elegir candidatos a conservar. No marca ni elimina nada automáticamente.

## Búsqueda, filtros y orden

La búsqueda admite nombre, extensión, ruta relativa, etiquetas, colección, cámara y metadatos indexados, con coincidencia parcial e insensible a mayúsculas. Si una consulta no cubre datos aún sin indexar, la interfaz lo indica. Las búsquedas complejas se pueden guardar.

Filtros combinables:

- fecha y fuente de fecha: captura, creación o importación;
- tipo, extensión, foto/vídeo/RAW, tamaño y dimensiones;
- cámara, lente, ISO, apertura, focal, orientación y GPS;
- raíz, carpeta, fuente, disponibilidad y ubicación;
- estado de metadatos, miniatura, indexación y diagnósticos;
- revisión, favoritos, etiquetas y colecciones;
- grupo/similitud de duplicados y candidato a conservar;
- estado operacional: sin plan, en plan, cuarentenado o fallo.

Se ordena por fecha efectiva, nombre natural, ruta, importación, tamaño, dimensiones, cámara, puntuación o similitud. Toda orden incluye el identificador de activo como desempate determinista, evitando saltos al cargar más resultados. Se muestra siempre la fecha efectiva y su origen.

## Acceso local, montado y remoto

### Local y mounts

En macOS y Linux una raíz puede ser una carpeta local, disco externo, SMB, NFS, AFP heredado o cualquier volumen ya montado por el sistema. Heimdall guarda identificador de volumen y ruta canónica además de la visible, detecta desmontajes y conserva el catálogo como `offline`. Navegar lo indexado sigue funcionando; abrir original o crear una miniatura nueva explica que se debe montar el volumen. Heimdall no conecta ni modifica montajes por sí solo.

En Windows se soportan carpetas locales, unidades con letra y rutas UNC (`\\servidor\\recurso`). Las unidades mapeadas no son identidad duradera: se conserva UNC/identificador de origen cuando existe. Se puede usar SSH/SFTP para servidores que no expongan un recurso montado. No se requiere FUSE, SSHFS ni drivers; se aprovechan si el usuario ya los configuró.

### SSH y agente remoto

Una fuente SSH define endpoint, host verificado, credencial en almacén seguro, rutas autorizadas y modo: **SFTP/SSH directo** o **agente Heimdall remoto**. Las claves privadas no se guardan en SQLite y un cambio de huella de host no se acepta automáticamente.

El modo directo lista y lee de forma limitada por SFTP/SSH para indexar u obtener derivados, con concurrencia, ancho de banda y pausas configurables. Es útil para bibliotecas pequeñas, aunque la latencia limita previews.

El agente remoto es un servicio instalado y aprobado en el servidor. Escanea, extrae metadatos, calcula hashes y genera miniaturas en la propia máquina; por SSH expone un protocolo versionado, autenticado y tipado. Envía al cliente registros, cambios, diagnósticos y derivados pedidos; el catálogo y la UI permanecen locales. No ejecuta comandos arbitrarios: acepta operaciones tipadas dentro de raíces autorizadas, con journal remoto. Para organizar archivos remotos, el cliente prepara y aprueba el plan; el agente lo revalida, lo ejecuta localmente y devuelve estados verificables.

Una desconexión deja la biblioteca navegable con caché. Las operaciones pendientes se ven como tales; no se reintentan opacamente y requieren reconexión explícita o una política de reanudación previamente aprobada.

## Acciones

Acciones de lectura: revelar/abrir original disponible, abrir carpeta, copiar ruta/URI/ID, copiar metadatos, actualizar índice, regenerar miniatura, inspeccionar hash y abrir diagnósticos. En remoto, «revelar» utiliza la capacidad expuesta —por ejemplo copiar ruta— y sólo puede solicitar al agente abrir una ruta si fue habilitado explícitamente.

Acciones de organización: añadir al plan de mover, copiar, renombrar, agrupar, consolidar duplicados, exportar o enviar a Photo Culler. La previsualización enumera origen, destino, colisiones, bytes, montaje/conexión necesaria y efecto sobre sidecars. No existe borrado directo: descartar una copia crea un paso de cuarentena y preserva al menos una copia verificada según política.

Etiquetas, favoritos, revisión y colecciones son cambios locales y reversibles. Escribir sidecars/XMP será siempre un plan independiente, nunca efecto colateral de editar una etiqueta.

## Modelo, rendimiento y consistencia

SQLite indexa fecha efectiva, raíz, ruta normalizada, tipo, disponibilidad, estado y duplicados. La vista usa paginación por cursor/keyset, no offsets profundos, para mantener fluidez con cientos de miles de activos. Conteos costosos se calculan bajo demanda y se etiquetan como estimados hasta entonces.

Escaneos publican cambios incrementales sin reordenar bruscamente la vista: elementos nuevos se señalan y se integran mediante refresco si afectarían la selección. Metadatos, capacidades y derivados se cachean con versión/caducidad. Descargar un original remoto para preview completo necesita petición explícita, muestra tamaño/progreso y respeta el límite de caché local.

## Seguridad y límites

- La exploración es de sólo lectura; ver no modifica originales.
- Las credenciales SSH están en Keychain, Credential Manager o equivalente; SQLite conserva sólo referencias no secretas.
- Se verifica el host y las rutas autorizadas; se bloquea escapar de la raíz, incluidos enlaces simbólicos ambiguos, registrando las resoluciones.
- Previews y miniaturas se decodifican con límites de tamaño, tiempo y memoria; archivos malformados se diagnostican.
- Cualquier cambio local o remoto sigue plan → aprobación → verificación → cuarentena/journal → deshacer, indicando claramente máquina y volumen afectados.

## Criterios de aceptación

- Con 100.000 registros, la rejilla se desplaza y filtra sin crear controles para todo el conjunto ni cargar originales invisibles.
- Una raíz desconectada sigue siendo consultable y señala exactamente las acciones que necesitan reconexión.
- Un activo con varias ubicaciones muestra cada copia y su verificación sin confundirla con una copia no comprobada.
- La consulta funciona para local, mount, UNC y SSH; cualquier límite de capacidad se comunica de forma explícita.
- Ninguna acción de rejilla puede borrar, mover o renombrar sin un plan revisable y registrado.
