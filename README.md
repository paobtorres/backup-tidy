# BackupTidy

App de escritorio para organizar backups de celulares: detecta duplicados, clasifica fotos,
videos, capturas y documentos, y los agrupa por año y mes. Nunca borra: todo se mueve,
queda registrado y se puede deshacer.

- Especificación del motor: [docs/ESPECIFICACION.md](docs/ESPECIFICACION.md)
- Script de referencia (PowerShell, probado con 357 GB reales): [docs/referencia/organizar_backup.ps1](docs/referencia/organizar_backup.ps1)

## Cómo se usa

1. **Carpetas**: el backup a organizar y la carpeta organizada (en el mismo disco).
2. **Análisis**: solo lee; se puede cancelar.
3. **Revisión**: resumen por categoría, lista de archivos con su destino y fecha, vista previa
   (los duplicados lado a lado con la copia que se conserva). Se puede guardar el plan en CSV.
4. **Resultado**: mueve, muestra lo hecho y permite deshacer.

El plan y el registro de movimientos de cada organización quedan en `<carpeta organizada>\_reportes\`.
Con ese registro se puede deshacer más tarde ("Deshacer una organización anterior" en el primer paso).

## Requisitos de desarrollo

- [SDK de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git, VS Code (extensión C# Dev Kit recomendada)

## Estructura

```
BackupTidy.slnx
src/BackupTidy.Core    motor: clasificacion, fechas, duplicados, plan, ejecucion y deshacer (sin UI)
src/BackupTidy.App     interfaz de escritorio (Avalonia + CommunityToolkit.Mvvm)
src/BackupTidy.Cli     linea de comandos para correr el motor sobre carpetas reales y validarlo
tests/BackupTidy.Tests pruebas del motor (xUnit)
```

```
dotnet build
dotnet test
dotnet run --project src/BackupTidy.App
dotnet run --project src/BackupTidy.Cli -- analizar --origen <backup> --destino <organizado> --plan plan.csv
```
