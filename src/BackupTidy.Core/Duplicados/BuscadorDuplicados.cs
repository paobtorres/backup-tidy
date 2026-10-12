using System.Security.Cryptography;
using BackupTidy.Core.Clasificacion;

namespace BackupTidy.Core.Duplicados;

/// <summary>Archivo que participa en la busqueda de duplicados.</summary>
/// <param name="Ruta">Ruta completa.</param>
/// <param name="RutaRelativa">Relativa al origen, o a la carpeta organizada si es referencia.</param>
/// <param name="EsReferencia">Ya organizado (comparar con): nunca se mueve, solo detecta lo que ya existe.</param>
public sealed record ArchivoComparable(string Ruta, string RutaRelativa, long Tamano, bool EsReferencia = false);

public enum MotivoDuplicado
{
    /// <summary>Hay otra copia identica dentro del origen, que es la que se conserva.</summary>
    IgualA,
    /// <summary>El archivo ya esta en la carpeta organizada.</summary>
    YaExisteEn,
}

/// <param name="Original">La copia que se conserva: ruta relativa al origen (IgualA) o ruta completa (YaExisteEn).</param>
public sealed record Duplicado(MotivoDuplicado Motivo, string Original)
{
    public string Nota => Motivo == MotivoDuplicado.IgualA ? $"Igual a: {Original}" : $"Ya existe en: {Original}";
}

/// <param name="Duplicados">Por ruta completa: los archivos nuevos que sobran.</param>
/// <param name="Hashes">MD5 de cada archivo que se llego a comparar, por ruta completa.</param>
public sealed record ResultadoDuplicados(IReadOnlyDictionary<string, Duplicado> Duplicados, IReadOnlyDictionary<string, string> Hashes);

/// <summary>Duplicados exactos (mismo contenido) segun docs/ESPECIFICACION.md.</summary>
public static class BuscadorDuplicados
{
    /// <summary>
    /// Agrupa por tamano y calcula el hash solo en grupos con mas de un archivo y al menos uno nuevo
    /// (comparar organizados entre si no sirve). En cada grupo identico se conserva la copia "mas limpia"
    /// o, si ya existe en lo organizado, todos los nuevos son duplicados.
    /// </summary>
    /// <param name="calcularHash">Hash del contenido, o null si el archivo no se puede leer (se omite).</param>
    public static ResultadoDuplicados Buscar(IEnumerable<ArchivoComparable> archivos, Func<string, string?> calcularHash,
                                             IProgress<(int Hechos, int Total)>? progreso = null,
                                             CancellationToken cancelar = default)
    {
        var candidatos = archivos.GroupBy(a => a.Tamano)
                                 .Where(g => g.Count() > 1 && g.Any(a => !a.EsReferencia))
                                 .SelectMany(g => g)
                                 .ToList();

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var grupos = new Dictionary<(long, string), List<ArchivoComparable>>();
        for (var i = 0; i < candidatos.Count; i++)
        {
            cancelar.ThrowIfCancellationRequested();
            var a = candidatos[i];
            var hash = calcularHash(a.Ruta);
            progreso?.Report((i + 1, candidatos.Count));
            if (hash is null) continue;

            hashes[a.Ruta] = hash;
            if (!grupos.TryGetValue((a.Tamano, hash), out var grupo)) grupos[(a.Tamano, hash)] = grupo = [];
            grupo.Add(a);
        }

        var duplicados = new Dictionary<string, Duplicado>(StringComparer.OrdinalIgnoreCase);
        foreach (var grupo in grupos.Values.Where(g => g.Count > 1))
        {
            var nuevos = grupo.Where(a => !a.EsReferencia).ToList();
            var referencia = grupo.Where(a => a.EsReferencia)
                                  .OrderBy(a => EstaEnRevision(a.RutaRelativa) ? 1 : 0)
                                  .ThenBy(a => a.Ruta, StringComparer.InvariantCultureIgnoreCase)
                                  .FirstOrDefault();
            if (referencia is not null)
            {
                foreach (var a in nuevos) duplicados[a.Ruta] = new(MotivoDuplicado.YaExisteEn, referencia.Ruta);
                continue;
            }

            var orden = nuevos.OrderBy(a => Clasificador.OriginalDeVistaPrevia(a.RutaRelativa) is null ? 0 : 1)
                              .ThenBy(a => Clasificador.EstaEnMensajes(a.RutaRelativa) ? 1 : 0)
                              .ThenBy(a => Path.GetFileName(a.RutaRelativa).Length)
                              .ThenBy(a => a.RutaRelativa.Length)
                              // Orden alfabetico como el Explorador y el script ('_' antes que los digitos)
                              .ThenBy(a => a.RutaRelativa, StringComparer.InvariantCultureIgnoreCase)
                              .ToList();
            foreach (var a in orden.Skip(1)) duplicados[a.Ruta] = new(MotivoDuplicado.IgualA, orden[0].RutaRelativa);
        }

        return new(duplicados, hashes);
    }

    /// <summary>MD5 del archivo en hexadecimal, o null si no se puede leer.</summary>
    public static string? Md5(string ruta)
    {
        try
        {
            using var archivo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            return Convert.ToHexString(MD5.HashData(archivo));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Copias organizadas en carpetas de revision (_duplicados*, _jpg_reemplazados, _revision*):
    // solo se usan como referencia si no hay otra
    static bool EstaEnRevision(string rutaRelativa) =>
        rutaRelativa.Split('\\', '/').Any(c => c.StartsWith("_duplicados", StringComparison.OrdinalIgnoreCase)
                                               || c.StartsWith("_revision", StringComparison.OrdinalIgnoreCase)
                                               || c.Equals("_jpg_reemplazados", StringComparison.OrdinalIgnoreCase));
}
