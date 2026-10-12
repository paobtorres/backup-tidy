using System.Globalization;
using BackupTidy.Core.Clasificacion;
using BackupTidy.Core.Duplicados;
using BackupTidy.Core.Fechas;

namespace BackupTidy.Core.Plan;

/// <param name="CompararCon">Carpetas ya organizadas: lo que ya exista ahi va a _duplicados.</param>
/// <param name="Ahora">Para validar fechas; por defecto, la hora actual.</param>
public sealed record OpcionesAnalisis(string Origen, string Destino, IReadOnlyList<string>? CompararCon = null, DateTime? Ahora = null);

public enum EtapaAnalisis { Listando, BuscandoDuplicados, LeyendoMetadatos, Clasificando }

public readonly record struct ProgresoAnalisis(EtapaAnalisis Etapa, int Hechos, int Total);

/// <param name="Avisos">Problemas que no impiden el analisis (por ejemplo una carpeta para comparar que no existe).</param>
public sealed record ResultadoAnalisis(IReadOnlyList<FilaPlan> Filas, IReadOnlyList<string> Avisos);

/// <summary>
/// Recorre el origen y arma el plan (origen -> destino, categoria, motivo). Solo lee: no mueve ni modifica nada.
/// </summary>
public static class Analizador
{
    const string NotaDescarte = "Va a la Papelera solo si se elige al ejecutar";
    const string NotaVistaPrevia = "Vista previa JPG de un original que existe al lado";

    public static ResultadoAnalisis Analizar(OpcionesAnalisis opciones, IProgress<ProgresoAnalisis>? progreso = null,
                                             CancellationToken cancelar = default)
    {
        var origen = Normalizar(opciones.Origen);
        var destino = Normalizar(opciones.Destino);
        if (!Directory.Exists(origen)) throw new DirectoryNotFoundException($"No existe la carpeta de origen: {origen}");
        if (EstaDentro(destino, origen) || destino.Equals(origen, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("El destino no puede estar dentro del origen.", nameof(opciones));
        var ahora = opciones.Ahora ?? DateTime.Now;
        var avisos = new List<string>();

        // 1. Listar
        progreso?.Report(new(EtapaAnalisis.Listando, 0, 0));
        var todos = new List<FileInfo>();          // fotos, videos y documentos, en orden de recorrido
        var esDocumento = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var descartes = new List<FileInfo>();
        foreach (var f in Listar(origen))
        {
            cancelar.ThrowIfCancellationRequested();
            switch (Clasificador.TipoDe(f.Name))
            {
                case TipoArchivo.Descarte: descartes.Add(f); break;
                case TipoArchivo.Documento: todos.Add(f); esDocumento.Add(f.FullName); break;
                default: todos.Add(f); break;
            }
        }
        var media = todos.Where(f => !esDocumento.Contains(f.FullName)).ToList();

        var referencias = new List<ArchivoComparable>();
        foreach (var carpeta in (opciones.CompararCon ?? []).Select(Normalizar))
        {
            if (!Directory.Exists(carpeta)) { avisos.Add($"No existe para comparar: {carpeta}"); continue; }
            // Si el origen esta dentro de lo organizado, sus archivos no son referencia de si mismos
            referencias.AddRange(Listar(carpeta)
                .Where(f => Clasificador.TipoDe(f.Name) != TipoArchivo.Descarte && !EstaDentro(f.FullName, origen))
                .Select(f => new ArchivoComparable(f.FullName, Path.GetRelativePath(carpeta, f.FullName), f.Length, EsReferencia: true)));
        }
        var relativaDeReferencia = referencias.ToDictionary(r => r.Ruta, r => r.RutaRelativa, StringComparer.OrdinalIgnoreCase);

        // 2. Duplicados exactos
        var nuevos = todos.Select(f => new ArchivoComparable(f.FullName, Path.GetRelativePath(origen, f.FullName), f.Length));
        var duplicados = BuscadorDuplicados.Buscar(nuevos.Concat(referencias), BuscadorDuplicados.Md5,
            Etapa(progreso, EtapaAnalisis.BuscandoDuplicados), cancelar);

        var rutasMedia = media.Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool EsVistaPrevia(FileInfo f) => Clasificador.OriginalDeVistaPrevia(f.FullName) is string o && rutasMedia.Contains(o);

        // 3. Metadatos de lo que se va a ordenar por fecha
        var aLeer = media.Where(f => !duplicados.Duplicados.ContainsKey(f.FullName) && !EsVistaPrevia(f)).ToList();
        var metadatos = new Dictionary<string, MetadatosMedia>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < aLeer.Count; i++)
        {
            cancelar.ThrowIfCancellationRequested();
            metadatos[aLeer[i].FullName] = LectorMetadatos.Leer(aLeer[i].FullName);
            progreso?.Report(new(EtapaAnalisis.LeyendoMetadatos, i + 1, aLeer.Count));
        }

        var fotosLive = media.Where(f => Clasificador.PuedeSerFotoLive(f.Name, metadatos.GetValueOrDefault(f.FullName)?.ExtensionContenido))
                             .Select(f => Clasificador.ClaveLive(Path.GetRelativePath(origen, f.FullName)))
                             .ToHashSet();
        var carpetasDudosas = ReglasFecha.CarpetasConFechaDudosa(media.Select(f => (f.DirectoryName!, f.LastWriteTime)));
        var indice = new IndiceOrganizado(referencias);

        // 4. Clasificar
        var plan = new List<FilaPlan>();
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reemplazados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < todos.Count; i++)
        {
            cancelar.ThrowIfCancellationRequested();
            progreso?.Report(new(EtapaAnalisis.Clasificando, i + 1, todos.Count));
            var f = todos[i];
            var rel = Path.GetRelativePath(origen, f.FullName);
            var hash = duplicados.Hashes.GetValueOrDefault(f.FullName);
            duplicados.Duplicados.TryGetValue(f.FullName, out var duplicado);

            if (esDocumento.Contains(f.FullName))
            {
                // Sin fecha: la de modificacion es la de la exportacion
                var (categoria, carpeta) = duplicado is null
                    ? (CategoriaPlan.Documentos, Path.Combine("Documentos", Clasificador.TipoDocumentoDe(f.Name).ToString()))
                    : (CategoriaPlan.DocumentosDuplicados, Path.Combine("Documentos", "_duplicados"));
                plan.Add(new(categoria, f.FullName, DestinoLibre(Path.Combine(destino, carpeta, f.Name), usados), f.Length,
                             Hash: hash, Nota: duplicado?.Nota ?? ""));
                continue;
            }
            if (duplicado is not null)
            {
                plan.Add(new(CategoriaPlan.Duplicados, f.FullName, DestinoLibre(Path.Combine(destino, "_duplicados", rel), usados),
                             f.Length, Hash: hash, Nota: duplicado.Nota));
                continue;
            }
            if (EsVistaPrevia(f))
            {
                plan.Add(new(CategoriaPlan.Duplicados, f.FullName, DestinoLibre(Path.Combine(destino, "_duplicados_conversiones", rel), usados),
                             f.Length, Hash: hash, Nota: NotaVistaPrevia));
                continue;
            }

            var meta = metadatos[f.FullName];
            var fecha = ReglasFecha.Resolver(meta, f.Name, f.LastWriteTime, carpetasDudosas.Contains(f.DirectoryName!), ahora);
            var categoriaMedia = Clasificador.CategoriaDe(new DatosMedia(rel, f.Length, meta.TieneMarcaCamara,
                                                                          fotosLive.Contains(Clasificador.ClaveLive(rel)), meta.ExtensionContenido));
            var cat = FilaPlan.CategoriaDeMedia(categoriaMedia);
            var dest = Path.Combine(destino, cat.ToString(), fecha.Fuente == FuenteFecha.ArchivoDudoso ? "_fecha_dudosa" : "",
                                    fecha.Fecha.ToString("yyyy", CultureInfo.InvariantCulture),
                                    fecha.Fecha.ToString("yyyy-MM", CultureInfo.InvariantCulture), f.Name);
            var notas = new List<string>();

            if (cat is (CategoriaPlan.Fotos or CategoriaPlan.Videos or CategoriaPlan.LivePhotos)
                && fecha.Fuente is (FuenteFecha.Exif or FuenteFecha.Metadatos))
            {
                var mismaFoto = indice.Evaluar(f.Name, Clasificador.ExtensionEfectiva(rel, meta.ExtensionContenido), fecha.Fecha);
                if (mismaFoto.Accion == AccionMismaFoto.ReemplazaJpg)
                {
                    // El HEIC original entra; el JPG convertido sale a revision
                    notas.Add("Reemplaza JPG convertido: " + string.Join(" ; ", mismaFoto.Organizados));
                    foreach (var jpg in mismaFoto.Organizados.Where(reemplazados.Add))
                        plan.Add(new(CategoriaPlan.JpgReemplazado, jpg,
                                     DestinoLibre(Path.Combine(destino, "_jpg_reemplazados", relativaDeReferencia[jpg]), usados),
                                     new FileInfo(jpg).Length, fecha.Fecha, Nota: "Reemplazado por: " + f.FullName));
                }
                else if (mismaFoto.Accion == AccionMismaFoto.Probable)
                {
                    notas.Add("Mismo nombre y mes que: " + mismaFoto.Organizados[0]);
                    cat = CategoriaPlan.Duplicados;
                    dest = Path.Combine(destino, "_duplicados_probables", rel);
                }
            }
            if (Clasificador.NotaContenido(rel, meta.ExtensionContenido) is string notaContenido) notas.Add(notaContenido);

            plan.Add(new(cat, f.FullName, DestinoLibre(dest, usados), f.Length, fecha.Fecha, fecha.Fuente, hash, string.Join(" | ", notas)));
        }

        plan.AddRange(descartes.Select(f => new FilaPlan(CategoriaPlan.Descartar, f.FullName, null, f.Length, Nota: NotaDescarte)));
        return new(plan, avisos);
    }

    /// <summary>Archivos, cantidad y bytes por categoria, de mayor a menor tamano.</summary>
    public static IReadOnlyList<(CategoriaPlan Categoria, int Archivos, long Bytes)> Resumen(IEnumerable<FilaPlan> filas) =>
        filas.GroupBy(f => f.Categoria)
             .Select(g => (g.Key, g.Count(), g.Sum(f => f.Bytes)))
             .OrderByDescending(r => r.Item3)
             .ToList();

    // Nunca sobrescribir: nombre_2.ext, nombre_3.ext...
    static string DestinoLibre(string ruta, HashSet<string> usados)
    {
        var carpeta = Path.GetDirectoryName(ruta)!;
        var nombre = Path.GetFileNameWithoutExtension(ruta);
        var ext = Path.GetExtension(ruta);
        var candidato = ruta;
        for (var k = 2; usados.Contains(candidato) || File.Exists(candidato); k++)
            candidato = Path.Combine(carpeta, $"{nombre}_{k}{ext}");
        usados.Add(candidato);
        return candidato;
    }

    // Incluye ocultos y de sistema (Thumbs.db, desktop.ini son descartes); no sigue enlaces
    static IEnumerable<FileInfo> Listar(string carpeta) =>
        new DirectoryInfo(carpeta).EnumerateFiles("*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        });

    static IProgress<(int Hechos, int Total)>? Etapa(IProgress<ProgresoAnalisis>? progreso, EtapaAnalisis etapa) =>
        progreso is null ? null : new ProgresoEtapa(progreso, etapa);

    sealed class ProgresoEtapa(IProgress<ProgresoAnalisis> destino, EtapaAnalisis etapa) : IProgress<(int Hechos, int Total)>
    {
        public void Report((int Hechos, int Total) valor) => destino.Report(new(etapa, valor.Hechos, valor.Total));
    }

    internal static string Normalizar(string ruta) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(ruta));

    internal static bool EstaDentro(string ruta, string carpeta) =>
        ruta.StartsWith(carpeta + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
