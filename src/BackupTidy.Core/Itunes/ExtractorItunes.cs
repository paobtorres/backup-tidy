namespace BackupTidy.Core.Itunes;

/// <param name="Copiados">Archivos copiados ahora.</param>
/// <param name="YaEstaban">Ya extraidos en una vez anterior (mismo nombre y tamano): no se copian de nuevo.</param>
/// <param name="Errores">Un renglon por archivo que no se pudo copiar.</param>
public sealed record ResultadoExtraccion(int Copiados, int YaEstaban, long BytesCopiados, IReadOnlyList<string> Errores);

/// <summary>
/// Copia los archivos de una copia de iTunes a una carpeta comun, con su nombre y fecha reales, para analizarla
/// como cualquier backup. Copia (no mueve): la copia de iTunes queda igual.
/// </summary>
/// <remarks>
/// Se puede cancelar y retomar: cada archivo se copia como <c>.parcial</c> y se renombra al terminar, asi que
/// lo que esta con su nombre esta completo y la proxima vez se saltea.
/// </remarks>
public static class ExtractorItunes
{
    const string Parcial = ".parcial";

    /// <summary>Bytes que falta copiar: lo extraido en una vez anterior no cuenta.</summary>
    public static long BytesPendientes(IEnumerable<ArchivoItunes> archivos, string salida) =>
        archivos.Where(a => !YaExtraido(a, Path.Combine(salida, a.Destino))).Sum(a => a.Bytes);

    /// <exception cref="IOException">Se lleno el disco: se detiene en vez de fallar archivo por archivo.</exception>
    /// <exception cref="OperationCanceledException">Se cancelo; lo copiado hasta ahi queda y se retoma.</exception>
    public static ResultadoExtraccion Extraer(IReadOnlyList<ArchivoItunes> archivos, string salida,
                                              IProgress<(int Hechos, int Total)>? progreso = null,
                                              CancellationToken cancelar = default)
    {
        int copiados = 0, yaEstaban = 0;
        long bytes = 0;
        var errores = new List<string>();
        for (var i = 0; i < archivos.Count; i++)
        {
            cancelar.ThrowIfCancellationRequested();
            var archivo = archivos[i];
            var destino = Path.Combine(salida, archivo.Destino);
            if (YaExtraido(archivo, destino))
            {
                yaEstaban++;
            }
            else
            {
                var parcial = destino + Parcial;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                    File.Copy(archivo.Origen, parcial, overwrite: true);
                    // La fecha del iPhone: sin esto todo quedaria con la de hoy y el analisis la tomaria por dudosa
                    if (archivo.Creado is DateTime creado) File.SetCreationTimeUtc(parcial, creado);
                    if (archivo.Modificado is DateTime modificado) File.SetLastWriteTimeUtc(parcial, modificado);
                    // Sin sobrescribir: si ya hay otro archivo con ese nombre, queda como error
                    File.Move(parcial, destino);
                    copiados++;
                    bytes += archivo.Bytes;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Borrar(parcial);
                    if (DiscoLleno(e)) throw new IOException($"Se llenó el disco de {salida}. Lo copiado hasta ahora queda; libera espacio y vuelve a extraer para seguir.", e);
                    errores.Add($"{archivo.Destino}: {e.Message}");
                }
            }
            progreso?.Report((i + 1, archivos.Count));
        }
        return new ResultadoExtraccion(copiados, yaEstaban, bytes, errores);
    }

    static bool YaExtraido(ArchivoItunes archivo, string destino)
    {
        var info = new FileInfo(destino);
        return info.Exists && info.Length == archivo.Bytes;
    }

    // ERROR_HANDLE_DISK_FULL (39) y ERROR_DISK_FULL (112)
    static bool DiscoLleno(Exception e) => (e.HResult & 0xFFFF) is 39 or 112;

    static void Borrar(string ruta)
    {
        try
        {
            File.Delete(ruta);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Si no se puede, queda un .parcial que la proxima extraccion pisa
        }
    }
}
