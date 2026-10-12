using System.Globalization;
using System.Text.RegularExpressions;

namespace BackupTidy.Core.Fechas;

/// <summary>De donde salio la fecha que se usa para ordenar un archivo.</summary>
public enum FuenteFecha
{
    /// <summary>EXIF DateTimeOriginal / DateTimeDigitized / DateTime (hora local de la camara).</summary>
    Exif,
    /// <summary>Metadatos del contenedor de video (QuickTime / MP4).</summary>
    Metadatos,
    /// <summary>Fecha en el nombre: IMG-20190315-WA0001, Screenshot_2019-03-15...</summary>
    Nombre,
    /// <summary>Fecha de modificacion del archivo.</summary>
    Archivo,
    /// <summary>Fecha de modificacion en una carpeta restaurada: probablemente es la del restore.</summary>
    ArchivoDudoso,
}

public readonly record struct FechaArchivo(DateTime Fecha, FuenteFecha Fuente);

/// <summary>Lo leido del contenido del archivo. Las fechas son candidatas en orden de preferencia, sin validar.</summary>
/// <param name="ExtensionContenido">
/// Extension que corresponde al formato real segun los primeros bytes (".jpg", ".mov", ".heic"...), o null si no se reconoce.
/// </param>
public sealed record MetadatosMedia(IReadOnlyList<DateTime> FechasExif, IReadOnlyList<DateTime> FechasContenedor,
                                    string? MarcaCamara, string? ExtensionContenido = null)
{
    public static readonly MetadatosMedia Vacio = new([], [], null);

    public bool TieneMarcaCamara => !string.IsNullOrWhiteSpace(MarcaCamara);
}

/// <summary>Reglas de fecha de docs/ESPECIFICACION.md. Funciones puras.</summary>
public static class ReglasFecha
{
    public static readonly DateTime Minima = new(2000, 1, 1);

    /// <summary>Una carpeta con al menos tantos archivos...</summary>
    public const int MinArchivosCarpetaDudosa = 20;
    /// <summary>...y al menos esta proporcion con el mismo dia de modificacion es una carpeta restaurada.</summary>
    public const double ProporcionCarpetaDudosa = 0.5;

    static readonly Regex RxFechaNombre = new(
        @"(?<!\d)((?:19|20)\d{2})[-_.]?(0[1-9]|1[0-2])[-_.]?(0[1-9]|[12]\d|3[01])(?!\d)", RegexOptions.CultureInvariant);

    static readonly string[] FormatosExif = ["yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy:MM:dd HH:mm"];

    /// <summary>Rango aceptado: desde 2000-01-01 hasta manana (tolera relojes y zonas horarias adelantadas).</summary>
    public static bool EsValida(DateTime fecha, DateTime ahora) => fecha >= Minima && fecha <= ahora.AddDays(1);

    /// <summary>Primera fecha AAAAMMDD real que aparece en el nombre (sin hora), o null.</summary>
    public static DateTime? FechaDeNombre(string nombreArchivo)
    {
        for (var m = RxFechaNombre.Match(nombreArchivo); m.Success; m = m.NextMatch())
        {
            int anio = int.Parse(m.Groups[1].ValueSpan), mes = int.Parse(m.Groups[2].ValueSpan), dia = int.Parse(m.Groups[3].ValueSpan);
            // La regex acepta 2019-02-31: se descarta y se sigue buscando
            if (dia <= DateTime.DaysInMonth(anio, mes)) return new DateTime(anio, mes, dia);
        }
        return null;
    }

    /// <summary>Fecha EXIF en formato <c>yyyy:MM:dd HH:mm:ss</c> (hora local), o null si no es una fecha.</summary>
    public static DateTime? ParsearExif(string? texto)
    {
        texto = texto?.Trim('\0', ' ');
        if (string.IsNullOrEmpty(texto)) return null;
        return DateTime.TryParseExact(texto, FormatosExif, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>
    /// Fecha XMP (<c>2016-11-17T13:49:09+01:00</c>, con o sin zona y hora) tal como esta escrita: la hora local
    /// de quien la creo, igual que en EXIF. Null si no es una fecha.
    /// </summary>
    public static DateTime? ParsearXmp(string? texto) =>
        DateTimeOffset.TryParse(texto?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d.DateTime : null;

    /// <summary>
    /// Elige la fecha con la prioridad de la especificacion: EXIF, metadatos del contenedor, nombre y,
    /// como ultimo recurso, la fecha de modificacion (marcada como dudosa si la carpeta fue restaurada).
    /// </summary>
    public static FechaArchivo Resolver(MetadatosMedia metadatos, string nombreArchivo, DateTime fechaModificacion,
                                        bool carpetaDudosa, DateTime ahora)
    {
        foreach (var d in metadatos.FechasExif)
            if (EsValida(d, ahora)) return new(d, FuenteFecha.Exif);
        foreach (var d in metadatos.FechasContenedor)
            if (EsValida(d, ahora)) return new(d, FuenteFecha.Metadatos);
        if (FechaDeNombre(nombreArchivo) is DateTime n && EsValida(n, ahora)) return new(n, FuenteFecha.Nombre);
        return new(fechaModificacion, carpetaDudosa ? FuenteFecha.ArchivoDudoso : FuenteFecha.Archivo);
    }

    /// <summary>
    /// Carpetas restauradas: muchos archivos y la mayoria con el mismo dia de modificacion (el del restore).
    /// Recibe las fotos y videos del origen con la carpeta que los contiene.
    /// </summary>
    public static HashSet<string> CarpetasConFechaDudosa(IEnumerable<(string Carpeta, DateTime FechaModificacion)> archivos)
    {
        var dudosas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var carpeta in archivos.GroupBy(a => a.Carpeta, StringComparer.OrdinalIgnoreCase))
        {
            var total = carpeta.Count();
            var maxMismoDia = carpeta.GroupBy(a => a.FechaModificacion.Date).Max(g => g.Count());
            if (total >= MinArchivosCarpetaDudosa && maxMismoDia >= total * ProporcionCarpetaDudosa)
                dudosas.Add(carpeta.Key);
        }
        return dudosas;
    }
}
