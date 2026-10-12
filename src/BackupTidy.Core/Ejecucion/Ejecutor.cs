using BackupTidy.Core.Plan;

namespace BackupTidy.Core.Ejecucion;

/// <param name="Omitidos">No se movieron porque algo cambio desde el analisis (ver <see cref="Ejecutor.Ejecutar"/>).</param>
/// <param name="Cancelado">Se detuvo a pedido del usuario: lo movido hasta ahi queda en el registro y se puede deshacer.</param>
public sealed record ResultadoEjecucion(int Movidos, int Omitidos, int Errores, int EnviadosAPapelera, bool Cancelado,
                                        IReadOnlyList<string> Avisos);

/// <param name="EnPapelera">Archivos enviados a la Papelera: se restauran desde la Papelera de Windows.</param>
public sealed record ResultadoDeshacer(int Restaurados, int Omitidos, int EnPapelera);

/// <summary>
/// Ejecuta un plan revisado y lo registra para poder deshacerlo. Nunca borra ni sobrescribe.
/// </summary>
public static class Ejecutor
{
    const string MarcaPapelera = "PAPELERA";

    /// <summary>
    /// Mueve cada archivo del plan. Se omite (sin error) si: esta en otro disco que el destino,
    /// el origen ya no existe, el destino ya existe o el tamano cambio desde el analisis.
    /// </summary>
    /// <param name="rutaRegistro">CSV de movimientos (Origen;Destino;Categoria) para <see cref="Deshacer"/>.</param>
    /// <param name="descartesAPapelera">
    /// Si no es null, los descartes que esten dentro de <paramref name="origen"/> van a esta Papelera.
    /// </param>
    public static ResultadoEjecucion Ejecutar(IReadOnlyList<FilaPlan> filas, string rutaRegistro, string origen,
                                              IPapelera? descartesAPapelera = null,
                                              IProgress<(int Hechos, int Total)>? progreso = null,
                                              CancellationToken cancelar = default)
    {
        var avisos = new List<string>();
        int movidos = 0, omitidos = 0, errores = 0, enviados = 0;
        var aMover = filas.Where(f => f.Categoria != CategoriaPlan.Descartar && f.Destino is not null).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(rutaRegistro))!);
        using var registro = Csv.AbrirEscritura(rutaRegistro);   // se vuelca linea por linea: sirve aunque se corte
        Csv.EscribirLinea(registro, "Origen", "Destino", "Categoria");

        for (var i = 0; i < aMover.Count; i++)
        {
            if (cancelar.IsCancellationRequested)
                return new(movidos, omitidos, errores, enviados, Cancelado: true, avisos);
            progreso?.Report((i + 1, aMover.Count));
            var f = aMover[i];

            if (!string.Equals(Path.GetPathRoot(f.Origen), Path.GetPathRoot(f.Destino), StringComparison.OrdinalIgnoreCase))
            {
                avisos.Add($"Distinto disco, se omite: {f.Origen}");
                omitidos++;
                continue;
            }
            var info = new FileInfo(f.Origen);
            if (!info.Exists || File.Exists(f.Destino) || info.Length != f.Bytes)
            {
                omitidos++;
                continue;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(f.Destino)!);
                File.Move(f.Origen, f.Destino!, overwrite: false);
                Csv.EscribirLinea(registro, f.Origen, f.Destino, f.Categoria.ToString());
                movidos++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                avisos.Add($"No se pudo mover {f.Origen}: {e.Message}");
                errores++;
            }
        }

        if (descartesAPapelera is not null)
            enviados = EnviarDescartes(filas.Where(f => f.Categoria == CategoriaPlan.Descartar).ToList(),
                                       Analizador.Normalizar(origen), descartesAPapelera, registro, avisos);

        return new(movidos, omitidos, errores, enviados, Cancelado: false, avisos);
    }

    /// <summary>Devuelve cada archivo a su lugar, en orden inverso, si sigue en el destino y el origen esta libre.</summary>
    public static ResultadoDeshacer Deshacer(string rutaRegistro)
    {
        int restaurados = 0, omitidos = 0, enPapelera = 0;
        var filas = Csv.Leer(rutaRegistro);
        filas.Reverse();
        foreach (var f in filas)
        {
            var (origen, destino) = (f["Origen"], f["Destino"]);
            if (destino == MarcaPapelera) { enPapelera++; continue; }
            if (File.Exists(destino) && !File.Exists(origen))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(origen)!);
                File.Move(destino, origen);
                restaurados++;
            }
            else omitidos++;
        }
        return new(restaurados, omitidos, enPapelera);
    }

    // Si una carpeta solo tiene descartes (o esta vacia) se envia entera: una operacion en vez de miles.
    // Nunca se envia la carpeta de origen ni archivos que no esten en el plan.
    static int EnviarDescartes(List<FilaPlan> descartes, string origen, IPapelera papelera, StreamWriter registro, List<string> avisos)
    {
        var existentes = descartes.Where(f => File.Exists(f.Origen)).ToList();
        if (existentes.Count == 0) return 0;
        if (existentes.Any(f => !Analizador.EstaDentro(f.Origen, origen)))
        {
            avisos.Add($"Hay descartes fuera de {origen}: no se envia ninguno a la Papelera.");
            return 0;
        }
        var bytes = existentes.Sum(f => f.Bytes);
        if (papelera.Capacidad(origen) is not long capacidad || bytes > capacidad)
        {
            avisos.Add($"No se envian descartes: la Papelera de {Path.GetPathRoot(origen)} no esta confirmada o no alcanza " +
                       $"({bytes / 1073741824.0:N1} GB). Quedan en su lugar.");
            return 0;
        }

        var enviar = existentes.Select(f => f.Origen).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enviados = 0;

        void Anotar(string archivo)
        {
            Csv.EscribirLinea(registro, archivo, MarcaPapelera, nameof(CategoriaPlan.Descartar));
            enviados++;
        }

        void EnviarArbol(string carpeta)
        {
            var archivos = Directory.GetFiles(carpeta, "*", SearchOption.AllDirectories);
            if (archivos.All(enviar.Contains))
            {
                papelera.EnviarCarpeta(carpeta);
                foreach (var a in archivos) Anotar(a);
                return;
            }
            foreach (var sub in Directory.GetDirectories(carpeta)) EnviarArbol(sub);
            foreach (var a in Directory.GetFiles(carpeta).Where(enviar.Contains))
            {
                papelera.EnviarArchivo(a);
                Anotar(a);
            }
        }

        // Desde las subcarpetas: la carpeta de origen en si nunca va a la Papelera
        foreach (var sub in Directory.GetDirectories(origen)) EnviarArbol(sub);
        foreach (var a in Directory.GetFiles(origen).Where(enviar.Contains))
        {
            papelera.EnviarArchivo(a);
            Anotar(a);
        }
        return enviados;
    }
}
