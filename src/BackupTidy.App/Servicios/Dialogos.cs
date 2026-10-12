using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace BackupTidy.App.Servicios;

/// <summary>Dialogos del sistema. Interfaz para que los ViewModels no dependan de la ventana.</summary>
public interface IDialogos
{
    Task<string?> ElegirCarpeta(string titulo, string? inicial);

    Task<string?> ElegirCsv(string titulo, string? carpetaInicial);

    Task<string?> GuardarCsv(string titulo, string nombreSugerido, string? carpetaInicial);

    /// <summary>Abre la carpeta en el Explorador; si es un archivo, abre su carpeta y lo selecciona.</summary>
    void MostrarEnExplorador(string ruta);
}

public sealed class DialogosAvalonia(TopLevel ventana) : IDialogos
{
    static readonly FilePickerFileType Csv = new("Plan o registro (CSV)") { Patterns = ["*.csv"] };

    public async Task<string?> ElegirCarpeta(string titulo, string? inicial)
    {
        var elegidas = await ventana.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = titulo,
            AllowMultiple = false,
            SuggestedStartLocation = await Carpeta(inicial),
        });
        return elegidas.Count > 0 ? elegidas[0].TryGetLocalPath() : null;
    }

    public async Task<string?> ElegirCsv(string titulo, string? carpetaInicial)
    {
        var elegidos = await ventana.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = titulo,
            AllowMultiple = false,
            FileTypeFilter = [Csv],
            SuggestedStartLocation = await Carpeta(carpetaInicial),
        });
        return elegidos.Count > 0 ? elegidos[0].TryGetLocalPath() : null;
    }

    public async Task<string?> GuardarCsv(string titulo, string nombreSugerido, string? carpetaInicial)
    {
        var archivo = await ventana.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = titulo,
            SuggestedFileName = nombreSugerido,
            DefaultExtension = "csv",
            FileTypeChoices = [Csv],
            SuggestedStartLocation = await Carpeta(carpetaInicial),
        });
        return archivo?.TryGetLocalPath();
    }

    public void MostrarEnExplorador(string ruta)
    {
        if (!OperatingSystem.IsWindows()) return;
        var argumentos = File.Exists(ruta) ? $"/select,\"{ruta}\"" : $"\"{ruta}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", argumentos) { UseShellExecute = true });
    }

    async Task<IStorageFolder?> Carpeta(string? ruta) =>
        string.IsNullOrWhiteSpace(ruta) || !Directory.Exists(ruta) ? null : await ventana.StorageProvider.TryGetFolderFromPathAsync(ruta);
}
