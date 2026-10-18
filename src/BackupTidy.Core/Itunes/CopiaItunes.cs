using BackupTidy.Core.Clasificacion;
using Microsoft.Data.Sqlite;

namespace BackupTidy.Core.Itunes;

/// <summary>Que se extrae de una copia de iTunes.</summary>
[Flags]
public enum ContenidoItunes
{
    Ninguno = 0,
    /// <summary>Originales del carrete (<c>DCIM</c>), con las Live Photos.</summary>
    Carrete = 1,
    /// <summary>Lo editado en el iPhone (<c>FullSizeRender</c>), al lado del original como <c>IMG_1234_editada</c>.</summary>
    Editadas = 2,
    /// <summary>Fotos y videos de los chats (<c>Message/Media</c>), sin stickers ni notas de voz.</summary>
    WhatsApp = 4,
    Todo = Carrete | Editadas | WhatsApp,
}

/// <summary>Un archivo a extraer de la copia.</summary>
/// <param name="Origen">Ruta completa dentro de la copia: el nombre es un hash, no el original.</param>
/// <param name="Destino">Ruta relativa con el nombre real, dentro de la carpeta donde se extrae.</param>
/// <param name="Modificado">Fecha de modificacion en el iPhone (UTC), segun el indice de la copia.</param>
/// <param name="Creado">Fecha de creacion en el iPhone (UTC), segun el indice de la copia.</param>
public sealed record ArchivoItunes(ContenidoItunes Contenido, string Origen, string Destino, long Bytes,
                                   DateTime? Modificado, DateTime? Creado);

/// <summary>
/// Copia de seguridad de un iPhone hecha con iTunes o con la app Dispositivos de Apple: una carpeta por
/// dispositivo, con los archivos renombrados por su hash y un indice SQLite (<c>Manifest.db</c>) que dice
/// que es cada uno. Solo se lee: la copia no se modifica.
/// </summary>
public sealed class CopiaItunes
{
    static readonly Dictionary<string, string> CarpetasWhatsApp = new()
    {
        ["AppDomainGroup-group.net.whatsapp.WhatsApp.shared"] = "WhatsApp",
        ["AppDomainGroup-group.net.whatsapp.WhatsAppSMB.shared"] = "WhatsApp Business",
    };

    CopiaItunes(string carpeta, string dispositivo, string? versionIos, DateTime? fecha, bool encriptada)
    {
        Carpeta = carpeta;
        Dispositivo = dispositivo;
        VersionIos = versionIos;
        Fecha = fecha;
        Encriptada = encriptada;
    }

    public string Carpeta { get; }

    /// <summary>Nombre del iPhone ("iPhone de Pao").</summary>
    public string Dispositivo { get; }

    public string? VersionIos { get; }

    /// <summary>Cuando se hizo la copia (UTC).</summary>
    public DateTime? Fecha { get; }

    /// <summary>Copia con contrasena: el indice y los archivos estan cifrados y todavia no se pueden leer.</summary>
    public bool Encriptada { get; }

    /// <summary>Nombre para la carpeta de extraccion: "iPhone de Pao 2026-10-12".</summary>
    public string NombreSugerido
    {
        get
        {
            var nombre = Fecha is DateTime f ? $"{Dispositivo} {f.ToLocalTime():yyyy-MM-dd}" : Dispositivo;
            return string.Concat(nombre.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        }
    }

    /// <summary>Donde guardan las copias iTunes (instalador de Apple) y la version de Microsoft Store.</summary>
    public static IReadOnlyList<string> UbicacionesHabituales() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Apple Computer", "MobileSync", "Backup"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apple", "MobileSync", "Backup"),
    ];

    /// <summary>Las copias que hay en esas carpetas, la mas reciente primero. Las que no se pueden leer se omiten.</summary>
    public static IReadOnlyList<CopiaItunes> Buscar(IEnumerable<string> ubicaciones)
    {
        var copias = new List<CopiaItunes>();
        foreach (var ubicacion in ubicaciones.Where(Directory.Exists))
        {
            string[] carpetas;
            try
            {
                carpetas = Directory.GetDirectories(ubicacion);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Sin permiso para ver la carpeta: se sigue con las otras ubicaciones
                continue;
            }
            foreach (var carpeta in carpetas)
            {
                try
                {
                    copias.Add(Abrir(carpeta));
                }
                catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    // Copia a medio hacer, de iOS muy viejo o una carpeta que no es copia
                }
            }
        }
        return copias.OrderByDescending(c => c.Fecha).ToList();
    }

    /// <exception cref="ArgumentException">La carpeta no es una copia de iTunes, o es de un formato que no se lee.</exception>
    /// <exception cref="IOException">Manifest.plist esta danado.</exception>
    public static CopiaItunes Abrir(string carpeta)
    {
        var plist = Path.Combine(carpeta, "Manifest.plist");
        if (!File.Exists(plist)) throw new ArgumentException("No es una copia de seguridad de iTunes: falta Manifest.plist.");
        if (!File.Exists(Path.Combine(carpeta, "Manifest.db")))
            throw new ArgumentException(File.Exists(Path.Combine(carpeta, "Manifest.mbdb"))
                ? "Es una copia de iOS 9 o anterior: ese formato todavía no se puede leer."
                : "No es una copia de seguridad de iTunes: falta Manifest.db.");

        Dictionary<string, object?> manifiesto;
        try
        {
            manifiesto = ListaPropiedades.Leer(File.ReadAllBytes(plist)) as Dictionary<string, object?>
                         ?? throw new FormatException("Manifest.plist no es un diccionario");
        }
        catch (FormatException e)
        {
            throw new IOException("No se pudo leer Manifest.plist: " + e.Message, e);
        }
        var equipo = manifiesto.GetValueOrDefault("Lockdown") as Dictionary<string, object?>;
        return new CopiaItunes(
            carpeta,
            equipo?.GetValueOrDefault("DeviceName") as string ?? Path.GetFileName(carpeta),
            equipo?.GetValueOrDefault("ProductVersion") as string,
            manifiesto.GetValueOrDefault("Date") as DateTime?,
            manifiesto.GetValueOrDefault("IsEncrypted") as bool? ?? false);
    }

    /// <summary>
    /// Si iTunes termino la copia. Mientras la hace (o si se corto a la mitad) <c>Status.plist</c> no dice
    /// <c>SnapshotState = finished</c>, y el indice puede estar a medio escribir. Se lee cada vez: puede cambiar.
    /// </summary>
    /// <remarks>Sin <c>Status.plist</c> legible no se puede saber: se toma como terminada.</remarks>
    public bool EstaTerminada()
    {
        try
        {
            var estado = ListaPropiedades.Leer(File.ReadAllBytes(Path.Combine(Carpeta, "Status.plist"))) as Dictionary<string, object?>;
            return estado?.GetValueOrDefault("SnapshotState") is not string s || s == "finished";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return true;
        }
    }

    /// <summary>Lo que se puede extraer, con su nombre real. Solo lee el indice, no copia nada.</summary>
    /// <remarks>
    /// Dos archivos con el mismo nombre de salida quedan como <c>nombre_2.ext</c>: nunca se pisan. Las filas del
    /// indice que no tienen la forma esperada se omiten.
    /// </remarks>
    /// <exception cref="ArgumentException">La copia esta encriptada.</exception>
    /// <exception cref="IOException">La copia no esta terminada, o Manifest.db esta danado.</exception>
    public IReadOnlyList<ArchivoItunes> Listar(ContenidoItunes contenido, CancellationToken cancelar = default)
    {
        if (Encriptada) throw new ArgumentException("La copia está encriptada (tiene contraseña): todavía no se puede leer.");
        if (!EstaTerminada()) throw new IOException(SinTerminar);

        var candidatos = new List<(ContenidoItunes Que, string Ruta, FileInfo Info, byte[]? Metadatos)>();
        var fotosDelCarrete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            // immutable: no toca la copia (ni bloqueos ni archivos -wal / -shm al lado)
            DataSource = new Uri(Path.GetFullPath(Path.Combine(Carpeta, "Manifest.db"))).AbsoluteUri + "?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        try
        {
            conexion.Open();
            using var consulta = conexion.CreateCommand();
            // flags = 1: archivos (2 son carpetas, 4 enlaces)
            consulta.CommandText = """
                SELECT fileID, domain, relativePath, file FROM Files
                WHERE flags = 1 AND (domain = 'CameraRollDomain' OR domain LIKE '%whatsapp%')
                ORDER BY domain, relativePath
                """;
            using var filas = consulta.ExecuteReader();
            while (filas.Read())
            {
                cancelar.ThrowIfCancellationRequested();
                // SQLite no obliga a respetar los tipos de las columnas: lo que no es texto (o blob) se omite
                if (filas.GetValue(0) is not string id || !EsHash(id)
                    || filas.GetValue(1) is not string dominio || filas.GetValue(2) is not string rutaRelativa
                    || RutaDeSalida(dominio, rutaRelativa) is not (var que, var ruta))
                    continue;
                if (que == ContenidoItunes.Carrete && Clasificador.TipoDe(ruta) == TipoArchivo.Foto) fotosDelCarrete.Add(SinExtension(ruta));
                if ((contenido & que) == 0) continue;

                // Los archivos estan en <copia>\<2 primeros caracteres del hash>\<hash>
                var info = new FileInfo(Path.Combine(Carpeta, id[..2], id));
                if (info.Exists) candidatos.Add((que, ruta, info, filas.GetValue(3) as byte[]));
            }
        }
        catch (SqliteException e)
        {
            throw new IOException("No se pudo leer Manifest.db: " + e.Message, e);
        }

        var archivos = new List<ArchivoItunes>(candidatos.Count);
        var usadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (que, ruta, info, metadatos) in candidatos)
        {
            // Al editar un video, Fotos guarda el video editado y un JPG con un cuadro: ese JPG no es una foto,
            // y al lado del video lo haria pasar por Live Photo. La imagen editada solo vale si el original es foto
            if (que == ContenidoItunes.Editadas && Clasificador.TipoDe(ruta) == TipoArchivo.Foto
                && !fotosDelCarrete.Contains(SinExtension(ruta)[..^EditadaSufijo.Length]))
                continue;
            var (modificado, creado) = Fechas(metadatos);
            archivos.Add(new ArchivoItunes(que, info.FullName, NombreLibre(ruta, usadas), info.Length, modificado, creado));
        }
        return archivos;
    }

    const string EditadaSufijo = "_editada";

    const string SinTerminar = "La copia no está terminada: iTunes la está haciendo ahora o se cortó a la mitad. "
                               + "Espera a que termine (o vuelve a hacerla) y vuelve a intentarlo.";

    // SHA1 en hexadecimal: 40 caracteres
    static bool EsHash(string id) => id.Length == 40 && id.All(char.IsAsciiHexDigit);

    static string SinExtension(string ruta) => Path.Combine(Path.GetDirectoryName(ruta)!, Path.GetFileNameWithoutExtension(ruta));

    /// <summary>
    /// Ruta de salida (relativa) de un archivo de la copia y de que contenido es; null si no es foto ni video
    /// de lo que se extrae (miniaturas de Fotos, ediciones .AAE, stickers, notas de voz, otras apps).
    /// </summary>
    public static (ContenidoItunes Contenido, string Ruta)? RutaDeSalida(string dominio, string rutaRelativa)
    {
        if (Clasificador.TipoDe(rutaRelativa) is not (TipoArchivo.Foto or TipoArchivo.Video)) return null;
        var partes = rutaRelativa.Split('/');

        if (dominio == "CameraRollDomain")
        {
            // Media/DCIM/106APPLE/IMG_6671.HEIC (y su .MOV si es Live Photo, con el mismo nombre)
            if (partes is ["Media", "DCIM", var carpeta, var nombre])
                return Salida(ContenidoItunes.Carrete, "DCIM", carpeta, nombre);
            // Media/PhotoData/Mutations/DCIM/106APPLE/IMG_6352/Adjustments/FullSizeRender.heic: la version editada.
            // Una Live Photo editada tiene FullSizeRender.heic y .mov: quedan emparejadas como IMG_6352_editada
            if (partes is ["Media", "PhotoData", "Mutations", "DCIM", var c, var foto, "Adjustments", var render]
                && render.StartsWith("FullSizeRender.", StringComparison.OrdinalIgnoreCase))
                return Salida(ContenidoItunes.Editadas, "DCIM", c, foto + EditadaSufijo + Path.GetExtension(render));
            return null;
        }

        // Message/Media/<chat>/0/a/<uuid>.jpg: el nombre ya es unico. Los .webp de los chats son stickers
        if (CarpetasWhatsApp.TryGetValue(dominio, out var carpetaWhatsApp) && partes is ["Message", "Media", .., var archivo]
            && !archivo.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            return Salida(ContenidoItunes.WhatsApp, carpetaWhatsApp, archivo);
        return null;
    }

    static readonly char[] NoValidos = Path.GetInvalidFileNameChars();

    // Un nombre que en Windows no es valido (con \ o :, o "..") se omite: nada sale de la carpeta de extraccion
    static (ContenidoItunes, string)? Salida(ContenidoItunes que, params string[] nombres) =>
        nombres.All(n => n.Length > 0 && n is not ("." or "..") && n.IndexOfAny(NoValidos) < 0) ? (que, Path.Combine(nombres)) : null;

    static string NombreLibre(string ruta, HashSet<string> usadas)
    {
        var libre = ruta;
        for (var n = 2; !usadas.Add(libre); n++)
            libre = Path.Combine(Path.GetDirectoryName(ruta)!, $"{Path.GetFileNameWithoutExtension(ruta)}_{n}{Path.GetExtension(ruta)}");
        return libre;
    }

    // Metadatos de cada archivo: un NSKeyedArchiver (MBFile) con LastModified y Birth en segundos Unix
    static (DateTime? Modificado, DateTime? Creado) Fechas(byte[]? metadatos)
    {
        if (metadatos is null) return (null, null);
        try
        {
            var raiz = ListaPropiedades.RaizArchivada(ListaPropiedades.Leer(metadatos));
            return (Unix(raiz?.GetValueOrDefault("LastModified")), Unix(raiz?.GetValueOrDefault("Birth")));
        }
        catch (FormatException)
        {
            return (null, null);
        }
    }

    // Una fecha fuera de rango (metadatos danados) se pierde sola: no hace fallar toda la copia
    static DateTime? Unix(object? segundos) =>
        segundos is long s && s > 0 && s <= UnixMaximo ? DateTime.UnixEpoch.AddSeconds(s) : null;

    static readonly long UnixMaximo = (long)(DateTime.MaxValue - DateTime.UnixEpoch).TotalSeconds;
}
