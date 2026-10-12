namespace BackupTidy.Core.Clasificacion;

public enum TipoArchivo
{
    Foto,
    Video,
    /// <summary>Notas de voz, contactos, ediciones de iOS, stickers... Van a la Papelera, no al destino.</summary>
    Descarte,
    /// <summary>Todo lo que no es foto, video ni descarte.</summary>
    Documento,
}

/// <summary>Subcarpeta dentro de <c>Documentos\</c>.</summary>
public enum TipoDocumento
{
    PDF,
    Office,
    Comprimidos,
    Otros,
}

/// <summary>Carpeta de destino de una foto o video. El orden es la prioridad de las reglas.</summary>
public enum CategoriaMedia
{
    /// <summary>Cache interna de apps: la ruta contiene <c>App Photos\</c>.</summary>
    Apps,
    WhatsApp,
    /// <summary><c>*.THM.jpg</c>.</summary>
    Miniaturas,
    /// <summary>Capturas y grabaciones de pantalla, o PNG sin marca de camara.</summary>
    Capturas,
    /// <summary>Adjuntos de Messages / Telegram.</summary>
    Mensajes,
    /// <summary>Video corto con una foto del mismo nombre en el mismo backup.</summary>
    LivePhotos,
    /// <summary>JPG/TIFF sin marca de camara: descargas, WhatsApp guardado en el carrete.</summary>
    Guardadas,
    Fotos,
    Videos,
}

/// <summary>Lo que necesita el clasificador de una foto o video.</summary>
/// <param name="RutaRelativa">Ruta relativa a la carpeta de origen (separador <c>\</c> o <c>/</c>).</param>
/// <param name="Tamano">Tamano en bytes.</param>
/// <param name="TieneMarcaCamara">El EXIF / metadatos indican fabricante de camara.</param>
/// <param name="TieneFotoLive">
/// Existe en el origen una foto con la misma <see cref="Clasificador.ClaveLive"/> (solo importa en videos).
/// </param>
public sealed record DatosMedia(string RutaRelativa, long Tamano, bool TieneMarcaCamara, bool TieneFotoLive);
