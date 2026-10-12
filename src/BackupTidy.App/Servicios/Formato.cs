using System.Globalization;
using BackupTidy.Core.Fechas;
using BackupTidy.Core.Plan;

namespace BackupTidy.App.Servicios;

/// <summary>Textos para mostrar al usuario.</summary>
public static class Formato
{
    public static string Tamano(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("N1", CultureInfo.CurrentCulture) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("N0", CultureInfo.CurrentCulture) + " MB",
        >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("N0", CultureInfo.CurrentCulture) + " KB",
        _ => bytes + " bytes",
    };

    public static string Cantidad(int n) => n.ToString("N0", CultureInfo.CurrentCulture);

    public static string Categoria(CategoriaPlan c) => c switch
    {
        CategoriaPlan.Fotos => "Fotos",
        CategoriaPlan.Videos => "Videos",
        CategoriaPlan.LivePhotos => "Live Photos",
        CategoriaPlan.Capturas => "Capturas",
        CategoriaPlan.WhatsApp => "WhatsApp",
        CategoriaPlan.Guardadas => "Guardadas",
        CategoriaPlan.Mensajes => "Mensajes",
        CategoriaPlan.Apps => "Apps",
        CategoriaPlan.Miniaturas => "Miniaturas",
        CategoriaPlan.Duplicados => "Duplicados",
        CategoriaPlan.Documentos => "Documentos",
        CategoriaPlan.DocumentosDuplicados => "Documentos duplicados",
        CategoriaPlan.JpgReemplazado => "JPG reemplazados",
        CategoriaPlan.Descartar => "Descartes",
        _ => c.ToString(),
    };

    public static string DescripcionCategoria(CategoriaPlan c) => c switch
    {
        CategoriaPlan.Fotos => "Sacadas con la cámara",
        CategoriaPlan.Videos => "Videos de la cámara",
        CategoriaPlan.LivePhotos => "El video corto de cada Live Photo",
        CategoriaPlan.Capturas => "Capturas y grabaciones de pantalla",
        CategoriaPlan.WhatsApp => "Recibidas y enviadas por WhatsApp",
        CategoriaPlan.Guardadas => "Descargas e imágenes sin cámara",
        CategoriaPlan.Mensajes => "Adjuntos de Mensajes y Telegram",
        CategoriaPlan.Apps => "Imágenes internas de apps",
        CategoriaPlan.Miniaturas => "Miniaturas de videos (.THM)",
        CategoriaPlan.Duplicados => "Van a revisión, no se borran",
        CategoriaPlan.Documentos => "PDF, Office, comprimidos y otros",
        CategoriaPlan.DocumentosDuplicados => "Van a revisión, no se borran",
        CategoriaPlan.JpgReemplazado => "Su HEIC original los reemplaza",
        CategoriaPlan.Descartar => "Audios, contactos, ediciones… quedan en su lugar",
        _ => "",
    };

    public static string Fuente(FuenteFecha? fuente) => fuente switch
    {
        FuenteFecha.Exif => "fecha de la cámara (EXIF)",
        FuenteFecha.Metadatos => "fecha guardada en el archivo",
        FuenteFecha.Nombre => "fecha del nombre del archivo",
        FuenteFecha.Archivo => "fecha de modificación del archivo",
        FuenteFecha.ArchivoDudoso => "fecha dudosa: probablemente la del restore",
        _ => "sin fecha",
    };

    public static string Fecha(DateTime? fecha) => fecha?.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture) ?? "—";
}
