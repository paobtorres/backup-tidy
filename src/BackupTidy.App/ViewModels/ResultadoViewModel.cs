using BackupTidy.App.Servicios;
using BackupTidy.Core.Ejecucion;
using BackupTidy.Core.Plan;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupTidy.App.ViewModels;

/// <summary>Paso 4: mover segun el plan (o deshacer) y mostrar el resultado.</summary>
public partial class ResultadoViewModel(MainViewModel principal, string? destino) : ViewModelBase
{
    readonly CancellationTokenSource cancelar = new();

    public string? Destino { get; } = destino;

    [ObservableProperty]
    public partial bool Trabajando { get; set; } = true;

    [ObservableProperty]
    public partial string Titulo { get; set; } = "Organizando…";

    [ObservableProperty]
    public partial string Detalle { get; set; } = "";

    [ObservableProperty]
    public partial double Porcentaje { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Cifra> Cifras { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> Avisos { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeDeshacer))]
    public partial string? RutaRegistro { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeDeshacer))]
    public partial bool Deshecho { get; set; }

    [ObservableProperty]
    public partial bool ConfirmandoDeshacer { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DetenerCommand))]
    public partial bool Deteniendo { get; set; }

    public bool PuedeDeshacer => RutaRegistro is not null && !Deshecho;

    public bool HayAvisos => Avisos.Count > 0;

    partial void OnAvisosChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HayAvisos));

    public async Task Ejecutar(IReadOnlyList<FilaPlan> filas, string origen, bool descartesAPapelera)
    {
        // El plan y el registro quedan junto a lo organizado: el registro es lo que permite deshacer
        var reportes = Path.Combine(Destino!, "_reportes");
        var marca = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        IPapelera? papelera = descartesAPapelera && OperatingSystem.IsWindows() ? new PapeleraWindows() : null;
        try
        {
            Directory.CreateDirectory(reportes);
            PlanCsv.Escribir(filas, Path.Combine(reportes, $"plan_{marca}.csv"));
            var registro = Path.Combine(reportes, $"movimientos_{marca}.csv");
            var progreso = new ProgresoUi<(int Hechos, int Total)>(p =>
            {
                Porcentaje = 100.0 * p.Hechos / Math.Max(1, p.Total);
                Detalle = $"{Formato.Cantidad(p.Hechos)} de {Formato.Cantidad(p.Total)} archivos";
            });

            var r = await Task.Run(() => Ejecutor.Ejecutar(filas, registro, origen, papelera, progreso, cancelar.Token));

            RutaRegistro = registro;
            Titulo = r.Cancelado ? "Organización detenida" : r.Errores > 0 ? "Listo, con errores" : "¡Listo!";
            Detalle = r.Cancelado
                ? "Lo movido hasta ahora quedó registrado y se puede deshacer."
                : "Nada se borró: cada movimiento quedó registrado y se puede deshacer.";
            Cifras =
            [
                new("Movidos", Formato.Cantidad(r.Movidos)),
                new("Omitidos", Formato.Cantidad(r.Omitidos), "Cambiaron desde el análisis o el destino ya existía"),
                new("Errores", Formato.Cantidad(r.Errores)),
                new("A la Papelera", Formato.Cantidad(r.EnviadosAPapelera)),
            ];
            Avisos = r.Avisos;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Titulo = "No se pudo organizar";
            Detalle = e.Message;
        }
        finally
        {
            Trabajando = false;
            Porcentaje = 100;
        }
    }

    public async Task Deshacer(string registro)
    {
        Trabajando = true;
        ConfirmandoDeshacer = false;
        Titulo = "Deshaciendo…";
        Detalle = "Devolviendo cada archivo a su lugar";
        try
        {
            var r = await Task.Run(() => Ejecutor.Deshacer(registro));
            Deshecho = true;
            RutaRegistro ??= registro;
            Titulo = "Deshecho";
            Detalle = r.EnPapelera > 0
                ? "Los descartes enviados a la Papelera se restauran desde la Papelera de reciclaje."
                : "Los archivos volvieron a donde estaban.";
            Cifras =
            [
                new("Restaurados", Formato.Cantidad(r.Restaurados)),
                new("Omitidos", Formato.Cantidad(r.Omitidos), "Ya no estaban en el destino o su lugar original estaba ocupado"),
                new("En la Papelera", Formato.Cantidad(r.EnPapelera)),
            ];
            Avisos = [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            Titulo = "No se pudo deshacer";
            Detalle = e is KeyNotFoundException ? "El archivo elegido no es un registro de movimientos." : e.Message;
        }
        finally
        {
            Trabajando = false;
        }
    }

    bool PuedeDetener => !Deteniendo;

    [RelayCommand(CanExecute = nameof(PuedeDetener))]
    void Detener()
    {
        Deteniendo = true;
        cancelar.Cancel();
    }

    [RelayCommand]
    void AbrirDestino()
    {
        if (Destino is not null) principal.Dialogos.MostrarEnExplorador(Destino);
    }

    [RelayCommand]
    void PedirDeshacer() => ConfirmandoDeshacer = true;

    [RelayCommand]
    void CancelarDeshacer() => ConfirmandoDeshacer = false;

    [RelayCommand]
    Task ConfirmarDeshacer() => Deshacer(RutaRegistro!);

    [RelayCommand]
    void NuevoAnalisis() => principal.VolverAlInicio();
}

public sealed record Cifra(string Nombre, string Valor, string? Ayuda = null);
