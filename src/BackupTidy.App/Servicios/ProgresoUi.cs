using Avalonia.Threading;

namespace BackupTidy.App.Servicios;

/// <summary>
/// Recibe el progreso desde el hilo de trabajo y lo pasa a la interfaz como mucho cada
/// <paramref name="intervaloMs"/> ms: el motor informa por cada archivo (decenas de miles por segundo).
/// El ultimo valor puede no llegar; quien lo usa fija el estado final al terminar.
/// </summary>
public sealed class ProgresoUi<T>(Action<T> aplicar, int intervaloMs = 80) : IProgress<T>
{
    long ultimo = long.MinValue;

    public void Report(T valor)
    {
        var ahora = Environment.TickCount64;
        if (ahora - Interlocked.Read(ref ultimo) < intervaloMs) return;
        Interlocked.Exchange(ref ultimo, ahora);
        Dispatcher.UIThread.Post(() => aplicar(valor));
    }
}
