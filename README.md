# BackupTidy

App de escritorio para organizar backups de celulares: detecta duplicados, clasifica fotos,
videos, capturas y documentos, y los agrupa por año y mes. Nunca borra: todo se mueve,
queda registrado y se puede deshacer.

- Especificación del motor: [docs/ESPECIFICACION.md](docs/ESPECIFICACION.md)
- Script de referencia (PowerShell, probado con 357 GB reales): [docs/referencia/organizar_backup.ps1](docs/referencia/organizar_backup.ps1)

## Requisitos de desarrollo

- [SDK de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git, VS Code (extensión C# Dev Kit recomendada)

## Estructura

```
BackupTidy.slnx
src/BackupTidy.Core    motor: clasificacion, fechas, duplicados, plan, ejecucion y deshacer (sin UI)
src/BackupTidy.App     interfaz de escritorio (Avalonia + CommunityToolkit.Mvvm)
tests/BackupTidy.Tests pruebas del motor (xUnit)
```

```
dotnet build
dotnet test
dotnet run --project src/BackupTidy.App
```
