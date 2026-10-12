namespace BackupTidy.Tests;

/// <summary>Carpeta temporal para pruebas con archivos reales; se borra al terminar.</summary>
sealed class CarpetaTemporal : IDisposable
{
    public string Ruta { get; } = Directory.CreateTempSubdirectory("backuptidy-").FullName;

    public string this[string relativa] => Path.Combine(Ruta, relativa);

    public string Crear(string relativa, byte[] contenido, DateTime? modificacion = null)
    {
        var ruta = this[relativa];
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllBytes(ruta, contenido);
        if (modificacion is DateTime m) File.SetLastWriteTime(ruta, m);
        return ruta;
    }

    public string Crear(string relativa, string contenido) => Crear(relativa, System.Text.Encoding.UTF8.GetBytes(contenido));

    public void Dispose() => Directory.Delete(Ruta, recursive: true);
}
