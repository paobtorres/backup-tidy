using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Xmp;
using MetadataExtractor.Util;

namespace BackupTidy.Core.Fechas;

/// <summary>
/// Lee fechas y marca de camara con MetadataExtractor, sin decodificar la imagen.
/// Nunca falla: un archivo ilegible o corrupto devuelve <see cref="MetadatosMedia.Vacio"/>.
/// </summary>
public static class LectorMetadatos
{
    // En orden de preferencia; solo propiedades de primer nivel (no las de documentos incrustados)
    static readonly string[] ClavesXmp = ["exif:DateTimeOriginal", "photoshop:DateCreated", "xmp:CreateDate"];

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
        // El formato se detecta aparte para conocerlo aunque los metadatos esten danados
        string? extensionContenido = null;
        IReadOnlyList<MetadataExtractor.Directory> directorios;
        try
        {
            var formato = FileTypeDetector.DetectFileType(contenido);
            if (formato.GetCommonExtension() is string ext) extensionContenido = "." + ext;
            contenido.Position = 0;
            directorios = ImageMetadataReader.ReadMetadata(contenido);
        }
        catch (Exception)
        {
            // Formato desconocido o archivo danado: en backups de 170.000 archivos siempre hay alguno
            return MetadatosMedia.Vacio with { ExtensionContenido = extensionContenido };
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
        // XMP: PNG y webp editados (stickers, fondos, adjuntos) suelen tener la fecha solo aca
        foreach (var d in directorios.OfType<XmpDirectory>())
        {
            var propiedades = d.GetXmpProperties();
            foreach (var clave in ClavesXmp)
                if (propiedades.TryGetValue(clave, out var valor) && ReglasFecha.ParsearXmp(valor) is DateTime fecha)
                    fechasContenedor.Add(fecha);
        }

        var marca = ifd0.Select(d => d.GetString(ExifDirectoryBase.TagMake)?.Trim('\0', ' '))
                        .FirstOrDefault(m => !string.IsNullOrEmpty(m));

        return new MetadatosMedia(fechasExif, fechasContenedor, marca, extensionContenido);
    }

    static void AgregarExif(List<DateTime> fechas, IEnumerable<ExifDirectoryBase> directorios, int etiqueta)
    {
        foreach (var d in directorios)
            if (ReglasFecha.ParsearExif(d.GetString(etiqueta)) is DateTime fecha)
                fechas.Add(fecha);
    }
}
