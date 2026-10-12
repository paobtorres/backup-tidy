using System.Globalization;
using System.Text.RegularExpressions;

namespace BackupTidy.Core.Duplicados;

public enum AccionMismaFoto
{
    Ninguna,
    /// <summary>Entra un HEIC y lo organizado tiene su JPG convertido: el JPG sale a <c>_jpg_reemplazados</c>.</summary>
    ReemplazaJpg,
    /// <summary>Mismo nombre base y mes que algo ya organizado: va a <c>_duplicados_probables</c>.</summary>
    Probable,
}

/// <param name="Organizados">Rutas completas: los JPG a reemplazar o el archivo parecido.</param>
public sealed record ResultadoMismaFoto(AccionMismaFoto Accion, IReadOnlyList<string> Organizados)
{
    public static readonly ResultadoMismaFoto Ninguna = new(AccionMismaFoto.Ninguna, []);
}

/// <summary>
/// Misma foto en otro formato: FoneLab puede exportar HEIC convertidos a JPG, que no son iguales por hash.
/// Indexa lo ya organizado por "AAAA-MM | nombre base".
/// </summary>
public sealed class IndiceOrganizado
{
    // Solo lo organizado con fecha fiable: Categoria\AAAA\AAAA-MM\nombre (excluye _fecha_dudosa y revision)
    static readonly Regex RxOrganizado = new(@"^(Fotos|Videos|LivePhotos)[\\/]\d{4}[\\/](\d{4}-\d{2})[\\/]([^\\/]+)$",
                                             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex RxSufijoCopia = new(@"_\d$", RegexOptions.CultureInvariant);

    readonly Dictionary<string, List<(string Ruta, bool EsJpgDeFotos)>> porClave = [];

    public IndiceOrganizado(IEnumerable<ArchivoComparable> referencias)
    {
        foreach (var r in referencias)
        {
            var m = RxOrganizado.Match(r.RutaRelativa);
            if (!m.Success) continue;
            var clave = $"{m.Groups[2].Value}|{NombreBase(m.Groups[3].Value)}";
            if (!porClave.TryGetValue(clave, out var lista)) porClave[clave] = lista = [];
            var ext = Path.GetExtension(m.Groups[3].Value);
            lista.Add((r.Ruta, m.Groups[1].Value.Equals("Fotos", StringComparison.OrdinalIgnoreCase)
                               && (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))));
        }
    }

    /// <summary><c>IMG_1234.HEIC.jpg</c> / <c>IMG_1234_2.JPG</c> -> <c>img_1234</c>.</summary>
    public static string NombreBase(string nombre)
    {
        var punto = nombre.IndexOf('.');
        if (punto > 0) nombre = nombre[..punto];
        return RxSufijoCopia.Replace(nombre, "").ToLowerInvariant();
    }

    /// <summary>
    /// Compara un archivo nuevo con lo organizado. Usar solo para Fotos, Videos y LivePhotos con fecha
    /// de EXIF o metadatos (con otra fuente el mes no es fiable). Si varios HEIC reemplazan el mismo JPG,
    /// quien arma el plan debe moverlo una sola vez.
    /// </summary>
    /// <param name="extension">Extension efectiva del archivo nuevo (ver <c>Clasificador.ExtensionEfectiva</c>).</param>
    public ResultadoMismaFoto Evaluar(string nombre, string extension, DateTime fecha)
    {
        var clave = $"{fecha.ToString("yyyy-MM", CultureInfo.InvariantCulture)}|{NombreBase(nombre)}";
        if (!porClave.TryGetValue(clave, out var similares)) return ResultadoMismaFoto.Ninguna;

        var esHeic = extension.Equals(".heic", StringComparison.OrdinalIgnoreCase) || extension.Equals(".heif", StringComparison.OrdinalIgnoreCase);
        var jpgs = similares.Where(s => s.EsJpgDeFotos).Select(s => s.Ruta).ToList();
        if (esHeic && jpgs.Count > 0) return new(AccionMismaFoto.ReemplazaJpg, jpgs);
        return new(AccionMismaFoto.Probable, [similares[0].Ruta]);
    }
}
