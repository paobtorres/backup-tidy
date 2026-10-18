using BackupTidy.Core.Clasificacion;
using BackupTidy.Core.Fechas;

namespace BackupTidy.Core.Plan;

/// <summary>Categorias del plan. Los nombres son los del CSV del script de referencia.</summary>
public enum CategoriaPlan
{
    Fotos,
    Videos,
    LivePhotos,
    Capturas,
    WhatsApp,
    Guardadas,
    Mensajes,
    Apps,
    Miniaturas,
    /// <summary>Duplicado exacto, vista previa convertida o duplicado probable (ver el destino).</summary>
    Duplicados,
    Documentos,
    DocumentosDuplicados,
    /// <summary>JPG ya organizado que sale a revision porque entra su HEIC original.</summary>
    JpgReemplazado,
    /// <summary>Queda en su lugar, o va a la Papelera si se elige al ejecutar.</summary>
    Descartar,
}

/// <summary>Un movimiento previsto. No se ejecuta nada hasta que el usuario revisa el plan.</summary>
/// <param name="Destino">Ruta completa de destino; null en los descartes.</param>
/// <param name="Bytes">Tamano al analizar: si cambia antes de ejecutar, el archivo no se mueve.</param>
/// <param name="Relacionado">
/// Ruta completa del otro archivo que explica la decision: la copia que se conserva, la ya organizada,
/// el original de una vista previa, el parecido o el HEIC que reemplaza a un JPG. Para revisar lado a lado.
/// </param>
public sealed record FilaPlan(CategoriaPlan Categoria, string Origen, string? Destino, long Bytes,
                              DateTime? Fecha = null, FuenteFecha? FuenteFecha = null, string? Hash = null, string Nota = "",
                              string? Relacionado = null)
{
    /// <summary>Copia de otro archivo (del backup o ya organizado), vista previa convertida o duplicado probable.</summary>
    public bool EsDuplicado => Categoria is CategoriaPlan.Duplicados or CategoriaPlan.DocumentosDuplicados;

    public static CategoriaPlan CategoriaDeMedia(CategoriaMedia categoria) => categoria switch
    {
        CategoriaMedia.Apps => CategoriaPlan.Apps,
        CategoriaMedia.WhatsApp => CategoriaPlan.WhatsApp,
        CategoriaMedia.Miniaturas => CategoriaPlan.Miniaturas,
        CategoriaMedia.Capturas => CategoriaPlan.Capturas,
        CategoriaMedia.Mensajes => CategoriaPlan.Mensajes,
        CategoriaMedia.LivePhotos => CategoriaPlan.LivePhotos,
        CategoriaMedia.Guardadas => CategoriaPlan.Guardadas,
        CategoriaMedia.Fotos => CategoriaPlan.Fotos,
        CategoriaMedia.Videos => CategoriaPlan.Videos,
        _ => throw new ArgumentOutOfRangeException(nameof(categoria), categoria, null),
    };
}
