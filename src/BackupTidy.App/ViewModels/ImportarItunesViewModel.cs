using System.Globalization;
using BackupTidy.App.Servicios;
using BackupTidy.Core.Clasificacion;
using BackupTidy.Core.Itunes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupTidy.App.ViewModels;

/// <summary>
/// Paso 1, otra entrada: copiar las fotos de una copia de iTunes a una carpeta comun, que despues se analiza
/// como cualquier backup. La copia de iTunes no se modifica.
/// </summary>
public partial class ImportarItunesViewModel : ViewModelBase
{
    readonly MainViewModel principal;
    IReadOnlyList<ArchivoItunes> archivos = [];
    CancellationTokenSource? cancelar;
    int lectura;

    // Lo elegido y su total: se recalculan al cambiar la copia o los contenidos, no con cada tecla en la carpeta
    IReadOnlyList<ArchivoItunes> elegidos = [];
    long bytesElegidos;

    // Lo que falta copiar a la carpeta de salida y lo libre en su disco; null mientras se calcula
    (long Pendientes, long Libres)? espacio;
    int calculo;

    // La carpeta que se sugirio para la copia elegida: si no se cambio, al elegir otra copia se sugiere la suya
    string? sugerida;

    public ImportarItunesViewModel(MainViewModel principal)
    {
        this.principal = principal;
        Contenidos =
        [
            new(this, ContenidoItunes.Carrete, "Carrete", "Fotos y videos originales, con las Live Photos"),
            new(this, ContenidoItunes.Editadas, "Versiones editadas", "Lo editado en el iPhone, al lado del original como IMG_1234_editada"),
            new(this, ContenidoItunes.WhatsApp, "WhatsApp", "Fotos y videos de los chats, sin stickers ni notas de voz"),
        ];
        _ = Buscar();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayCopias), nameof(SinCopias))]
    public partial IReadOnlyList<CopiaVista> Copias { get; set; } = [];

    public bool HayCopias => Copias.Count > 0;

    public bool SinCopias => !Buscando && Copias.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCopias))]
    public partial bool Buscando { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayCopia))]
    public partial CopiaVista? Seleccionada { get; set; }

    public bool HayCopia => Seleccionada is not null;

    /// <summary>Leyendo el indice de la copia elegida.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtraerCommand))]
    public partial bool Leyendo { get; set; }

    public IReadOnlyList<ContenidoVista> Contenidos { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorSalida), nameof(TextoEspacio))]
    [NotifyCanExecuteChangedFor(nameof(ExtraerCommand))]
    public partial string Salida { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtraerCommand), nameof(CancelarCommand))]
    public partial bool Extrayendo { get; set; }

    [ObservableProperty]
    public partial double Porcentaje { get; set; }

    [ObservableProperty]
    public partial string? Detalle { get; set; }

    /// <summary>No se pudo leer la copia o se corto la extraccion.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    ContenidoItunes Elegido => Contenidos.Where(c => c.Incluir).Aggregate(ContenidoItunes.Ninguno, (t, c) => t | c.Contenido);

    string? CarpetaSalida
    {
        get
        {
            try
            {
                return string.IsNullOrWhiteSpace(Salida) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(Salida));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }
    }

    /// <summary>Problema con la carpeta donde se extrae, o null si se puede (o falta elegirla).</summary>
    public string? ErrorSalida
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Salida)) return null;
            if (CarpetaSalida is not string salida) return "La ruta no es válida.";
            if (Seleccionada is { } copia && Dentro(salida, copia.Copia.Carpeta)) return "No puede estar dentro de la copia de iTunes.";

            var organizada = principal.Carpetas.Destino;
            if (!string.IsNullOrWhiteSpace(organizada))
            {
                if (Dentro(salida, organizada)) return "No puede estar dentro de la carpeta organizada.";
                // Despues se organiza moviendo, y eso solo se hace dentro de un mismo disco
                if (!string.Equals(Path.GetPathRoot(salida), Path.GetPathRoot(Path.GetFullPath(organizada)), StringComparison.OrdinalIgnoreCase))
                    return $"Tiene que estar en el mismo disco que la carpeta organizada ({Path.GetPathRoot(Path.GetFullPath(organizada))}).";
            }
            // Solo lo que falta: si una extraccion anterior se corto, lo ya copiado no vuelve a ocupar lugar
            if (espacio is { } e && e.Pendientes > e.Libres)
                return $"No hay espacio: faltan copiar {Formato.Tamano(e.Pendientes)} y en {Path.GetPathRoot(salida)} quedan {Formato.Tamano(e.Libres)}.";
            return null;
        }
    }

    public string? TextoEspacio
    {
        get
        {
            if (CarpetaSalida is not string salida || elegidos.Count == 0) return null;
            if (espacio is not { } e) return "Calculando el espacio…";
            var libres = $"quedan libres {Formato.Tamano(e.Libres)} en {Path.GetPathRoot(salida)}";
            return e.Pendientes < bytesElegidos
                ? $"Faltan copiar {Formato.Tamano(e.Pendientes)} de {Formato.Tamano(bytesElegidos)} (lo demás ya está en la carpeta) · {libres}"
                : $"Se copian {Formato.Tamano(bytesElegidos)} · {libres}";
        }
    }

    bool PuedeExtraer => !Leyendo && !Extrayendo && elegidos.Count > 0 && CarpetaSalida is not null
                         && espacio is not null && ErrorSalida is null;

    bool PuedeCancelar => Extrayendo;

    async Task Buscar()
    {
        try
        {
            var copias = await Task.Run(() => CopiaItunes.Buscar(CopiaItunes.UbicacionesHabituales()));
            Copias = copias.Select(c => new CopiaVista(c)).ToList();
            Seleccionada = Copias.FirstOrDefault(c => !c.Copia.Encriptada) ?? Copias.FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = "No se pudieron buscar las copias de iTunes: " + e.Message;
        }
        finally
        {
            // Pase lo que pase deja de buscar: si no, la pantalla queda en "Buscando copias…"
            Buscando = false;
        }
    }

    async partial void OnSeleccionadaChanged(CopiaVista? value)
    {
        // Si mientras se lee el indice se elige otra copia, lo leido se descarta
        var esta = ++lectura;
        archivos = [];
        Error = null;
        foreach (var c in Contenidos) c.Resumir([]);
        Actualizar();
        if (value is null) return;

        // La carpeta sugerida es por dispositivo: si no se cambio a mano, sigue a la copia elegida
        if (string.IsNullOrWhiteSpace(Salida) || Salida == sugerida)
        {
            sugerida = SalidaSugerida(value.Copia);
            Salida = sugerida;
        }
        if (value.Copia.Encriptada)
        {
            Error = "Esta copia está encriptada (tiene contraseña). Todavía no se puede leer: en iTunes o en la app Dispositivos de Apple, "
                    + "desmarca \"Encriptar copia local\" y vuelve a hacer la copia.";
            return;
        }

        Leyendo = true;
        try
        {
            var leidos = await Task.Run(() => value.Copia.Listar(ContenidoItunes.Todo));
            if (esta != lectura) return;
            archivos = leidos;
            foreach (var c in Contenidos) c.Resumir(leidos.Where(a => a.Contenido == c.Contenido).ToList());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (esta == lectura) Error = "No se pudo leer la copia: " + e.Message;
        }
        finally
        {
            if (esta == lectura) Leyendo = false;
            Actualizar();
        }
    }

    // Al lado de la carpeta organizada, para que quede en el mismo disco
    string SalidaSugerida(CopiaItunes copia)
    {
        var organizada = principal.Carpetas.Destino;
        if (string.IsNullOrWhiteSpace(organizada)) return "";
        try
        {
            var completa = Path.TrimEndingDirectorySeparator(Path.GetFullPath(organizada));
            return Path.Combine(Path.GetDirectoryName(completa) ?? completa, copia.NombreSugerido);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    /// <summary>Cambio la copia, lo leido o que contenidos se incluyen.</summary>
    internal void Actualizar()
    {
        var elegido = Elegido;
        elegidos = archivos.Where(a => (elegido & a.Contenido) != 0).ToList();
        bytesElegidos = elegidos.Sum(a => a.Bytes);
        CalcularEspacio();
    }

    partial void OnSalidaChanged(string value) => CalcularEspacio();

    // Mira en la carpeta de salida que ya esta copiado: un archivo por archivo, asi que se hace aparte y,
    // mientras se escribe la ruta, recien cuando se deja de escribir
    async void CalcularEspacio()
    {
        var este = ++calculo;
        espacio = null;
        Notificar();
        if (CarpetaSalida is not string salida || elegidos.Count == 0) return;

        var lista = elegidos;
        var total = bytesElegidos;
        await Task.Delay(300);
        if (este != calculo) return;
        var calculado = await Task.Run(() =>
        {
            long pendientes;
            try
            {
                pendientes = Directory.Exists(salida) ? ExtractorItunes.BytesPendientes(lista, salida) : total;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                pendientes = total;
            }
            return (pendientes, EspacioLibre(salida));
        });
        if (este != calculo) return;
        espacio = calculado;
        Notificar();
    }

    void Notificar()
    {
        OnPropertyChanged(nameof(ErrorSalida));
        OnPropertyChanged(nameof(TextoEspacio));
        ExtraerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    async Task ElegirCopia()
    {
        if (await principal.Dialogos.ElegirCarpeta("Copia de seguridad de iTunes", CopiaItunes.UbicacionesHabituales()[0]) is not string carpeta) return;
        try
        {
            var vista = new CopiaVista(CopiaItunes.Abrir(carpeta));
            Copias = [.. Copias.Where(c => !c.Copia.Carpeta.Equals(carpeta, StringComparison.OrdinalIgnoreCase)), vista];
            Seleccionada = vista;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    async Task ElegirSalida()
    {
        if (await principal.Dialogos.ElegirCarpeta("Dónde copiar las fotos", Salida) is string carpeta) Salida = carpeta;
    }

    [RelayCommand(CanExecute = nameof(PuedeExtraer))]
    async Task Extraer()
    {
        var copia = Seleccionada!.Copia;
        var salida = CarpetaSalida!;
        var aCopiar = elegidos;
        cancelar = new CancellationTokenSource();
        Error = null;
        Porcentaje = 0;
        Detalle = "Preparando…";
        Extrayendo = true;
        try
        {
            var progreso = new ProgresoUi<(int Hechos, int Total)>(p =>
            {
                Porcentaje = 100.0 * p.Hechos / p.Total;
                Detalle = $"{Formato.Cantidad(p.Hechos)} de {Formato.Cantidad(p.Total)} archivos";
            });
            var token = cancelar.Token;
            var r = await Task.Run(() =>
            {
                // Lo leido es de cuando se eligio la copia: si despues iTunes empezo a hacerla de nuevo, se espera
                if (!copia.EstaTerminada())
                    throw new IOException("iTunes está haciendo esta copia ahora: espera a que termine y vuelve a elegirla.");
                return ExtractorItunes.Extraer(aCopiar, salida, progreso, token);
            }, token);

            var errores = r.Errores.Count == 0 ? "" : $" {Formato.Cantidad(r.Errores.Count)} no se pudieron copiar (por ejemplo {r.Errores[0]}).";
            principal.Carpetas.Origen = salida;
            principal.Carpetas.Aviso = $"Se copiaron {Formato.Cantidad(r.Copiados + r.YaEstaban)} archivos de {copia.Dispositivo} a {salida}.{errores} "
                                       + "Ya puedes analizarla; la copia de iTunes quedó igual.";
            principal.VolverAlInicio();
        }
        catch (OperationCanceledException)
        {
            Detalle = null;
            Error = "Cancelado. Lo copiado queda en la carpeta: si vuelves a extraer, sigue desde ahí.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
        finally
        {
            Extrayendo = false;
            Actualizar();
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeCancelar))]
    void Cancelar()
    {
        Detalle = "Cancelando…";
        cancelar?.Cancel();
    }

    [RelayCommand]
    void Volver() => principal.VolverAlInicio();

    static bool Dentro(string ruta, string carpeta)
    {
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpeta));
        return ruta.Equals(c, StringComparison.OrdinalIgnoreCase) || ruta.StartsWith(c + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static long EspacioLibre(string carpeta)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(carpeta)!).AvailableFreeSpace;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }
}

/// <summary>Una copia de iTunes en la lista.</summary>
public sealed class CopiaVista(CopiaItunes copia)
{
    public CopiaItunes Copia { get; } = copia;

    public string Nombre => Copia.Dispositivo;

    public string Detalle
    {
        get
        {
            var partes = new List<string>();
            if (Copia.VersionIos is string ios) partes.Add("iOS " + ios);
            if (Copia.Fecha is DateTime f) partes.Add("copia del " + f.ToLocalTime().ToString("d 'de' MMMM 'de' yyyy, HH:mm", CultureInfo.CurrentCulture));
            if (Copia.Encriptada) partes.Add("encriptada");
            return string.Join(" · ", partes);
        }
    }
}

/// <summary>Carrete, editadas o WhatsApp: si se incluye y cuanto hay.</summary>
public partial class ContenidoVista(ImportarItunesViewModel importar, ContenidoItunes contenido, string nombre, string descripcion)
    : ObservableObject
{
    public ContenidoItunes Contenido { get; } = contenido;

    public string Nombre { get; } = nombre;

    public string Descripcion { get; } = descripcion;

    [ObservableProperty]
    public partial bool Incluir { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayArchivos))]
    public partial string Resumen { get; set; } = "";

    public bool HayArchivos => Resumen.Length > 0;

    partial void OnIncluirChanged(bool value) => importar.Actualizar();

    public void Resumir(IReadOnlyList<ArchivoItunes> archivos)
    {
        if (archivos.Count == 0)
        {
            Resumen = "";
            return;
        }
        var fotos = archivos.Count(a => Clasificador.TipoDe(a.Destino) == TipoArchivo.Foto);
        var videos = archivos.Count - fotos;
        Resumen = $"{Formato.Cantidad(fotos)} fotos y {Formato.Cantidad(videos)} videos · {Formato.Tamano(archivos.Sum(a => a.Bytes))}";
    }
}
