# 01 — Raíces de biblioteca y destinos gestionados

## Propósito

Una **raíz** es un directorio que Heimdall puede catalogar y, si el usuario lo
autoriza, organizar. Es el límite explícito de alcance: Heimdall no recorre ni
modifica rutas que no pertenezcan a una raíz, un destino gestionado, una
cuarentena o un backup configurado.

La funcionalidad admite bibliotecas en el equipo, discos externos, recursos de
red montados por el sistema y, posteriormente, un servidor alcanzado por SSH.
Una raíz puede ser sólo de lectura: catalogar y planificar sigue siendo útil
aunque organizar no lo sea.

Esta especificación sustituye `MainPath`, `LibraryPath` y galerías `Name`/`Path`
por recursos nombrados y con capacidades explícitas. Nunca depende de rutas
codificadas en la app.

## Tipos de ubicación

| Tipo | Ejemplos | Quién opera | Uso recomendado |
| --- | --- | --- | --- |
| Local | `/Volumes/Fotos`, `/mnt/photos`, `D:\\Fotos` | Cliente Heimdall | Disco interno o USB. |
| Montaje del SO | SMB/NFS/AFP/FUSE montado; `\\servidor\\Fotos` | Cliente mediante el montaje | NAS o servidor accesible como filesystem. |
| Agente SSH remoto | `ssh://foto@nas.example/volume/photos` | Agente Heimdall en servidor | El servidor conserva datos y trabaja localmente. |

Una ubicación montada **no requiere que Heimdall implemente SMB, NFS o AFP**:
el usuario o administrador la monta con herramientas nativas y Heimdall usa la
ruta resultante. Esto respeta el proveedor de red y sus políticas.

### Compatibilidad de montajes

- **macOS:** rutas locales y volúmenes bajo `/Volumes`; recursos SMB/NFS/AFP
  montados con Finder o `mount` aparecen como carpetas.
- **Linux:** rutas locales, `/mnt`, `/media` o `/run/user/.../gvfs`. Admite
  SMB/CIFS, NFS, SSHFS y FUSE cuando ya estén disponibles para el usuario.
- **Windows:** letras (`D:\\Fotos`), UNC (`\\\\nas\\Fotos`) y unidades de red.
  La app normaliza rutas sin asumir sensibilidad a mayúsculas y detecta
  unidades desconectadas. Usa la sesión y gestor de credenciales del sistema.

Para todos los montajes, la UI muestra tipo detectado cuando el SO lo exponga,
espacio, identidad del volumen/recurso, latencia y conexión. Si desaparece,
queda **offline**: no se borran assets de catálogo ni se intenta reparar nada.

## Subfunciones

### 1. Alta guiada de raíz

El usuario elige una carpeta con selector nativo o pega una ruta, le da un
nombre (por ejemplo, “Archivo 2020–24”) y elige modo:

- **Catalogar solamente:** lectura de rutas y metadatos; ningún cambio a los
  originales.
- **Proponer organización:** permite análisis y planes, pero no aplicarlos.
- **Gestionada:** permite planes aprobados. Exige escritura comprobada y
  cuarentena válida o destino de copia verificado.

Antes de guardar, muestra una vista previa no invasiva: estimación de fotos,
formatos, tamaño y exclusiones. El escaneo completo sólo empieza al confirmar.

### 2. Edición, pausa y retirada segura

Cada raíz admite cambiar nombre, etiquetas, estado, política de lectura y
exclusiones. Pausar detiene scans nuevos y evita aplicar operaciones pendientes,
sin borrar catálogo. Al retirarla, se ofrece:

- **Desconectar del catálogo:** historial y assets quedan marcados inactivos.
- **Olvidar datos de catálogo:** borra únicamente datos derivados locales tras
  confirmación explícita; jamás archivos del usuario.

No puede retirarse una raíz con plan en ejecución. Un cambio de ruta es una
reconexión: se comprueba huella de volumen y raíz antes de reutilizar catálogo.

### 3. Solapes y ownership

Al guardar, Heimdall canonicaliza y comprueba que una raíz no contenga ni esté
contenida por otra raíz, cuarentena, cache, destino gestionado o backup. El
solape se bloquea por defecto porque provoca scans duplicados y operaciones
recursivas. Sólo se puede declarar una excepción de lectura con justificación;
nunca una relación que mueva a una carpeta escaneada como fuente.

Cada ruta pertenece a una raíz propietaria y puede tener ubicaciones
alternativas. Si dos raíces ven el mismo archivo por bind mount, symlink o UNC,
el catálogo deduplica por identidad de filesystem cuando exista y advierte si
no puede garantizarlo.

### 4. Exclusiones y límites de exploración

Permite excluir subcarpetas, glob patterns y tipos, con vista previa de impacto.
Se excluyen por defecto catálogo, previews, cuarentena, backups y temporales.
El usuario puede definir profundidad, seguimiento de symlinks/junctions (por
defecto no), ocultos/sistema, formatos, tamaños, ventanas y límites de E/S.

Se detectan ciclos incluso si se siguen enlaces. Ningún patrón puede incluir
implícitamente datos de Heimdall en una raíz gestionada.

### 5. Estado, salud y reconexión

La ficha muestra último scan y operación, número de assets, espacio, permisos,
errores, versión de agente y diagnóstico. Estados mínimos: `Pendiente de
validar`, `Activa`, `Sólo lectura`, `Pausada`, `Offline`, `Credenciales
requeridas`, `Degradada` y `Error de configuración`.

“Comprobar ahora” es ligero; “Reescanear” programa scan completo o incremental.
Una desconexión no produce borrados, movimientos ni conflictos falsos.

### 6. Destinos asociados

La pantalla configura también:

- **Destino gestionado:** copia o reubicación mediante plantilla organizativa.
- **Cuarentena:** retirada reversible con estructura, manifiesto y hash.
- **Cache de previews:** regenerable y excluida del scan.
- **Backups:** destinos para verificación/copia.

Todos tienen nombre, ruta, conexión, capacidad y permisos propios. El asistente
exige cuarentena y destinos disponibles antes de ofrecer “Aplicar plan”.

## Flujo de UI recomendado

1. **Bibliotecas → Añadir ubicación:** elegir `Carpeta local o montada` o
   `Servidor SSH`.
2. **Conectar y validar:** comprobar ruta, volumen, permisos y solapes; explicar
   claramente los límites de una verificación fallida.
3. **Definir alcance:** nombre, modo, exclusiones y ritmo de scan, con ejemplos
   incluidos/excluidos.
4. **Confirmar:** crear `Pendiente de validar`; iniciar scan ahora o después.
5. **Gestionar:** tarjeta con salud y `Abrir en el sistema`, `Comprobar`,
   `Escanear`, `Pausar`, `Editar`, `Retirar`.

Las acciones de modificación no viven aquí: la raíz sólo otorga capacidad. Los
planes conservan revisión, aprobación y journal propios.

## Modelo de dominio

`LibraryRoot` incluye como mínimo:

| Campo | Descripción |
| --- | --- |
| `Id`, `DisplayName` | Identidad estable y nombre visible. |
| `LocationKind` | `LocalPath`, `MountedPath` o `SshAgent`. |
| `CanonicalUri` | Ruta/URI normalizada, sin secretos. |
| `RootFingerprint` | Identidad de volumen y directorio, si es posible. |
| `Mode` | `CatalogOnly`, `PlanOnly` o `Managed`. |
| `Status`, `LastHealthCheckAt`, `LastScanAt` | Estado y trazabilidad. |
| `Capabilities` | Lectura, escritura, rename atómico, hardlinks, xattrs, watch, hash remoto. |
| `ScanPolicy` | Exclusiones, enlaces, límites y programación. |
| `CredentialReference` | Referencia opaca al almacén seguro, nunca secreto. |
| `AgentEndpoint` | SSH: host, puerto, usuario y clave de host fijada. |
| `CreatedAt`, `UpdatedAt`, `LastError` | Auditoría local. |

`ManagedDestination` reutiliza la abstracción, añade `Purpose` (`Library`,
`Quarantine`, `PreviewCache`, `Backup`) y reglas de capacidad.
`RootHealthSnapshot` conserva comprobaciones fechadas para que un plan exija
salud reciente antes de ejecutarse.

## Validaciones obligatorias

- La ruta existe, es directorio y se resuelve sin ambigüedad.
- `Managed` exige prueba de escritura reversible dentro de la ubicación, nunca
  fuera de ella.
- `CatalogOnly` y `PlanOnly` no permiten mover, renombrar, cuarentenar ni borrar.
- Cuarentena, cache y backup no pueden quedar bajo una fuente salvo exclusión
  verificada.
- Separadores, Unicode y case-sensitivity se normalizan por plataforma, aunque
  se conserva la ruta original para UI.
- En red, un timeout conserva estado anterior e informa el fallo; nunca infiere
  escritura.
- La identidad de host SSH se fija antes de autenticar; si cambia, se bloquea.

## Credenciales y SSH

Para local y montajes, Heimdall delega autenticación al SO: no guarda passwords
SMB/NFS/AFP, tokens cloud ni credenciales Windows. Puede explicar cómo montar o
reconectar, pero no pide ni persiste la contraseña.

Una ubicación SSH conecta a un **agente Heimdall**, no a un shell arbitrario.
El agente enumera, extrae metadatos, hashea y ejecuta operaciones sólo en rutas
autorizadas; el cliente recibe inventario, estado y resultados verificables. La
cuenta remota debe tener permiso mínimo y el agente rechaza escapes de ruta.

- Preferir clave/certificado SSH o agente del sistema; password sólo si el
  transporte lo permite y siempre en Keychain (macOS), Secret Service/KWallet
  (Linux) o Credential Manager (Windows).
- Guardar sólo `CredentialReference` y huella pública del host; jamás secretos
  en SQLite, logs, exports o diagnósticos.
- Usar túnel estándar y backoff; nunca desactivar verificación de host.
- El cliente solicita operaciones planificadas con ID, precondiciones y hash;
  el agente journaliza, verifica y permite undo conforme a política.

El protocolo, instalación y autorización del agente se detallarán en su propia
funcionalidad. Esta pantalla sólo prueba conexión, declara rutas permitidas y
muestra capacidades.

## Privacidad, exportación y auditoría

Configuración local en catálogo. Su JSON versionado redacta por defecto rutas,
hosts, usuarios y huellas; el export portable con ubicaciones requiere aviso.
Las credenciales no se exportan nunca.

Diagnósticos registran alta, salud, offline, reconexión y permisos con IDs y
rutas redactables; no listados completos de fotos ni secretos. Se pueden borrar
sin afectar originales ni journals de operación existentes.

## Criterios de aceptación

- Varias raíces locales o montadas se escanean sin doble catalogación por solape.
- Un volumen desmontado mantiene inventario y queda `Offline`, sin operaciones.
- UNC/unidades Windows, `/Volumes` macOS y mounts Linux funcionan como carpetas.
- Una raíz de sólo lectura permite scan y planes, pero el motor bloquea cambios.
- Destino gestionado/cuarentena inválidos bloquean aplicación sin tocar archivos.
- SQLite, exportaciones y logs no incluyen secretos ni rutas sensibles por defecto.
- SSH rechaza host cuya identidad cambie y operaciones fuera de rutas autorizadas.
