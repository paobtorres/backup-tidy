# BackupTidy

App de escritorio para organizar backups de celulares: detecta duplicados, clasifica fotos,
videos, capturas y documentos, y los agrupa por año y mes. Nunca borra: todo se mueve,
queda registrado y se puede deshacer.

- Especificación del motor: [docs/ESPECIFICACION.md](docs/ESPECIFICACION.md)
- Script de referencia (PowerShell, probado con 357 GB reales): [docs/referencia/organizar_backup.ps1](docs/referencia/organizar_backup.ps1)

## Cómo se usa

1. **Carpetas**: el backup a organizar y la carpeta organizada (en el mismo disco).
2. **Análisis**: solo lee; se puede cancelar.
3. **Revisión**: lista de archivos con su destino y fecha, y vista previa (los duplicados lado a lado
   con la copia que se conserva).
   - **Filtros**: por categoría o por subcarpeta (pestañas del menú de la izquierda), **Todos /
     Duplicados / Sin duplicar** (cada uno con su cantidad y tamaño), subcarpeta (el desplegable
     muestra los archivos y GB de cada una), búsqueda y fechas dudosas.
   - **Selección**: al empezar está todo seleccionado. Cada archivo tiene su casilla;
     "Seleccionar los N de la lista" selecciona o quita lo que se ve con los filtros, y la casilla de
     cada categoría o subcarpeta del menú la selecciona o quita entera. A medias, la casilla
     selecciona todo.
   - **Organizar** mueve solo lo seleccionado; lo demás queda donde está. Un JPG ya organizado sale a
     revisión solo si entra el HEIC que lo reemplaza. "Guardar plan (CSV)" guarda lo mismo que se va
     a organizar.

   JPG, PNG, GIF, BMP y WebP se muestran siempre, ya derechos según la orientación EXIF. HEIC, RAW,
   TIFF y videos usan la miniatura de Windows (la misma del Explorador), así que dependen de los
   códecs instalados: [HEIF](https://apps.microsoft.com/detail/9pmmsr1cgpwg),
   [HEVC](https://apps.microsoft.com/detail/9nmzlz57r3t7) y
   [RAW](https://apps.microsoft.com/detail/9nctdw2w1bh8) de Microsoft Store. Si falta el de HEVC,
   las Live Photos muestran su foto en lugar del video.
4. **Resultado**: mueve, muestra lo hecho y permite deshacer.

### Copias de seguridad de iTunes

En el paso 1, "Sacar las fotos de la copia…" lee la copia que iTunes (o la app Dispositivos de Apple)
guarda en `%APPDATA%\Apple Computer\MobileSync\Backup`, o cualquier carpeta con `Manifest.db`.
Copia a una carpeta nueva, con su nombre y fecha reales:

- **Carrete**: fotos y videos originales (`DCIM`), con las Live Photos.
- **Versiones editadas**: lo editado en el iPhone, al lado del original como `IMG_1234_editada`.
- **WhatsApp**: fotos y videos de los chats, sin stickers ni notas de voz.

Esa carpeta se analiza y organiza como cualquier backup. La copia de iTunes no se modifica. Como hay
que copiar (no mover), hace falta espacio libre en el disco de la carpeta organizada. Si se cancela o
se llena el disco, volver a copiar sigue desde donde quedó. Todavía no se leen copias encriptadas
(con contraseña) ni de iOS 9 o anterior.

El plan y el registro de movimientos de cada organización quedan en `<carpeta organizada>\_reportes\`.
Con ese registro se puede deshacer más tarde ("Deshacer una organización anterior" en el primer paso).

## Generar el ejecutable

Un único `BackupTidy.exe` (~48 MB) que no necesita .NET instalado:

```
dotnet publish src/BackupTidy.App -p:PublishProfile=win-x64
```

Queda en `publish\win-x64\BackupTidy.exe`. No está firmado: la primera vez Windows SmartScreen
puede avisar ("Más información" → "Ejecutar de todas formas").

## Requisitos de desarrollo

- [SDK de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git, VS Code (extensión C# Dev Kit recomendada)

## Estructura

```
BackupTidy.slnx
src/BackupTidy.Core         motor: clasificacion, fechas, duplicados, plan, ejecucion, deshacer y copias de iTunes (sin UI)
src/BackupTidy.App          interfaz de escritorio (Avalonia + CommunityToolkit.Mvvm)
src/BackupTidy.Cli          linea de comandos para correr el motor sobre carpetas reales y validarlo
tests/BackupTidy.Tests      pruebas del motor (xUnit)
tests/BackupTidy.App.Tests  pruebas de las pantallas (xUnit + Avalonia headless, sin abrir ventanas)
```

```
dotnet build
dotnet test
dotnet run --project src/BackupTidy.App
dotnet run --project src/BackupTidy.Cli -- analizar --origen <backup> --destino <organizado> --plan plan.csv
dotnet run --project src/BackupTidy.Cli -- copias-itunes
dotnet run --project src/BackupTidy.Cli -- extraer-itunes --copia <copia> --salida <carpeta> [--contenido carrete,editadas,whatsapp]
```
