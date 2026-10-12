using System.Text.RegularExpressions;

namespace BackupTidy.Core.Clasificacion;

/// <summary>
/// Reglas de clasificacion de docs/ESPECIFICACION.md (portadas de organizar_backup.ps1).
/// Funciones puras: no leen el disco. Lo que depende del contenido del archivo llega en <see cref="DatosMedia"/>.
/// </summary>
/// <remarks>
/// Las reglas por carpeta (App Photos, WhatsApp, Messages...) se evaluan sobre la ruta relativa al origen,
/// no sobre la ruta completa como hacia el script: asi un origen como <c>D:\WhatsApp backup\</c> no
/// convierte todo en WhatsApp.
/// </remarks>
public static class Clasificador
{
    public const long MaxTamanoLivePhoto = 30L * 1024 * 1024;

    static readonly HashSet<string> ExtFoto = Conjunto(
        ".jpg", ".jpeg", ".heic", ".heif", ".png", ".dng", ".gif", ".webp", ".tif", ".tiff", ".bmp",
        ".nef", ".cr2", ".cr3", ".arw", ".orf", ".raf", ".rw2");
    static readonly HashSet<string> ExtVideo = Conjunto(
        ".mov", ".mp4", ".m4v", ".3gp", ".avi", ".mkv", ".mpg", ".mpeg", ".wmv");
    // Notas de voz, audios de apps, contactos, calendario, ediciones de iPhone, stickers animados de WhatsApp
    static readonly HashSet<string> ExtDescarte = Conjunto(
        ".opus", ".m4a", ".mp3", ".wav", ".caf", ".amr", ".vcf", ".ics", ".aae", ".favicon", ".was", ".identifier");
    static readonly HashSet<string> NombresDescarte = Conjunto("thumbs.db", "desktop.ini", ".picasa.ini");

    static readonly HashSet<string> ExtPdf = Conjunto(".pdf");
    static readonly HashSet<string> ExtOffice = Conjunto(
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pbix", ".odt", ".ods", ".odp", ".rtf");
    static readonly HashSet<string> ExtComprimidos = Conjunto(".zip", ".rar", ".7z", ".gz", ".tar");

    // Formatos donde la falta de marca de camara indica una imagen guardada (no sacada con el celular)
    static readonly HashSet<string> ExtGuardadas = Conjunto(".jpg", ".jpeg", ".tif", ".tiff");
    static readonly HashSet<string> ExtFotoLive = Conjunto(".heic", ".heif", ".jpg", ".jpeg");

    const RegexOptions Opciones = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    static readonly Regex RxCaptura = new("screenshot|screen shot|captura|screen_recording|rpreplay", Opciones);
    // Vistas previas que crea el programa de restore: IMG_1234.HEIC.jpg junto a IMG_1234.HEIC
    static readonly Regex RxVistaPrevia = new(@"\.(hei[cf]|png|dng|mov|mp4)\.jpe?g$", Opciones);
    static readonly Regex RxExtJpg = new(@"\.jpe?g$", Opciones);
    static readonly Regex RxMiniatura = new(@"\.thm\.jpe?g$", Opciones);

    static readonly char[] Separadores = ['\\', '/'];

    public static TipoArchivo TipoDe(string ruta)
    {
        var nombre = NombreDe(ruta);
        var ext = Path.GetExtension(nombre);
        if (ExtFoto.Contains(ext)) return TipoArchivo.Foto;
        if (ExtVideo.Contains(ext)) return TipoArchivo.Video;
        if (ExtDescarte.Contains(ext) || NombresDescarte.Contains(nombre)) return TipoArchivo.Descarte;
        return TipoArchivo.Documento;
    }

    public static TipoDocumento TipoDocumentoDe(string ruta)
    {
        var ext = Path.GetExtension(NombreDe(ruta));
        if (ExtPdf.Contains(ext)) return TipoDocumento.PDF;
        if (ExtOffice.Contains(ext)) return TipoDocumento.Office;
        if (ExtComprimidos.Contains(ext)) return TipoDocumento.Comprimidos;
        return TipoDocumento.Otros;
    }

    /// <summary>
    /// Categoria de una foto o video que no es duplicado ni vista previa (eso se resuelve antes, en el analisis).
    /// </summary>
    public static CategoriaMedia CategoriaDe(DatosMedia media)
    {
        var tipo = TipoDe(media.RutaRelativa);
        if (tipo is not (TipoArchivo.Foto or TipoArchivo.Video))
            throw new ArgumentException($"No es foto ni video: {media.RutaRelativa}", nameof(media));

        var nombre = NombreDe(media.RutaRelativa);
        var carpetas = CarpetasDe(media.RutaRelativa);

        if (carpetas.Any(c => c.Equals("App Photos", StringComparison.OrdinalIgnoreCase))) return CategoriaMedia.Apps;
        if (carpetas.Any(EsCarpetaWhatsApp)) return CategoriaMedia.WhatsApp;
        if (RxMiniatura.IsMatch(nombre)) return CategoriaMedia.Miniaturas;
        if (RxCaptura.IsMatch(nombre)) return CategoriaMedia.Capturas;
        if (carpetas.Any(EsCarpetaMensajes)) return CategoriaMedia.Mensajes;

        if (tipo == TipoArchivo.Video)
            return media.TieneFotoLive && media.Tamano <= MaxTamanoLivePhoto ? CategoriaMedia.LivePhotos : CategoriaMedia.Videos;

        if (!media.TieneMarcaCamara)
        {
            var ext = Path.GetExtension(nombre);
            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)) return CategoriaMedia.Capturas;
            if (ExtGuardadas.Contains(ext)) return CategoriaMedia.Guardadas;
        }
        return CategoriaMedia.Fotos;
    }

    /// <summary>Si la ruta es una vista previa (<c>IMG_1234.HEIC.jpg</c>), la ruta del original; si no, null.</summary>
    /// <remarks>Solo es descarte si el original existe al lado: eso lo verifica el analisis.</remarks>
    public static string? OriginalDeVistaPrevia(string ruta) =>
        RxVistaPrevia.IsMatch(NombreDe(ruta)) ? RxExtJpg.Replace(ruta, "") : null;

    /// <summary>Si la foto cuenta para emparejar videos como Live Photo.</summary>
    public static bool PuedeSerFotoLive(string ruta) => ExtFotoLive.Contains(Path.GetExtension(NombreDe(ruta)));

    /// <summary>
    /// Clave para emparejar <c>IMG_1234.HEIC</c> con <c>IMG_1234.MOV</c>: primeros 2 niveles de la ruta
    /// relativa (identifican el backup) + nombre sin extension.
    /// </summary>
    public static string ClaveLive(string rutaRelativa)
    {
        var partes = rutaRelativa.Split(Separadores, StringSplitOptions.RemoveEmptyEntries);
        var raiz = string.Join('/', partes.Take(Math.Min(2, partes.Length - 1)));
        return $"{raiz}|{Path.GetFileNameWithoutExtension(partes[^1])}".ToLowerInvariant();
    }

    /// <summary>Esta en una carpeta de mensajeria (Messages, Attachments, WhatsApp, Telegram...).</summary>
    public static bool EstaEnMensajes(string rutaRelativa) =>
        CarpetasDe(rutaRelativa).Any(c => EsCarpetaMensajes(c) || EsCarpetaWhatsApp(c));

    static bool EsCarpetaWhatsApp(string carpeta) => carpeta.StartsWith("WhatsApp", StringComparison.OrdinalIgnoreCase);

    static bool EsCarpetaMensajes(string carpeta) =>
        carpeta.Equals("Messages", StringComparison.OrdinalIgnoreCase)
        || carpeta.Equals("Message Attachments", StringComparison.OrdinalIgnoreCase)
        || carpeta.Equals("Attachments", StringComparison.OrdinalIgnoreCase)
        || carpeta.StartsWith("Telegram", StringComparison.OrdinalIgnoreCase);

    static string NombreDe(string ruta) => ruta[(ruta.LastIndexOfAny(Separadores) + 1)..];

    static string[] CarpetasDe(string ruta) => ruta.Split(Separadores, StringSplitOptions.RemoveEmptyEntries)[..^1];

    static HashSet<string> Conjunto(params string[] valores) => new(valores, StringComparer.OrdinalIgnoreCase);
}
