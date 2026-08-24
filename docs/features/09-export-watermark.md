# 09 — Exportar copias redimensionadas y con marca de agua

## Objetivo y garantías

Exportar crea derivados para compartir o entregar desde una selección, álbum o búsqueda. Nunca modifica el original, su ubicación ni los metadatos de origen. Cada salida queda trazada hasta su activo, ubicación, receta, motor y destino. La lógica de PhotoLibrarizer de redimensionar y colocar un logo se conserva, pero el motor no ejecuta shell arbitrario ni puede sobrescribir silenciosamente.

## Flujo de usuario

1. Desde galería, detalle, álbum, duplicados o plan, el usuario pulsa **Exportar**. Se congela un manifiesto, por lo que cambios posteriores de filtro no alteran el lote.
2. El asistente muestra cantidad, formatos, espacio estimado y ejemplos. RAW, vídeo o formatos no compatibles se enumeran con su tratamiento previsto.
3. El usuario selecciona preset, destino y nombres. Los ajustes avanzados agrupan tamaño/formato, apariencia, metadatos y colisiones.
4. La previsualización renderiza una muestra horizontal, vertical y RAW si procede; compara original/salida a tamaño real, con recorte, orientación y marca.
5. La confirmación enumera creaciones, omisiones y colisiones. El trabajo es visible, se puede pausar, cancelar y reintentar. Cancelar no borra resultados verificados sin consentimiento.
6. Un informe permite abrir destino, copiar errores y consultar el journal. Las salidas gestionadas se eliminan únicamente con **Eliminar exportación**, nunca por limpieza automática.

## Presets y receta reproducible

Un preset es una receta nombrada, como «Instagram 2048», «Entrega JPG» o «Prueba con logo». Editarlo genera una nueva revisión; cada trabajo almacena el identificador y JSON exacto utilizados. La receta incluye:

- formato, calidad y límites;
- ajuste (`encajar`, `rellenar y recortar`, `lado largo`, `tamaño exacto`), interpolación y nitidez;
- color, transparencia y metadatos;
- plantilla de rutas/nombres, extensión y colisiones;
- una o más capas de marca de agua, con recursos versionados;
- destino o referencia sustituible de destino.

Los presets no contienen secretos SSH. Llaves y credenciales se guardan en el almacén seguro del sistema o el agente SSH; la receta solo referencia un perfil.

## Dimensiones, formatos, orientación y color

El modo inicial es **encajar sin ampliar**: conserva proporción, aplica orientación EXIF y limita el lado largo o una caja máxima. Se puede permitir ampliación, seleccionar recorte centrado o focal, fijar píxeles/DPI y revisar el resultado por elemento.

JPEG, PNG, WebP y TIFF son formatos iniciales. HEIC/AVIF se ofrecen solo cuando el motor puede codificarlos. RAW se revela con el decodificador/perfil elegido y se exporta a otro formato; nunca se reescribe como RAW. Archivos sin soporte quedan como error u omisión explícita.

La UI ofrece calidad numérica o perfiles pequeño/equilibrado/máxima y estima peso. PNG, WebP y TIFF pueden conservar alpha; JPEG se compone sobre el fondo definido. El renderizador normaliza píxeles según EXIF antes de escalar y escribe orientación normal para impedir fotos verticales tumbadas.

Color: conservar perfil cuando se pueda, convertir a sRGB (predeterminado web), u omitir perfil con advertencia. Metadatos: conservar seguros (fecha, autor, copyright), conservar todo, quitar GPS, quitar personales o quitar todo. Rutas internas, notas y etiquetas privadas se excluyen por defecto.

## Marcas de agua y capas

Una capa puede ser PNG/SVG con alpha o texto. Define contenido, opacidad, color, escala relativa al lado corto o píxeles, margen, rotación, una de nueve alineaciones, mosaico, sombra y contorno. La esquina inferior derecha es el valor inicial heredado, pero la vista previa siempre enseña el resultado para verticales y horizontales.

Las capas se aplican tras orientación, color y redimensionado, para escalar con el derivado. Texto admite campos controlados (`{copyright}`, `{author}`, `{year}`). El recurso de logo se copia a la receta o se identifica por hash, manteniendo auditoría si se reemplaza el fichero original.

## Destinos: local, montado y SSH

Todo destino pasa una comprobación de escritura antes del lote.

- **Carpeta local**: ruta en el equipo cliente.
- **Ruta montada**: NAS, USB, recurso de red o carpeta sincronizada expuesta como ruta local. macOS/Linux usan puntos de montaje; Windows admite letras de unidad, rutas UNC (`\\servidor\\recurso`) y unidades asignadas. Heimdall confirma que está montada en el cliente actual.
- **Servidor SSH con agente Heimdall**: el cliente envía manifiesto y receta. El agente lee/escribe solo sus raíces autorizadas, renderiza localmente en el servidor y devuelve progreso, resultados, hashes y diagnósticos. Es el modo recomendado cuando la biblioteca está ya en el servidor.
- **SSH sin agente**: limitado a transferencia y operaciones expresamente compatibles. Renderizar en remoto exige un agente compatible y jamás usa una cadena de shell arbitraria.

Para SSH se muestran rutas del servidor, espacio informado por agente, versión/capacidades de motor y huella de host. La primera conexión exige confirmar clave de host y un cambio posterior alerta. Se priorizan llaves del sistema, SSH key o agente SSH; no se guardan contraseñas en catálogo. El usuario decide si las salidas se indexan como derivados remotos o como salidas externas; rutas remotas no se interpretan como locales.

## Nombres, rutas y colisiones

La plantilla genera rutas con campos controlados, por ejemplo `{captureDate:yyyy/MM}/{originalStem}_{sequence}`. Cada segmento se sanea según el destino: caracteres reservados, dispositivos Windows, longitud y sensibilidad a mayúsculas. Están prohibidos `..`, rutas absolutas y expansión de comandos.

Antes de renderizar se calcula manifiesto final y colisiones del lote/destino. Las políticas son: detener y preguntar (predeterminada), añadir sufijo estable, omitir existente, reemplazar solo una salida gestionada con mismo origen/receta, o crear carpeta por lote. Reemplazar exige confirmación adicional y usa cuarentena si está disponible. Nunca reemplaza un original.

## Ejecución, integridad y rendimiento

Cada resultado se crea temporalmente junto al destino —o en el área del agente—, se verifica, registra tamaño/hash relevante y se publica por renombrado atómico. Si el sistema de archivos no lo soporta, el informe lo explica y aplica un protocolo conservador.

La cola limita paralelismo por CPU, RAM, disco y capacidad remota; evita releer el mismo original y deja miniaturas de progreso en baja prioridad. Reanudar compara manifiesto, receta, hash de entrada y resultados comprobados; no reutiliza una salida si cambió original, preset, logo o motor relevante.

## Modelo, gestión y seguridad

`ExportJob` guarda manifiesto, estado, momento, destino, receta, motor, totales y errores. `ExportItem` enlaza activo y ubicación origen, ruta temporal/final, dimensiones, hash, estado y diagnóstico. Cuando corresponde se crea `DerivedAsset` con relación *derivado de*, evitando contaminar la detección de duplicados como originales.

El journal registra creación, publicación, sustitución, omisión y eliminación gestionada. El historial permite repetir como nuevo trabajo, localizar origen, verificar integridad y borrar solo ficheros identificados como gestionados. Un archivo ajeno no se elimina aunque comparta nombre.

- No se modifican originales ni metadatos de origen.
- Cliente y agente validan rutas contra el destino y las raíces autorizadas; el agente aplica su propia autorización.
- Secretos, llaves y tokens no figuran en recetas serializadas, informes ni logs.
- Previsualizaciones no salen del equipo salvo procesamiento remoto elegido explícitamente.
- Los errores se clasifican por lectura, decodificación, color, escritura, conexión, permiso o colisión; éxito parcial no se presenta como total.

## Criterios de aceptación

- Un JPEG vertical con EXIF exporta vertical, respeta caja y marca visual seleccionada.
- La misma receta produce resultados equivalentes y trazables en local, montaje y agente SSH compatible.
- Una colisión no sobrescribe por defecto y se decide antes de renderizar.
- Interrumpir no publica temporales; reanudar no reprocesa salidas verificadas.
- El historial localiza el origen y elimina una exportación gestionada sin tocar el original.
