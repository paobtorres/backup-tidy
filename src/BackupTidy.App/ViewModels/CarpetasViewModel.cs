using BackupTidy.Core.Plan;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupTidy.App.ViewModels;

/// <summary>Paso 1: elegir el backup a organizar y la carpeta organizada.</summary>
public partial class CarpetasViewModel(MainViewModel principal) : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error))]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    public partial string Origen { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error))]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    public partial string Destino { get; set; } = "";

    /// <summary>No volver a importar lo que ya esta en el destino.</summary>
    [ObservableProperty]
    public partial bool CompararConDestino { get; set; } = true;

    /// <summary>Error del ultimo analisis (se muestra al volver a este paso).</summary>
    [ObservableProperty]
    public partial string? ErrorAnalisis { get; set; }

    /// <summary>Lo que dejo hecho otro paso, por ejemplo la extraccion de una copia de iTunes.</summary>
    [ObservableProperty]
    public partial string? Aviso { get; set; }

    /// <summary>Problema con las carpetas elegidas, o null si se puede analizar (o falta elegir).</summary>
    public string? Error
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Origen) || string.IsNullOrWhiteSpace(Destino)) return null;
            string origen, destino;
            try
            {
                origen = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Origen));
                destino = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Destino));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "La ruta no es válida.";
            }
            if (!Directory.Exists(origen)) return "La carpeta del backup no existe.";
            if (destino.Equals(origen, StringComparison.OrdinalIgnoreCase)
                || destino.StartsWith(origen + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return "La carpeta organizada no puede estar dentro del backup.";
            if (!string.Equals(Path.GetPathRoot(origen), Path.GetPathRoot(destino), StringComparison.OrdinalIgnoreCase))
                return "Las dos carpetas tienen que estar en el mismo disco: así mover es instantáneo y no ocupa espacio extra.";
            return null;
        }
    }

    bool PuedeAnalizar => !string.IsNullOrWhiteSpace(Origen) && !string.IsNullOrWhiteSpace(Destino) && Error is null;

    [RelayCommand]
    async Task ElegirOrigen()
    {
        if (await principal.Dialogos.ElegirCarpeta("Backup a organizar", Origen) is string carpeta) Origen = carpeta;
    }

    [RelayCommand]
    async Task ElegirDestino()
    {
        if (await principal.Dialogos.ElegirCarpeta("Carpeta organizada", Destino) is string carpeta) Destino = carpeta;
    }

    [RelayCommand(CanExecute = nameof(PuedeAnalizar))]
    Task Analizar()
    {
        ErrorAnalisis = Aviso = null;
        // Si el destino todavia no existe no hay nada con que comparar
        var compararCon = CompararConDestino && Directory.Exists(Destino) ? new[] { Destino } : null;
        return principal.Analizar(new OpcionesAnalisis(Origen, Destino, compararCon));
    }

    [RelayCommand]
    void ImportarItunes()
    {
        Aviso = null;
        principal.IrA(new ImportarItunesViewModel(principal), 1);
    }

    [RelayCommand]
    async Task DeshacerAnterior()
    {
        var carpeta = Directory.Exists(Path.Combine(Destino, "_reportes")) ? Path.Combine(Destino, "_reportes") : null;
        if (await principal.Dialogos.ElegirCsv("Registro de movimientos (movimientos_….csv)", carpeta) is string registro)
            await principal.Deshacer(registro);
    }
}
