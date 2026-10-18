using System.Globalization;
using System.Text;
using BackupTidy.Core.Ejecucion;
using BackupTidy.Core.Itunes;
using BackupTidy.Core.Plan;

// Herramienta de linea de comandos para correr el motor sobre carpetas reales (pruebas y validacion).
// La interfaz grafica es BackupTidy.App.

Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 0 || args[0] is "-h" or "--ayuda") return Ayuda();

var opciones = LeerOpciones(args[1..]);
string Requerida(string nombre) =>
    opciones.TryGetValue(nombre, out var v) && v.Count > 0 ? v[0] : throw new ArgumentException($"Falta --{nombre}");

try
{
    switch (args[0])
    {
        case "analizar":
        {
            var plan = Requerida("plan");
            var r = Analizador.Analizar(
                new OpcionesAnalisis(Requerida("origen"), Requerida("destino"), opciones.GetValueOrDefault("comparar-con")),
                new Progress<ProgresoAnalisis>(p => Progreso(p.Etapa.ToString(), p.Hechos, p.Total)));
            Console.WriteLine();
            foreach (var aviso in r.Avisos) Console.WriteLine("AVISO: " + aviso);
            PlanCsv.Escribir(r.Filas, plan);
            Console.WriteLine($"{"Categoria",-22}{"Archivos",10}{"GB",10}");
            foreach (var (categoria, archivos, bytes) in Analizador.Resumen(r.Filas))
                Console.WriteLine($"{categoria,-22}{archivos,10}{bytes / 1073741824.0,10:N2}");
            Console.WriteLine("Origen de la fecha:");
            foreach (var g in r.Filas.Where(f => f.FuenteFecha is not null).GroupBy(f => f.FuenteFecha).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-15}{g.Count(),8}");
            Console.WriteLine($"Plan guardado en: {Path.GetFullPath(plan)}  (no se movio ningun archivo)");
            return 0;
        }
        case "comparar":
            return Comparar(Requerida("a"), Requerida("b"), opciones.GetValueOrDefault("salida")?[0]);

        case "ejecutar":
        {
            if (!opciones.ContainsKey("si"))
            {
                Console.WriteLine("Mueve archivos de verdad. Revisa el plan y repite con --si para confirmar.");
                return 1;
            }
            IPapelera? papelera = opciones.ContainsKey("papelera-descartes") && OperatingSystem.IsWindows() ? new PapeleraWindows() : null;
            var r = Ejecutor.Ejecutar(PlanCsv.Leer(Requerida("plan")), Requerida("registro"), Requerida("origen"), papelera,
                                      new Progress<(int Hechos, int Total)>(p => Progreso("Moviendo", p.Hechos, p.Total)));
            Console.WriteLine();
            foreach (var aviso in r.Avisos) Console.WriteLine("AVISO: " + aviso);
            Console.WriteLine($"Movidos: {r.Movidos}  Omitidos: {r.Omitidos}  Errores: {r.Errores}  A la Papelera: {r.EnviadosAPapelera}");
            Console.WriteLine($"Registro para deshacer: {Path.GetFullPath(Requerida("registro"))}");
            return r.Errores > 0 ? 2 : 0;
        }
        case "deshacer":
        {
            var r = Ejecutor.Deshacer(Requerida("registro"));
            Console.WriteLine($"Restaurados: {r.Restaurados}  Omitidos: {r.Omitidos}  En la Papelera (restaurar desde ahi): {r.EnPapelera}");
            return 0;
        }
        case "copias-itunes":
        {
            var ubicaciones = opciones.GetValueOrDefault("en") is { Count: > 0 } en ? en : CopiaItunes.UbicacionesHabituales();
            var copias = CopiaItunes.Buscar(ubicaciones);
            if (copias.Count == 0) Console.WriteLine("No hay copias de iTunes en: " + string.Join(", ", ubicaciones));
            foreach (var copia in copias)
            {
                Console.WriteLine($"{copia.Dispositivo}  iOS {copia.VersionIos}  {copia.Fecha?.ToLocalTime():yyyy-MM-dd HH:mm}{(copia.Encriptada ? "  (encriptada)" : "")}");
                Console.WriteLine($"  {copia.Carpeta}");
                if (copia.Encriptada) continue;
                foreach (var g in copia.Listar(ContenidoItunes.Todo).GroupBy(a => a.Contenido))
                    Console.WriteLine($"  {g.Key,-10}{g.Count(),8} archivos{g.Sum(a => a.Bytes) / 1073741824.0,10:N2} GB");
            }
            return 0;
        }
        case "extraer-itunes":
        {
            var copia = CopiaItunes.Abrir(Requerida("copia"));
            var salida = Requerida("salida");
            var contenido = opciones.GetValueOrDefault("contenido") is { Count: > 0 } lista
                ? lista.SelectMany(v => v.Split(',')).Aggregate(ContenidoItunes.Ninguno, (c, v) => c | Enum.Parse<ContenidoItunes>(v, ignoreCase: true))
                : ContenidoItunes.Todo;
            var archivos = copia.Listar(contenido);
            // Para probar con una parte: los primeros N
            if (opciones.GetValueOrDefault("maximo") is [var maximo]) archivos = archivos.Take(int.Parse(maximo, CultureInfo.InvariantCulture)).ToList();

            Directory.CreateDirectory(salida);
            var pendientes = ExtractorItunes.BytesPendientes(archivos, salida);
            var libres = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(salida))!).AvailableFreeSpace;
            Console.WriteLine($"{copia.Dispositivo}: {archivos.Count} archivos ({contenido}), faltan copiar {pendientes / 1073741824.0:N2} GB; libres {libres / 1073741824.0:N2} GB");
            if (pendientes > libres)
            {
                Console.Error.WriteLine("ERROR: no hay espacio suficiente en el disco de la salida.");
                return 1;
            }
            var r = ExtractorItunes.Extraer(archivos, salida, new Progress<(int Hechos, int Total)>(p => Progreso("Copiando", p.Hechos, p.Total)));
            Console.WriteLine();
            foreach (var error in r.Errores) Console.WriteLine("ERROR: " + error);
            Console.WriteLine($"Copiados: {r.Copiados} ({r.BytesCopiados / 1073741824.0:N2} GB)  Ya estaban: {r.YaEstaban}  Errores: {r.Errores.Count}");
            Console.WriteLine($"Listo para analizar: {Path.GetFullPath(salida)}");
            return r.Errores.Count > 0 ? 2 : 0;
        }
        default:
            return Ayuda();
    }
}
catch (Exception e) when (e is ArgumentException or IOException or DirectoryNotFoundException)
{
    Console.Error.WriteLine("ERROR: " + e.Message);
    return 1;
}

static int Ayuda()
{
    Console.WriteLine("""
        BackupTidy.Cli <comando> [opciones]

          analizar  --origen <carpeta> --destino <carpeta> --plan <plan.csv> [--comparar-con <carpeta>]...
                    Genera el plan. No mueve ni modifica ningun archivo.
          comparar  --a <plan.csv> --b <plan.csv> [--salida <diferencias.csv>]
                    Compara dos planes del mismo origen (por ejemplo el del script y el del motor).
          ejecutar  --plan <plan.csv> --registro <movimientos.csv> --origen <carpeta> --si [--papelera-descartes]
                    Mueve segun el plan y registra cada movimiento.
          deshacer  --registro <movimientos.csv>
                    Devuelve cada archivo a su lugar.
          copias-itunes [--en <carpeta>]...
                    Lista las copias de seguridad de iTunes y cuanto hay para extraer de cada una.
          extraer-itunes --copia <carpeta> --salida <carpeta> [--contenido carrete,editadas,whatsapp] [--maximo N]
                    Copia fotos y videos con su nombre y fecha reales, para analizar la salida como un backup.
                    La copia de iTunes no se modifica. Si se corta, repetir el comando sigue donde quedo.
        """);
    return 1;
}

static Dictionary<string, List<string>> LeerOpciones(string[] args)
{
    var opciones = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) throw new ArgumentException($"Opcion inesperada: {args[i]}");
        var nombre = args[i][2..];
        if (!opciones.TryGetValue(nombre, out var valores)) opciones[nombre] = valores = [];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) valores.Add(args[++i]);
    }
    return opciones;
}

static void Progreso(string etapa, int hechos, int total)
{
    // Una linea por etapa, que se va actualizando
    if (hechos <= 1) Console.WriteLine();
    if (total == 0 || hechos == total || hechos % 100 == 0)
        Console.Write($"\r{etapa,-20} {hechos,8} / {total,-8}");
}

// Empareja las filas por archivo de origen y resume en que difieren: categoria, carpeta de destino y mes
static int Comparar(string rutaA, string rutaB, string? salida)
{
    var a = PlanCsv.Leer(rutaA).ToDictionary(f => f.Origen, StringComparer.OrdinalIgnoreCase);
    var b = PlanCsv.Leer(rutaB).ToDictionary(f => f.Origen, StringComparer.OrdinalIgnoreCase);
    var soloA = a.Keys.Except(b.Keys, StringComparer.OrdinalIgnoreCase).ToList();
    var soloB = b.Keys.Except(a.Keys, StringComparer.OrdinalIgnoreCase).ToList();

    static string? Carpeta(FilaPlan f) => f.Destino is null ? null : Path.GetDirectoryName(f.Destino);
    static string Mes(FilaPlan f) => f.Fecha?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "";

    var distintas = a.Keys.Intersect(b.Keys, StringComparer.OrdinalIgnoreCase)
        .Select(k => (A: a[k], B: b[k]))
        .Where(p => p.A.Categoria != p.B.Categoria || !string.Equals(Carpeta(p.A), Carpeta(p.B), StringComparison.OrdinalIgnoreCase))
        .ToList();

    Console.WriteLine($"Plan A: {a.Count} filas   Plan B: {b.Count} filas");
    Console.WriteLine($"Iguales: {a.Count - soloA.Count - distintas.Count}   Distintas: {distintas.Count}   Solo en A: {soloA.Count}   Solo en B: {soloB.Count}");
    if (distintas.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"{"Categoria A -> B",-40}{"Archivos",10}{"Mismo mes",11}");
        foreach (var g in distintas.GroupBy(p => $"{p.A.Categoria} -> {p.B.Categoria}").OrderByDescending(g => g.Count()))
            Console.WriteLine($"{g.Key,-40}{g.Count(),10}{g.Count(p => Mes(p.A) == Mes(p.B)),11}");
        Console.WriteLine();
        Console.WriteLine($"{"Fuente de fecha A -> B (con distinto destino)",-50}{"Archivos",10}");
        foreach (var g in distintas.GroupBy(p => $"{p.A.FuenteFecha} -> {p.B.FuenteFecha}").OrderByDescending(g => g.Count()))
            Console.WriteLine($"{g.Key,-50}{g.Count(),10}");
    }

    if (salida is not null)
    {
        using var csv = new StreamWriter(salida, false, new UTF8Encoding(true));
        static string Campo(string? s) => '"' + (s ?? "").Replace("\"", "\"\"") + '"';
        csv.WriteLine(string.Join(';', new[] { "Diferencia", "Origen", "CategoriaA", "CategoriaB", "DestinoA", "DestinoB",
                                               "FechaA", "FechaB", "FuenteA", "FuenteB", "NotaA", "NotaB" }.Select(Campo)));
        void Fila(string tipo, FilaPlan? x, FilaPlan? y) => csv.WriteLine(string.Join(';', new[]
        {
            tipo, (x ?? y)!.Origen, x?.Categoria.ToString(), y?.Categoria.ToString(), x?.Destino, y?.Destino,
            x?.Fecha?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), y?.Fecha?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            x?.FuenteFecha?.ToString(), y?.FuenteFecha?.ToString(), x?.Nota, y?.Nota,
        }.Select(Campo)));
        foreach (var (x, y) in distintas) Fila("distinta", x, y);
        foreach (var k in soloA) Fila("solo A", a[k], null);
        foreach (var k in soloB) Fila("solo B", null, b[k]);
        Console.WriteLine($"Detalle en: {Path.GetFullPath(salida)}");
    }
    return 0;
}
