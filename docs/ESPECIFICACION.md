# BackupTidy — Especificacion del motor

Reglas aprendidas organizando 357 GB de backups reales de celulares (4 iPhone + 1 Android,
exportados con FoneLab / iMazing desde backups de iTunes). La version de referencia en
PowerShell esta en `referencia/organizar_backup.ps1` y funciono sin errores sobre ~170.000 archivos.

## Principios

1. **Nunca borrar.** Todo se mueve (mismo disco = instantaneo, sin espacio extra). Los duplicados
   y descartes van a carpetas de revision o a la Papelera de reciclaje.
2. **Analizar antes de ejecutar.** El analisis genera un *plan* (origen -> destino, categoria,
   motivo). El usuario lo revisa y recien ahi se ejecuta.
3. **Todo es reversible.** Cada movimiento se registra; "Deshacer" lo revierte en orden inverso.
4. **Nunca sobrescribir.** Si el destino existe: `nombre_2.ext`, `nombre_3.ext`...
5. **Validar antes de mover:** el archivo origen sigue existiendo y tiene el mismo tamano que en el plan.

## Flujo

```
Origen (backup exportado) ──► Analisis ──► Plan ──► Revision ──► Ejecucion ──► Registro (deshacer)
                                  ▲
          Archivo organizado ─────┘  (comparar para no volver a importar lo que ya existe)
```

## Clasificacion de archivos

| Tipo | Extensiones |
|---|---|
| Foto | jpg jpeg heic heif png dng gif webp tif tiff bmp + RAW: nef cr2 cr3 arw orf raf rw2 |
| Video | mov mp4 m4v 3gp avi mkv mpg mpeg wmv |
| Descarte | opus m4a mp3 wav caf amr (notas de voz/audios de apps), vcf ics, aae (ediciones iOS), favicon, was (stickers animados WhatsApp = zip), Zone.Identifier, Thumbs.db, desktop.ini, .picasa.ini |
| Documento | todo lo demas |

### Categorias de fotos/videos (en orden de prioridad)

1. **Duplicado exacto** -> `_duplicados\<ruta original>`
2. **Vista previa del programa de restore** -> `_duplicados_conversiones\...`:
   `IMG_1234.HEIC.jpg`, `.PNG.jpg`, `.MOV.jpg`, `.DNG.jpg` cuando el original existe al lado.
3. **Apps** — ruta contiene `\App Photos\` (cache interna de Notas, Duo, etc.)
4. **WhatsApp** — ruta contiene `\WhatsApp...\`
5. **Miniaturas** — `*.THM.jpg`
6. **Capturas** — nombre contiene screenshot / captura / screen_recording / RPReplay,
   o PNG sin marca de camara.
7. **Mensajes** — ruta `\Messages\`, `\Message Attachments\`, `\Attachments\`, `\Telegram...\`
8. **LivePhotos** — video <= 30 MB con una foto del mismo nombre base en el mismo backup
   (clave: primeros 2 niveles de ruta + nombre sin extension).
9. **Guardadas** — JPG/TIFF sin marca de camara en el EXIF (descargas, WhatsApp guardado en el carrete).
10. **Fotos** / **Videos** — el resto.

Destino: `<Categoria>\<AAAA>\<AAAA-MM>\<nombre>`.

**Contenido vs. extension:** el tipo real se detecta por los primeros bytes. Si contradice a la extension
(foto <-> video, p. ej. un JPEG llamado `.MOV`), se clasifica por el contenido, **sin cambiar el nombre**,
con la nota "Es JPG con extension .MOV". Mismo tipo en otro formato (DNG leido como TIFF) no cuenta.

### Documentos

`Documentos\PDF | Office (doc docx xls xlsx ppt pptx pbix odt ods odp rtf) | Comprimidos (zip rar 7z gz tar) | Otros`.
Sin fecha (la de modificacion es la de la exportacion). Duplicados -> `Documentos\_duplicados`.

## Fechas (lo mas delicado)

Orden de prioridad:

1. **EXIF DateTimeOriginal (0x9003)**, luego 0x9004, luego 0x0132. Formato `yyyy:MM:dd HH:mm:ss`, hora local.
2. **Metadatos del contenedor**: videos -> `com.apple.quicktime.creationdate` o cabecera mvhd (UTC);
   luego XMP (`exif:DateTimeOriginal`, `photoshop:DateCreated`, `xmp:CreateDate`), donde PNG y webp
   editados (capturas, stickers, fondos) suelen tener su unica fecha.
   **Ojo:** las propiedades de Windows (System.Photo.DateTaken / System.Media.DateEncoded)
   vienen en **UTC** -> convertir a local (se verifico: EXIF 12:12 = Windows 15:12 en UTC-3).
3. **Fecha en el nombre**: `IMG-20190315-WA0001`, `Screenshot_2019-03-15`, `20190315_123456`
   (regex `(?<!\d)((?:19|20)\d{2})[-_.]?(0[1-9]|1[0-2])[-_.]?(0[1-9]|[12]\d|3[01])(?!\d)`).
   Solo en carpetas de WhatsApp, ademas `D_M_AAAA HH_MM` (`18_9_2025 18_06.jpg`, con hora).
4. **Fecha de modificacion** como ultimo recurso — **pero** en carpetas restauradas es la fecha
   del restore. Regla: si una carpeta tiene >= 20 archivos y >= 50 % comparten el mismo dia de
   modificacion, esa fecha es *dudosa* -> destino `<Categoria>\_fecha_dudosa\AAAA\AAAA-MM`.

Rango valido: 2000-01-01 .. hoy+1 dia.

## Duplicados

- Agrupar por **tamano**; calcular hash (MD5 alcanza) **solo** en grupos con >1 archivo. Enorme ahorro.
- Al comparar con el archivo organizado: solo hashear grupos que contengan al menos un archivo nuevo.
- Copia a conservar dentro del origen: no convertida (`.HEIC.jpg`) > fuera de Mensajes >
  nombre mas corto > ruta mas corta.
- Si ya existe en el archivo organizado: el nuevo va a `_duplicados` ("Ya existe en: ...").
  Preferir como referencia la copia que no esta en una carpeta de revision
  (`_duplicados*`, `_jpg_reemplazados`, `_revision*`).

### Misma foto en otro formato

FoneLab puede exportar HEIC convertidos a JPG (no son identicos por hash). Clave:
`AAAA-MM | nombre base` (`IMG_1234.HEIC.jpg` / `IMG_1234_2.JPG` -> `img_1234`).

- Nuevo HEIC + JPG organizado con la misma clave -> el HEIC entra; el JPG va a `_jpg_reemplazados\`.
- Otro caso con la misma clave -> `_duplicados_probables\`.

## Papelera

- Enviar con `SendToRecycleBin` (Microsoft.VisualBasic.FileIO o SHFileOperation con FOF_ALLOWUNDO).
- **Antes**, verificar capacidad: `HKCU\...\Explorer\BitBucket\Volume\{GUID}\MaxCapacity` (MB) y
  `NukeOnDelete`. Si el total supera la capacidad, Windows borraria **definitivamente** sin avisar
  -> no enviar.
- Si una carpeta solo contiene descartes (o esta vacia) enviarla entera: 1 operacion en vez de 30.000.
- Nunca enviar la carpeta raiz del origen ni archivos que no esten en el plan.

## Casos reales encontrados

- Backups restaurados: todos los archivos con fecha de modificacion = dia del restore.
- 4 backups del mismo iPhone solapados casi al 100 % -> 48.000 duplicados, 177 GB.
- Un `.zip` de 17,7 GB era copia exacta de una carpeta ya importada (verificado entrada por entrada).
- Camera Roll de iPhone 14 Pro exportado sin HEIC -> FoneLab tenia activada la conversion.
- 110 de 7.074 "videos" (.MOV / .MP4, 160 MB) eran en realidad JPEG: quedaban en Videos y LivePhotos
  y la fecha salia de la modificacion. Muestra en `backup_celulares_organizado\_revision_jpeg_con_extension_de_video`.
- Fotos de Sony Xperia (C1904): GDI no ve la marca en el EXIF y el script las mandaba a Guardadas;
  MetadataExtractor si la lee ("Sony").
- Exportacion de FoneLab del iPhone 14 Pro: la fecha de modificacion de todo es la de la exportacion.
  `WhatsApp Attachments` nombra los archivos `D_M_AAAA HH_MM` (`18_9_2025 18_06.jpg`): 15.333 archivos
  con la fecha del mensaje en el nombre que la regla `AAAAMMDD` no reconoce. Con la regla de WhatsApp,
  15.341 de 15.437 salen de `_fecha_dudosa`.

## Validacion contra el script (muestra de 84.327 archivos)

- Mismo plan en el 98,8 % de las filas; con `CompararCon` el 99,7 %, y las 31.293 decisiones de
  duplicados identicas. Todas las diferencias revisadas favorecen al motor (marca Sony, fechas
  EXIF/XMP de PNG que Windows no expone). Motor: 1,7 min; script: 9,6 min.
- Ejecutar + deshacer sobre 2.456 archivos reales: inventario (ruta, tamano, fecha, MD5) identico.
- Un restore del iPhone reinicio el carrete: un backup nuevo NO contiene necesariamente las fotos viejas.
- Backup de iTunes: un solo backup por dispositivo (carpeta = UDID); el nuevo reemplaza al anterior.
- `Manifest.db` (SQLite) indica que archivo es cada foto en backups **no cifrados** -> fase 2:
  leer backups de iTunes directamente sin FoneLab.

## Roadmap

**MVP (Windows):** elegir origen/destino -> analizar (progreso, cancelar) -> resumen + revision
visual (miniaturas, duplicados lado a lado, fechas dudosas) -> ejecutar -> deshacer.

**Fase 2:** leer backups de iTunes directamente; fotos similares (hash perceptual); reemplazo
HEIC/JPG asistido; firma de codigo e instalador para distribuir; macOS.

## Stack elegido

C# / .NET 10 + Avalonia UI (multiplataforma) + MetadataExtractor (EXIF/HEIC/QuickTime/RAW).
Publicacion como un unico `.exe` (`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`).
