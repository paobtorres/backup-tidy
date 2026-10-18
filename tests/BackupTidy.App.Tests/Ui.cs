using Avalonia;
using Avalonia.Headless;
using BackupTidy.App;
using BackupTidy.App.Servicios;

// Todas las pruebas comparten el hilo de la interfaz: una a la vez
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BackupTidy.App.Tests;

/// <summary>Corre las pruebas en el hilo de la interfaz de Avalonia, sin ventana real (headless).</summary>
static class Ui
{
    static readonly HeadlessUnitTestSession Sesion = HeadlessUnitTestSession.StartNew(typeof(Ui));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public static Task Ejecutar(Func<Task> prueba) =>
        Sesion.Dispatch(async () =>
        {
            await prueba();
            return true;
        }, CancellationToken.None);

    /// <summary>Espera a que el view model termine lo que hace en segundo plano.</summary>
    public static async Task Esperar(Func<bool> condicion, string que)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException("No paso: " + que);
            await Task.Delay(20);
        }
    }
}

/// <summary>Dialogos que devuelven lo que la prueba diga, sin abrir nada.</summary>
sealed class DialogosDePrueba : IDialogos
{
    public Queue<string?> Carpetas { get; } = new();

    public string? Csv { get; set; }

    public Task<string?> ElegirCarpeta(string titulo, string? inicial) => Task.FromResult(Carpetas.Count > 0 ? Carpetas.Dequeue() : null);

    public Task<string?> ElegirCsv(string titulo, string? carpetaInicial) => Task.FromResult(Csv);

    public Task<string?> GuardarCsv(string titulo, string nombreSugerido, string? carpetaInicial) => Task.FromResult(Csv);

    public void MostrarEnExplorador(string ruta)
    {
    }
}
