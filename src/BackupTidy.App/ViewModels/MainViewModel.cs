using BackupTidy.App.Servicios;
using BackupTidy.Core.Plan;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupTidy.App.ViewModels;

/// <summary>Ventana principal: navega entre los pasos Carpetas -> Analisis -> Revision -> Resultado.</summary>
public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(IDialogos dialogos)
    {
        Dialogos = dialogos;
        Carpetas = new CarpetasViewModel(this);
        Pagina = Carpetas;
        MarcarPaso(1);
    }

    public IDialogos Dialogos { get; }

    public CarpetasViewModel Carpetas { get; }

    public IReadOnlyList<PasoVista> Pasos { get; } =
        [new(1, "Carpetas"), new(2, "Análisis"), new(3, "Revisión"), new(4, "Resultado")];

    [ObservableProperty]
    public partial ViewModelBase Pagina { get; set; }

    public void IrA(ViewModelBase pagina, int paso)
    {
        Pagina = pagina;
        MarcarPaso(paso);
    }

    public void VolverAlInicio() => IrA(Carpetas, 1);

    public async Task Analizar(OpcionesAnalisis opciones)
    {
        var analisis = new AnalisisViewModel(opciones.Origen);
        IrA(analisis, 2);
        try
        {
            var resultado = await analisis.Ejecutar(opciones);
            IrA(new RevisionViewModel(this, opciones, resultado), 3);
        }
        catch (OperationCanceledException)
        {
            VolverAlInicio();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Carpetas.ErrorAnalisis = "No se pudo analizar: " + e.Message;
            VolverAlInicio();
        }
    }

    public async Task Organizar(IReadOnlyList<FilaPlan> filas, string origen, string destino, bool descartesAPapelera)
    {
        var resultado = new ResultadoViewModel(this, destino);
        IrA(resultado, 4);
        await resultado.Ejecutar(filas, origen, descartesAPapelera);
    }

    public async Task Deshacer(string rutaRegistro)
    {
        var resultado = new ResultadoViewModel(this, destino: null);
        IrA(resultado, 4);
        await resultado.Deshacer(rutaRegistro);
    }

    void MarcarPaso(int numero)
    {
        foreach (var p in Pasos)
        {
            p.EsActual = p.Numero == numero;
            p.EsHecho = p.Numero < numero;
        }
    }
}

public partial class PasoVista(int numero, string nombre) : ObservableObject
{
    public int Numero { get; } = numero;

    public string Nombre { get; } = nombre;

    [ObservableProperty]
    public partial bool EsActual { get; set; }

    [ObservableProperty]
    public partial bool EsHecho { get; set; }
}
