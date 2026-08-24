# 10 — Copias de seguridad, espejos y restauración

## Propósito y límites

Esta funcionalidad protege bibliotecas mediante copias verificadas y
recuperables. Cubre una copia histórica (**snapshot**), un espejo
unidireccional (**mirror**) y restauración selectiva o completa. Conserva el
resultado de *mirror backup*, *reduced-size mirror* y *backup-to-route* de los
productos previos, pero una copia terminada nunca equivale a un backup válido.

Un backup es una instantánea verificable de assets y sidecars. Un mirror añade
y actualiza el destino desde un origen, pero no elimina automáticamente sus
excedentes. Las retiradas son planes separados, aprobados, con cuarentena y
journal. No es sincronización bidireccional ni sustituye una estrategia 3-2-1.

| Concepto | Definición |
| --- | --- |
| Origen | Raíz, colección, filtros o lista fija de assets catalogados. |
| Destino | Ubicación configurada como `Backup`, local, montada o remota. |
| Perfil | Política reutilizable de alcance, verificación, estructura y retención. |
| Ejecución | Corrida concreta con manifiesto inmutable y journal. |
| Snapshot | Resultado correcto, identificable y restaurable de una ejecución. |
| Restore plan | Plan revisable para reponer un snapshot; nunca un copiar de vuelta ciego. |

## Subfunciones

### 1. Perfiles de backup y mirror

En `Protección → Nuevo backup`, el usuario escoge fuentes, destino, plantilla
de estructura, sidecars (XMP, JSON, RAW+JPEG asociados), exclusiones,
programación, límites de E/S y retención. Selecciona:

- **Snapshot:** conserva cada instantánea según retención.
- **Mirror seguro:** mantiene una copia actualizada sin limpieza implícita.
- **Copia de derivados:** puede producir JPEG/previews reducidos; se etiqueta
  como derivado y nunca se considera backup del original.

La política de verificación ofrece tamaño/fecha, hash parcial o hash completo.
La UI explica con precisión si se protegen originales, sidecars, derivados o
una combinación.

### 2. Preflight y plan

Antes de transferir, se compara origen/destino y se muestra: `nuevos`,
`cambiados`, `ya verificados`, `omitidos`, `conflictos` y `no accesibles`, además
de capacidad, staging requerido, bytes, tiempo aproximado y método de hash.
Se bloquea un destino dentro de una fuente escaneada salvo exclusión comprobada,
y se advierte si es la misma ubicación física que la única fuente protegida.

El usuario puede ejecutar, guardar o programar el plan. Cambios de identidad,
salud, selección, configuración o precondiciones lo invalidan y se verifican de
nuevo antes de ejecutarlo.

### 3. Copia verificable, atómica y reanudable

Cada archivo se copia a staging o nombre temporal. Según la política, Heimdall
calcula y compara hashes; sólo entonces lo publica atómicamente cuando el
filesystem lo permite. Journal y manifiesto incremental permiten reanudar tras
un corte sin considerar archivos incompletos como protegidos.

Por defecto se usa BLAKE3; SHA-256 se habilita para manifiesto interoperable o
máxima confianza. Cada entrada conserva algoritmo/hash, bytes, timestamps, ruta
relativa, tipo, sidecars y resultado. Los hashes existentes sólo se reutilizan
si identidad, tamaño, mtime y política lo permiten.

Los estados por item son `Pendiente`, `Copiado sin verificar`, `Verificado`,
`Fallido`, `Conflicto` y `Cancelado`; una corrida concluye `Correcta`, `Correcta
con avisos`, `Parcial`, `Fallida` o `Cancelada`.

### 4. Mirror seguro y limpieza separada

El mirror sólo añade y actualiza cuando la política de conflicto aprobada lo
permite. Después puede ofrecer **Revisar excedentes**, que lista items exclusivos
del destino y permite conservarlos, excluirlos o crear un plan de limpieza. El
último mueve primero a cuarentena, verifica manifiesto y journaliza; exige una
aprobación independiente. No existe “espejo destructivo inmediato”.

### 5. Historial, auditoría y retención

Las tarjetas de perfil muestran último snapshot correcto, edad, cobertura,
capacidad, crecimiento, salud del destino y alertas. La ejecución permite
inspeccionar diferencial, manifiesto, hashes, errores, exclusiones y journal.
El manifiesto es inmutable después de cerrarse; reintentos son ejecuciones
nuevas vinculadas. Puede exportarse como JSON versionado sin secretos.

La retención admite últimas `N`, diarias, semanales, mensuales, edad y espacio.
Antes de expirar, verifica que no sea la única copia correcta de un asset ni
esté implicada en una restauración. Genera una propuesta: sólo tras aprobación
se mueven archivos exclusivos a cuarentena (o papelera/versionado declarado por
el destino). Assets referenciados por varios snapshots no se eliminan todavía.

### 6. Restauración

Desde un snapshot, asset o búsqueda del catálogo, el usuario elige recuperar a
la ruta original válida, carpeta de recuperación o nueva raíz. Ve fuente,
hash, ruta original, sidecars y conflictos antes de aprobar. El restore copia a
staging, verifica contra manifiesto, publica y journaliza. Sobrescribir exige
elección explícita y backup/cuarentena previa del destino cuando sea posible.
Luego puede reescanear para reconciliar catálogo.

## UX recomendada

La pantalla presenta un semáforo por perfil: `Protegido`, `Vence pronto`, `Sin
backup correcto`, `Destino offline`, `Atención requerida`. Debe bastar una frase
para saber qué está protegido, dónde y cuándo se verificó.

El asistente guía: elegir contenido; validar destino; seleccionar modo y hash;
revisar coste, diferencial y riesgos; ejecutar/guardar; y seguir progreso con
bytes, archivos, velocidad e incidencias. `Pausar` y `Cancelar` preservan lo
verificado. El resumen final distingue con claridad éxito completo de parcial y
propone prueba de restauración.

## Local, mounts y SSH

Los destinos locales, USB y montados se usan como filesystem: Heimdall no gestiona
SMB/NFS/AFP y aprovecha credenciales del SO. Reconoce `/Volumes/...` en macOS,
`/mnt`, `/media`, GVFS/FUSE en Linux, y letras, UNC (`\\\\host\\share`) o unidades
asignadas en Windows. Detecta desconexión, case-sensitivity, Unicode, permisos,
rename atómico y timestamps.

En Windows, las unidades de red asignadas pueden no existir para una tarea en
segundo plano; la UI recomienda UNC o agente/servicio con acceso documentado.
En macOS/Linux valida que el mount automático esté disponible para el usuario
que ejecutará la tarea.

SSH usa un **agente Heimdall**, nunca shell arbitrario. El agente recibe rutas
relativas autorizadas y precondiciones, trabaja con staging/journal junto a los
datos, calcula hashes y devuelve capacidades, progreso y manifiestos. Puede
copiar entre discos del servidor sin retransmitir originales al cliente.

Se fija la host key, se prefiere clave/certificado/agente del SO y los secretos
viven únicamente en Keychain, Secret Service/KWallet o Credential Manager. Sólo
se persiste una referencia opaca; host, password, claves o tokens nunca aparecen
en SQLite, URLs exportadas, manifests o logs. Un cambio de host key bloquea el
destino hasta revisión.

## Modelo de datos

| Entidad | Campos esenciales |
| --- | --- |
| `BackupProfile` | ID, nombre, fuentes, destino, modo, selección, política, schedule, estado. |
| `BackupPolicy` | Exclusiones, sidecars, estructura, hash, staging, límites y retención. |
| `BackupRun` | ID, perfil/revisión, inicio/fin, estado, health snapshots, resumen, journal. |
| `BackupManifest` | Versión, run ID, hashes, rutas, bytes, timestamps, enlaces y firma opcional. |
| `ManifestEntry` | Asset/location ID, origen/destino, hash, estado, error y metadata. |
| `RestorePlan` | Snapshot, destino, conflictos, precondiciones, aprobación y journal. |
| `RetentionProposal` | Snapshots, impacto, cuarentena, aprobación y periodo de undo. |

La identidad del destino incluye huella de volumen/recurso y URI canonicalizada
sin credenciales; la ruta mostrada se conserva por separado.

## Seguridad e integridad

- Toda escritura queda dentro de rutas autorizadas, protegida contra traversal,
  symlink escape y cambios de mount durante la operación.
- Una corrida no es correcta hasta verificar todos los items requeridos; omisiones
  y avisos se cuantifican en cobertura.
- Limpieza y overwrite siempre pasan por plan, cuarentena y journal.
- Cambios de identidad de volumen/host, permisos o capacidad obligan a validar
  antes de reanudar.
- Los manifests pueden firmarse con clave local del almacén seguro; su estado se
  muestra al restaurar.
- Logs redactan rutas y no incluyen nombres salvo diagnóstico local explícito.

## Criterios de aceptación

- Una ejecución interrumpida se reanuda sin contar temporales/no verificados.
- El preflight comunica diferencial, espacio, conflictos y cobertura antes de
  escribir, y el cierre diferencia correcto de parcial.
- Un mirror jamás elimina excedentes: limpieza es plan aprobado, reversible y
  con cuarentena.
- Restore verifica hash, resuelve colisiones explícitamente y no sobrescribe en
  silencio.
- Local, mounts y SSH se degradan a `Offline` de forma segura; Windows UNC,
  macOS y Linux están contemplados.
- Manifiestos, journals y retención permiten auditar y recuperar dentro de la
  ventana de conservación.
- No se filtran credenciales; el agente SSH rechaza rutas fuera de su alcance.
