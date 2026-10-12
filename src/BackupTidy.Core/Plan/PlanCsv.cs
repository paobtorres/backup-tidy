using System.Globalization;
using BackupTidy.Core.Fechas;

namespace BackupTidy.Core.Plan;

/// <summary>
/// Guarda y lee el plan en el formato del script de referencia, para revisarlo en Excel y poder
/// comparar ambos: Categoria;Fecha;FechaFuente;TamanoMB;Bytes;Origen;Destino;Hash;Nota.
/// </summary>
public static class PlanCsv
{
    static readonly string[] Columnas = ["Categoria", "Fecha", "FechaFuente", "TamanoMB", "Bytes", "Origen", "Destino", "Hash", "Nota"];
    const string FormatoFecha = "yyyy-MM-dd HH:mm";

    static readonly Dictionary<FuenteFecha, string> TextoFuente = new()
    {
        [FuenteFecha.Exif] = "exif",
        [FuenteFecha.Metadatos] = "metadatos",
        [FuenteFecha.Nombre] = "nombre",
        [FuenteFecha.Archivo] = "archivo",
        [FuenteFecha.ArchivoDudoso] = "archivo_dudoso",
    };

    public static void Escribir(IEnumerable<FilaPlan> filas, string ruta)
    {
        using var salida = Csv.AbrirEscritura(ruta);
        Csv.EscribirLinea(salida, Columnas);
        foreach (var f in filas)
            Csv.EscribirLinea(salida,
                f.Categoria.ToString(),
                f.Fecha?.ToString(FormatoFecha, CultureInfo.InvariantCulture),
                f.FuenteFecha is FuenteFecha fuente ? TextoFuente[fuente] : null,
                // Solo informativo: con la cultura del usuario para que Excel lo tome como numero
                Math.Round(f.Bytes / 1048576.0, 2).ToString(CultureInfo.CurrentCulture),
                f.Bytes.ToString(CultureInfo.InvariantCulture),
                f.Origen, f.Destino, f.Hash, f.Nota);
    }

    public static List<FilaPlan> Leer(string ruta) => Csv.Leer(ruta).Select(r =>
    {
        var fecha = r["Fecha"];
        var fuente = r["FechaFuente"];
        return new FilaPlan(
            Enum.Parse<CategoriaPlan>(r["Categoria"], ignoreCase: true),
            r["Origen"],
            r["Destino"] is { Length: > 0 } destino ? destino : null,
            long.Parse(r["Bytes"], CultureInfo.InvariantCulture),
            fecha.Length > 0 ? DateTime.ParseExact(fecha, FormatoFecha, CultureInfo.InvariantCulture) : null,
            fuente.Length > 0 ? TextoFuente.Single(kv => kv.Value.Equals(fuente, StringComparison.OrdinalIgnoreCase)).Key : null,
            r["Hash"] is { Length: > 0 } hash ? hash : null,
            r["Nota"]);
    }).ToList();
}
