using System.Diagnostics;
using Avalonia.Threading;
using BackupTidy.App.Servicios;
using BackupTidy.Core.Plan;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupTidy.App.ViewModels;

/// <summary>Paso 2: analisis en curso. Solo lee: cancelar no deja nada a medias.</summary>
public partial class AnalisisViewModel(string origen) : ViewModelBase
{
    readonly CancellationTokenSource cancelar = new();
    readonly Stopwatch reloj = new();

    public string Origen { get; } = origen;

    public IReadOnlyList<EtapaVista> Etapas { get; } =
    [
        new(EtapaAnalisis.Listando, "Listar archivos"),
        new(EtapaAnalisis.BuscandoDuplicados, "Buscar duplicados"),
        new(EtapaAnalisis.LeyendoMetadatos, "Leer fechas y metadatos"),
        new(EtapaAnalisis.Clasificando, "Clasificar"),
    ];

    [ObservableProperty]
    public partial string Detalle { get; set; } = "Preparando…";

    [ObservableProperty]
    public partial double Porcentaje { get; set; }

    [ObservableProperty]
    public partial bool Indeterminado { get; set; } = true;

    [ObservableProperty]
    public partial string Transcurrido { get; set; } = "0:00";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelarCommand))]
    public partial bool Cancelando { get; set; }

    public async Task<ResultadoAnalisis> Ejecutar(OpcionesAnalisis opciones)
    {
        reloj.Start();
        var temporizador = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                                               (_, _) => Transcurrido = reloj.Elapsed.ToString(@"m\:ss"));
        temporizador.Start();
        try
        {
            var progreso = new ProgresoUi<ProgresoAnalisis>(Mostrar);
            return await Task.Run(() => Analizador.Analizar(opciones, progreso, cancelar.Token));
        }
        finally
        {
            temporizador.Stop();
        }
    }

    bool PuedeCancelar => !Cancelando;

    [RelayCommand(CanExecute = nameof(PuedeCancelar))]
    void Cancelar()
    {
        Cancelando = true;
        Detalle = "Cancelando…";
        cancelar.Cancel();
    }

    void Mostrar(ProgresoAnalisis p)
    {
        if (Cancelando) return;
        foreach (var e in Etapas)
        {
            e.EsActual = e.Etapa == p.Etapa;
            e.EsHecha = e.Etapa < p.Etapa;
        }
        Indeterminado = p.Total == 0;
        Porcentaje = p.Total == 0 ? 0 : 100.0 * p.Hechos / p.Total;
        Detalle = p.Total == 0 ? "Recorriendo carpetas…" : $"{Formato.Cantidad(p.Hechos)} de {Formato.Cantidad(p.Total)} archivos";
    }
}

public partial class EtapaVista(EtapaAnalisis etapa, string nombre) : ObservableObject
{
    public EtapaAnalisis Etapa { get; } = etapa;

    public string Nombre { get; } = nombre;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsPendiente))]
    public partial bool EsActual { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsPendiente))]
    public partial bool EsHecha { get; set; }

    public bool EsPendiente => !EsActual && !EsHecha;
}
