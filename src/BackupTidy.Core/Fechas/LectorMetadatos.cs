using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace BackupTidy.Core.Fechas;

/// <summary>
/// Lee fechas y marca de camara con MetadataExtractor, sin decodificar la imagen.
/// Nunca falla: un archivo ilegible o corrupto devuelve <see cref="MetadatosMedia.Vacio"/>.
/// </summary>
public static class LectorMetadatos
{
    public static MetadatosMedia Leer(string ruta)
    {
        try
        {
            using var archivo = File.OpenRead(ruta);
            return Leer(archivo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return MetadatosMedia.Vacio;
        }
    }

    public static MetadatosMedia Leer(Stream contenido)
    {
        IReadOnlyList<MetadataExtractor.Directory> directorios;
        try
        {
            directorios = ImageMetadataReader.ReadMetadata(contenido);
        }
        catch (Exception)
        {
            // Formato desconocido o archivo danado: en backups de 170.000 archivos siempre hay alguno
            return MetadatosMedia.Vacio;
        }

        var subIfd = directorios.OfType<ExifSubIfdDirectory>().ToList();
        var ifd0 = directorios.OfType<ExifIfd0Directory>().ToList();

        var fechasExif = new List<DateTime>();
        AgregarExif(fechasExif, subIfd, ExifDirectoryBase.TagDateTimeOriginal);
        AgregarExif(fechasExif, subIfd, ExifDirectoryBase.TagDateTimeDigitized);
        AgregarExif(fechasExif, ifd0, ExifDirectoryBase.TagDateTime);

        var fechasContenedor = new List<DateTime>();
        // com.apple.quicktime.creationdate trae zona horaria; MetadataExtractor ya la convierte a hora local
        foreach (var d in directorios.OfType<QuickTimeMetadataHeaderDirectory>())
            if (d.TryGetDateTime(QuickTimeMetadataHeaderDirectory.TagCreationDate, out var fecha))
                fechasContenedor.Add(fecha);
        // La fecha de creacion de la cabecera del video esta en UTC
        foreach (var d in directorios.OfType<QuickTimeMovieHeaderDirectory>())
            if (d.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var fecha))
                fechasContenedor.Add(DateTime.SpecifyKind(fecha, DateTimeKind.Utc).ToLocalTime());

        var marca = ifd0.Select(d => d.GetString(ExifDirectoryBase.TagMake)?.Trim('\0', ' '))
                        .FirstOrDefault(m => !string.IsNullOrEmpty(m));

        return new MetadatosMedia(fechasExif, fechasContenedor, marca);
    }

    static void AgregarExif(List<DateTime> fechas, IEnumerable<ExifDirectoryBase> directorios, int etiqueta)
    {
        foreach (var d in directorios)
            if (ReglasFecha.ParsearExif(d.GetString(etiqueta)) is DateTime fecha)
                fechas.Add(fecha);
    }
}
