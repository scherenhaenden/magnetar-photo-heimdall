# Índice de rendimiento y adopción selectiva de Rust

Heimdall empieza en C#/.NET por coherencia de producto, catálogo, UI y acceso
al sistema. Rust no es una reescritura: se adopta sólo para una unidad de trabajo
medida que sea CPU-intensiva, repetible y aislable detrás de una interfaz. La
primera opción es optimizar el diseño .NET (I/O secuencial, límites por volumen,
caché, lotes y concurrencia); después se compara una implementación Rust con el
baseline bajo las mismas condiciones.

## Índice de puntos calientes previstos

| Prioridad de medición | Trabajo | Por qué puede costar | Opción inicial | Posible núcleo Rust | Señal de éxito |
| --- | --- | --- | --- | --- | --- |
| 1 | BLAKE3 completo y huellas rápidas | Lee todos los bytes de bibliotecas grandes; puede saturar CPU, disco o red | Pipeline .NET limitado por endpoint, caché por identidad | `heimdall-hash` streaming/BLAKE3, lectura por bloques | Más MB/s sin aumentar I/O ni temperatura; mismo digest |
| 2 | Miniaturas, decodificación RAW y previews | Decodificación/resize de imagen consume CPU/RAM y bibliotecas nativas | Worker .NET con caché y límites | `heimdall-preview` sólo si una biblioteca Rust elegida supera la medición | Menor tiempo por preview y memoria; píxeles/orientación equivalentes |
| 3 | Comparación de duplicados/fingerprints visuales | Gran número de candidatos y operaciones vectoriales | Prefiltro de tamaño + BLAKE3 .NET | `heimdall-similarity` para hashes perceptuales/vectorización | Menos CPU/tiempo sin falsos positivos en conjunto de prueba |
| 4 | Recorrido masivo e inventario | Millones de entradas; llamadas a filesystem, metadatos y serialización | Scanner incremental .NET por raíz | `heimdall-scanner` si el perfil muestra CPU/allocs, no latencia de red | Más entradas/s con idéntico inventario/diagnósticos |
| 5 | Compresión/cifrado de caches o transferencias | Alto volumen de bytes, pero no siempre cuello de botella | APIs .NET/compresión existente | Núcleo pequeño basado en biblioteca auditada | Mejor coste de CPU/byte, sin alterar formato/seguridad |
| 6 | SQLite/catalog queries y UI | Normalmente I/O/diseño de consultas, no CPU puro | Índices, paginación, SQL y virtualización .NET | **No candidata por defecto** | Optimización SQL demuestra mejora antes de considerar FFI |
| 7 | Operaciones de archivos, SSH y planes | Correctitud, red y filesystem dominan | C# application service / agente remoto | **No candidata por defecto** | Mejorar protocolo/reintentos, no cruzar FFI |

Las columnas “posible núcleo Rust” son hipótesis, no compromisos. El agente SSH
puede usar el mismo núcleo compilado para su plataforma, pero su contrato con
el cliente continúa siendo RPC versionado; el cliente nunca cruza FFI hacia un
servidor remoto.

## Cómo decidir con datos

Para cada candidato se abre una ficha de benchmark con: versión de software,
hardware/OS, tipo de volumen (SSD/HDD/SMB/NFS/SSH), temperatura/política
térmica, conjunto de datos sintético y representativo, semilla, parámetros,
resultado y perfil. No se comparan resultados de discos o cachés distintos.

1. Medir baseline .NET en modo frío y caliente: throughput, latencia p50/p95,
   CPU, memoria, I/O leído/escrito, asignaciones, temperatura y energía si el
   sistema lo expone.
2. Localizar el límite: si el disco, SMB/NFS o SSH ya está saturado, Rust no es
   una solución; se mejora planificación, caché, paralelismo o cercanía del
   cómputo al dato.
3. Crear un prototipo Rust mínimo con el mismo contrato y corpus. Validar
   bit-a-bit hashes, inventario o resultado de render, además de cancelación y
   límites térmicos.
4. Adoptar sólo si mejora de manera reproducible el objetivo definido sin
   degradar memoria, fiabilidad, batería, temperatura ni portabilidad. El PR
   registra el baseline y umbral; si no hay ganancia relevante, se conserva
   .NET.
5. Mantener fallback .NET y conmutación por configuración hasta que las pruebas
   de recuperación, compatibilidad y empaquetado estén consolidadas.

Como punto de partida, una ganancia debe ser al menos 20% en la métrica que
limita el flujo y no superar los presupuestos térmicos. No es una regla rígida:
para hashing limitado por red se valora más reducir CPU/energía que MB/s.

## Límite de arquitectura

```text
Avalonia UI → servicios C# → IHashEngine / IPreviewEngine / IScanner
                              ├─ implementación .NET (fallback)
                              └─ adaptador FFI Rust (opcional)
                                      ↓
                                cdylib nativa por RID

Cliente C# ── túnel SSH/RPC ── agente Heimdall remoto
                                  └─ mismos motores locales, no FFI a distancia
```

Los servicios C# continúan siendo dueños de reglas, planes, permisos,
cuarentena, journal, cancelación, telemetría y UX. Rust recibe datos ya
validados y produce resultados sin decidir rutas ni modificar archivos fuera de
una operación explícita.

## Integración con csbindgen

`csbindgen` es adecuado para generar los `DllImport` C# desde funciones Rust
`extern "C"`; el proyecto requiere una `cdylib`, un `build.rs` y genera el
archivo C# en compilación. La API generada usa convención `Cdecl` y los binarios
resultantes varían por plataforma (`.dll`, `.so`, `.dylib`). [Documentación de
csbindgen](https://github.com/Cysharp/csbindgen)

La superficie FFI debe ser pequeña y orientada a lotes, no una llamada por byte
ni por archivo minúsculo:

- valores escalares y buffers con longitud explícita; UTF-8 y paths sólo como
  datos validados, sin permitir que Rust los trate como comandos;
- estructuras C ABI estables, `#[repr(C)]`, versión de API y función
  `get_capabilities`; nunca exponer tipos Rust, callbacks complejos o memoria
  administrada directamente;
- propiedad de memoria inequívoca: C# aporta buffers o Rust devuelve un handle
  opaco con `free_handle`; cada error se convierte en código/mensaje estructurado;
- `start/poll/cancel` por trabajo largo, con checkpoints, en vez de bloquear el
  hilo UI; los límites térmicos y de concurrencia los impone C# y se vuelven a
  comprobar en Rust;
- cargo build para cada RID objetivo (`win-x64`, `win-arm64`, `osx-arm64`,
  `osx-x64`, `linux-x64`, etc.), empaquetado junto a la app .NET, carga
  determinista y verificación en CI de que bindings y biblioteca coinciden.

El generador elimina trabajo manual de bindings, pero no resuelve ABI,
empaquetado, ownership ni compatibilidad. Por eso se versiona el contrato y se
prueban contra cada plataforma soportada.

## Primer experimento recomendado: BLAKE3

1. Implementar `IContentHasher` en .NET y medirlo sobre SSD local, HDD, SMB/NFS
   montado y agente SSH; incluir concurrencia 1, 2, 4 y política térmica.
2. Implementar `heimdall-hash` Rust con una sola operación de hash streaming,
   BLAKE3 completo y huella rápida versionada, cancelación cooperativa y sin
   escribir archivos.
3. Generar bindings con `csbindgen`, invocarlo mediante `RustContentHasher` y
   conservar `DotNetContentHasher` como fallback.
4. Verificar que cada digest y error coincida; comparar throughput, CPU, memoria
   y temperatura. En un agente remoto, ejecutar la comparación en el servidor
   donde viven los bytes.
5. Adoptarlo sólo para los RIDs donde gane y cargar el fallback cuando falte la
   biblioteca nativa o no supere la prueba de salud.

## Criterios de aceptación

- Ningún módulo Rust llega al producto sin baseline, corpus reproducible,
  comparación funcional y decisión documentada.
- La ausencia, fallo o incompatibilidad de una biblioteca nativa degrada a .NET
  sin afectar catálogo, planes ni datos de usuario.
- FFI no amplía permisos ni permite operaciones de filesystem/red/shell no
  autorizadas por los servicios C#.
- Las métricas incluyen temperatura y política activa, de modo que una mejora
  de velocidad que convierta el equipo en térmicamente inestable no se acepta.
