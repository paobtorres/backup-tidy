using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using BackupTidy.App.Servicios;
using BackupTidy.Core.Clasificacion;
using BackupTidy.Core.Fechas;
using BackupTidy.Core.Plan;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupTidy.App.ViewModels;

/// <summary>Paso 3: revisar el plan antes de mover nada.</summary>
public partial class RevisionViewModel : ViewModelBase
{
    readonly MainViewModel principal;
    readonly OpcionesAnalisis opciones;
    readonly IReadOnlyList<FilaPlan> plan;
    readonly List<FilaVista> filas;
    readonly DispatcherTimer esperaBusqueda;
    readonly Lazy<Dictionary<string, string>> fotosLive;
    int cargaVista;

    // Lo que pasa los filtros de categoria, fechas, busqueda y duplicados: de ahi salen las subcarpetas
    List<FilaVista> filtradas = [];
    bool armandoSubcarpetas;

    // Las filas de cada categoria y de cada subcarpeta, para elegir o quitar una entera
    readonly Dictionary<CategoriaPlan, List<FilaVista>> porCategoria;
    readonly Dictionary<string, List<FilaVista>> porCarpeta;

    // Al elegir una subcarpeta en el menu se elige en la lista, y al reves: evita que se llamen entre si
    bool sincronizandoCarpeta;

    // Lo elegido que se va a organizar (sin los JPG cuyo HEIC no entra)
    IReadOnlyList<FilaPlan> aOrganizar = [];
    int moverElegidos, descartesElegidos;
    long bytesAMover;

    // Al elegir muchas filas juntas se recalcula una vez al final, no por cada fila
    bool eligiendoVarias;

    public RevisionViewModel(MainViewModel principal, OpcionesAnalisis opciones, ResultadoAnalisis resultado)
    {
        this.principal = principal;
        this.opciones = opciones;
        plan = resultado.Filas;
        Avisos = resultado.Avisos;
        filas = plan.Select(f => new FilaVista(f, opciones.Origen, opciones.Destino, AlCambiarEleccion)).ToList();
        porCategoria = filas.GroupBy(f => f.Fila.Categoria).ToDictionary(g => g.Key, g => g.ToList());
        porCarpeta = filas.GroupBy(f => f.Carpeta, StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        CarpetasDelBackup = Analizador.ResumenPorCarpeta(plan, opciones.Origen)
                                      .Select(r => new SubcarpetaVista(r.Carpeta, r.Archivos, r.Bytes)).ToList();
        fotosLive = new(FotosLive);
        esperaBusqueda = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        esperaBusqueda.Tick += (_, _) =>
        {
            esperaBusqueda.Stop();
            Filtrar();
        };

        HayDescartes = plan.Any(f => f.Categoria == CategoriaPlan.Descartar);
        CantidadDudosas = plan.Count(f => f.FuenteFecha == FuenteFecha.ArchivoDudoso);

        Categorias = [new CategoriaVista(null, plan.Count, plan.Sum(f => f.Bytes)),
                      .. Analizador.Resumen(plan).Select(r => new CategoriaVista(r.Categoria, r.Archivos, r.Bytes))];
        ActualizarEleccion();
        OpcionesDuplicados =
        [
            new(FiltroDuplicados.Todos, "Todos"),
            new(FiltroDuplicados.Duplicados, "Duplicados"),
            new(FiltroDuplicados.SinDuplicar, "Sin duplicar"),
        ];
        DuplicadosSeleccionado = OpcionesDuplicados[0];
        CategoriaSeleccionada = Categorias[0];
    }

    public string Origen => opciones.Origen;

    public string Destino => opciones.Destino;

    public IReadOnlyList<string> Avisos { get; }

    public bool HayAvisos => Avisos.Count > 0;

    public IReadOnlyList<CategoriaVista> Categorias { get; }

    /// <summary>Todas las filas del plan, sin filtros (para las pruebas).</summary>
    internal IReadOnlyList<FilaVista> TodasLasFilas => filas;

    /// <summary>Lo seleccionado que se mueve (sin los descartes): cantidad y tamano.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoConfirmacion))]
    public partial string TextoAMover { get; set; } = "";

    [ObservableProperty]
    public partial string TextoDescartes { get; set; } = "";

    /// <summary>"1.234 de 25.000 archivos seleccionados".</summary>
    [ObservableProperty]
    public partial string TextoSeleccion { get; set; } = "";

    /// <summary>Si estan seleccionados todos los de la lista (null: algunos).</summary>
    [ObservableProperty]
    public partial bool? TodosVisiblesElegidos { get; set; }

    public bool HayDescartes { get; }

    public int CantidadDudosas { get; }

    public string TextoDudosas => $"Solo fechas dudosas ({Formato.Cantidad(CantidadDudosas)})";

    [ObservableProperty]
    public partial CategoriaVista? CategoriaSeleccionada { get; set; }

    /// <summary>Todos, solo los duplicados o solo lo que no esta duplicado; cada uno con su cantidad y tamano.</summary>
    public IReadOnlyList<OpcionDuplicados> OpcionesDuplicados { get; }

    [ObservableProperty]
    public partial OpcionDuplicados? DuplicadosSeleccionado { get; set; }

    /// <summary>"Todas" y cada subcarpeta del backup con lo que queda despues de los demas filtros.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SubcarpetaVista> Subcarpetas { get; set; } = [];

    [ObservableProperty]
    public partial SubcarpetaVista? SubcarpetaSeleccionada { get; set; }

    /// <summary>Todas las subcarpetas del backup, con todos sus archivos: para seleccionarlas enteras desde el menu.</summary>
    public IReadOnlyList<SubcarpetaVista> CarpetasDelBackup { get; }

    /// <summary>La subcarpeta elegida en el menu: es la misma que se ve en la lista.</summary>
    [ObservableProperty]
    public partial SubcarpetaVista? CarpetaDelBackupSeleccionada { get; set; }

    [ObservableProperty]
    public partial string Busqueda { get; set; } = "";

    [ObservableProperty]
    public partial bool SoloDudosas { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinFilas), nameof(HayFilas), nameof(TextoTodosVisibles))]
    public partial IReadOnlyList<FilaVista> FilasVisibles { get; set; } = [];

    public bool HayFilas => FilasVisibles.Count > 0;

    public string TextoTodosVisibles => $"Seleccionar los {Formato.Cantidad(FilasVisibles.Count)} de la lista";

    public bool SinFilas => FilasVisibles.Count == 0;

    [ObservableProperty]
    public partial FilaVista? Seleccionada { get; set; }

    [ObservableProperty]
    public partial Bitmap? VistaArchivo { get; set; }

    [ObservableProperty]
    public partial Bitmap? VistaRelacionado { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PedirConfirmacionCommand))]
    public partial bool EnviarDescartesAPapelera { get; set; }

    [ObservableProperty]
    public partial bool Confirmando { get; set; }

    [ObservableProperty]
    public partial string? Mensaje { get; set; }

    public string TextoConfirmacion =>
        $"Se van a mover los {Formato.Cantidad(moverElegidos)} archivos seleccionados ({Formato.Tamano(bytesAMover)}) a {Destino}; "
        + "lo que no está seleccionado queda donde está. "
        + "No se borra nada: cada movimiento queda registrado y se puede deshacer."
        + (EnviarDescartesAPapelera ? " Los descartes seleccionados van a la Papelera de reciclaje." : "");

    partial void OnCategoriaSeleccionadaChanged(CategoriaVista? value) => Filtrar();

    // Con decenas de miles de filas se filtra cuando se deja de escribir, no en cada tecla
    partial void OnBusquedaChanged(string value)
    {
        esperaBusqueda.Stop();
        esperaBusqueda.Start();
    }

    partial void OnSoloDudosasChanged(bool value) => Filtrar();

    partial void OnDuplicadosSeleccionadoChanged(OpcionDuplicados? value) => Filtrar();

    partial void OnSubcarpetaSeleccionadaChanged(SubcarpetaVista? value)
    {
        if (!sincronizandoCarpeta)
        {
            sincronizandoCarpeta = true;
            CarpetaDelBackupSeleccionada = value?.Carpeta is string carpeta
                ? CarpetasDelBackup.FirstOrDefault(c => string.Equals(c.Carpeta, carpeta, StringComparison.OrdinalIgnoreCase))
                : null;
            sincronizandoCarpeta = false;
        }
        if (!armandoSubcarpetas) MostrarSubcarpeta();
    }

    // Elegir una subcarpeta en el menu la muestra en la lista; si los filtros la dejan vacia, se vuelve a ver todo
    partial void OnCarpetaDelBackupSeleccionadaChanged(SubcarpetaVista? value)
    {
        if (sincronizandoCarpeta || value?.Carpeta is not string carpeta) return;
        sincronizandoCarpeta = true;
        if (EnLista(carpeta) is null)
        {
            CategoriaSeleccionada = Categorias[0];
            DuplicadosSeleccionado = OpcionesDuplicados[0];
        }
        SubcarpetaSeleccionada = EnLista(carpeta) ?? Subcarpetas[0];
        sincronizandoCarpeta = false;
    }

    SubcarpetaVista? EnLista(string carpeta) =>
        Subcarpetas.FirstOrDefault(s => string.Equals(s.Carpeta, carpeta, StringComparison.OrdinalIgnoreCase));

    partial void OnEnviarDescartesAPapeleraChanged(bool value) => OnPropertyChanged(nameof(TextoConfirmacion));

    async partial void OnSeleccionadaChanged(FilaVista? value)
    {
        // Las vistas previas se cargan en segundo plano; si mientras tanto se elige otra fila, se descartan
        var carga = ++cargaVista;
        VistaArchivo = VistaRelacionado = null;
        if (value is null) return;
        // Si el video de una Live Photo no tiene miniatura (sin el codec HEVC, por ejemplo), se ve su foto
        var archivo = await Miniaturas.Cargar(value.Fila.Origen, 640)
                      ?? await Miniaturas.Cargar(FotoDeLivePhoto(value.Fila), 640);
        var relacionado = await Miniaturas.Cargar(value.Fila.Relacionado, 640);
        if (carga != cargaVista) return;
        VistaArchivo = archivo;
        VistaRelacionado = relacionado;
    }

    string? FotoDeLivePhoto(FilaPlan fila) =>
        fila.Categoria == CategoriaPlan.LivePhotos
            ? fotosLive.Value.GetValueOrDefault(Clasificador.ClaveLive(Path.GetRelativePath(Origen, fila.Origen)))
            : null;

    // Las fotos del backup con la clave que empareja el video de una Live Photo con su foto
    Dictionary<string, string> FotosLive()
    {
        var porClave = new Dictionary<string, string>();
        foreach (var fila in plan.Where(f => Clasificador.PuedeSerFotoLive(f.Origen)))
            porClave.TryAdd(Clasificador.ClaveLive(Path.GetRelativePath(Origen, fila.Origen)), fila.Origen);
        return porClave;
    }

    void Filtrar()
    {
        IEnumerable<FilaVista> visibles = filas;
        if (CategoriaSeleccionada?.Categoria is CategoriaPlan c) visibles = visibles.Where(f => f.Fila.Categoria == c);
        if (SoloDudosas) visibles = visibles.Where(f => f.EsDudosa);
        if (!string.IsNullOrWhiteSpace(Busqueda))
        {
            var texto = Busqueda.Trim();
            visibles = visibles.Where(f => f.Fila.Origen.Contains(texto, StringComparison.OrdinalIgnoreCase));
        }
        var conFiltros = visibles.ToList();

        // Cuanto hay en cada opcion con los demas filtros, para ver de un vistazo que parte esta duplicada
        foreach (var opcion in OpcionesDuplicados) opcion.Contar(conFiltros.Where(f => opcion.Incluye(f.Fila)).Select(f => f.Fila));
        var filtro = DuplicadosSeleccionado ?? OpcionesDuplicados[0];
        filtradas = conFiltros.Where(f => filtro.Incluye(f.Fila)).ToList();

        // Se rearman las subcarpetas con sus cantidades; la elegida se mantiene si sigue habiendo algo en ella
        var anterior = SubcarpetaSeleccionada?.Carpeta;
        armandoSubcarpetas = true;
        Subcarpetas = [new SubcarpetaVista(null, filtradas.Count, filtradas.Sum(f => f.Fila.Bytes)),
                       .. Analizador.ResumenPorCarpeta(filtradas.Select(f => f.Fila), Origen)
                                    .Select(r => new SubcarpetaVista(r.Carpeta, r.Archivos, r.Bytes))];
        SubcarpetaSeleccionada = Subcarpetas.FirstOrDefault(s => anterior is not null && string.Equals(s.Carpeta, anterior, StringComparison.OrdinalIgnoreCase))
                                 ?? Subcarpetas[0];
        armandoSubcarpetas = false;
        MostrarSubcarpeta();
    }

    void MostrarSubcarpeta()
    {
        FilasVisibles = SubcarpetaSeleccionada?.Carpeta is string carpeta
            ? filtradas.Where(f => string.Equals(f.Carpeta, carpeta, StringComparison.OrdinalIgnoreCase)).ToList()
            : filtradas;
        Seleccionada = FilasVisibles.Count > 0 ? FilasVisibles[0] : null;
        TodosVisiblesElegidos = Estado(FilasVisibles);
    }

    void AlCambiarEleccion()
    {
        if (!eligiendoVarias) ActualizarEleccion();
    }

    void Elegir(IReadOnlyList<FilaVista> cuales)
    {
        // Si ya estan todas se quitan; si falta alguna, se eligen todas
        var elegir = Estado(cuales) != true;
        eligiendoVarias = true;
        foreach (var f in cuales) f.Elegida = elegir;
        eligiendoVarias = false;
        ActualizarEleccion();
    }

    // Todas elegidas: true; ninguna: false; algunas: null
    static bool? Estado(IReadOnlyCollection<FilaVista> cuales)
    {
        var elegidas = cuales.Count(f => f.Elegida);
        return elegidas == cuales.Count && elegidas > 0 ? true : elegidas == 0 ? false : null;
    }

    void ActualizarEleccion()
    {
        aOrganizar = Analizador.SoloElegidas(filas.Where(f => f.Elegida).Select(f => f.Fila));
        var mover = aOrganizar.Where(f => f.Categoria != CategoriaPlan.Descartar).ToList();
        var descartes = aOrganizar.Where(f => f.Categoria == CategoriaPlan.Descartar).ToList();
        moverElegidos = mover.Count;
        bytesAMover = mover.Sum(f => f.Bytes);
        descartesElegidos = descartes.Count;

        TextoAMover = $"{Formato.Cantidad(moverElegidos)} archivos · {Formato.Tamano(bytesAMover)}";
        TextoDescartes = $"Enviar a la Papelera los {Formato.Cantidad(descartes.Count)} descartes seleccionados ({Formato.Tamano(descartes.Sum(f => f.Bytes))})";
        TextoSeleccion = $"{Formato.Cantidad(filas.Count(f => f.Elegida))} de {Formato.Cantidad(filas.Count)} archivos seleccionados";
        foreach (var c in Categorias)
            c.Eleccion = Estado(c.Categoria is CategoriaPlan cat ? porCategoria[cat] : filas);
        foreach (var c in CarpetasDelBackup) c.Eleccion = Estado(porCarpeta[c.Carpeta!]);
        TodosVisiblesElegidos = Estado(FilasVisibles);
        PedirConfirmacionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Selecciona (o quita, si ya estan todos) los archivos de la lista, con los filtros que tenga.</summary>
    [RelayCommand]
    void ElegirVisibles() => Elegir(FilasVisibles);

    /// <summary>Selecciona (o quita) una categoria entera; "Todo" es todo el plan.</summary>
    [RelayCommand]
    void ElegirCategoria(CategoriaVista? categoria)
    {
        if (categoria is not null) Elegir(categoria.Categoria is CategoriaPlan c ? porCategoria[c] : filas);
    }

    /// <summary>Selecciona (o quita) una subcarpeta entera, con todas sus categorias.</summary>
    [RelayCommand]
    void ElegirCarpeta(SubcarpetaVista? carpeta)
    {
        if (carpeta?.Carpeta is string c && porCarpeta.TryGetValue(c, out var suyas)) Elegir(suyas);
    }

    [RelayCommand]
    void Volver() => principal.VolverAlInicio();

    [RelayCommand]
    void MostrarEnExplorador(string? ruta)
    {
        if (!string.IsNullOrEmpty(ruta)) principal.Dialogos.MostrarEnExplorador(ruta);
    }

    [RelayCommand]
    async Task GuardarPlan()
    {
        var nombre = $"plan_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        if (await principal.Dialogos.GuardarCsv("Guardar el plan", nombre, Destino) is not string ruta) return;
        try
        {
            // Lo que se va a organizar: solo lo seleccionado, como el plan que queda al organizar
            PlanCsv.Escribir(aOrganizar, ruta);
            Mensaje = $"Plan guardado en {ruta} ({Formato.Cantidad(aOrganizar.Count)} archivos seleccionados)";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Mensaje = "No se pudo guardar el plan: " + e.Message;
        }
    }

    // Hay algo que hacer: archivos para mover, o descartes si van a la Papelera
    bool HayAlgoElegido => moverElegidos > 0 || EnviarDescartesAPapelera && descartesElegidos > 0;

    [RelayCommand(CanExecute = nameof(HayAlgoElegido))]
    void PedirConfirmacion() => Confirmando = true;

    [RelayCommand]
    void CancelarConfirmacion() => Confirmando = false;

    [RelayCommand]
    Task Organizar() => principal.Organizar(aOrganizar, Origen, Destino, EnviarDescartesAPapelera);
}

/// <summary>Una categoria en el resumen; sin categoria es "Todo".</summary>
public sealed partial class CategoriaVista(CategoriaPlan? categoria, int archivos, long bytes) : ObservableObject
{
    static readonly Dictionary<CategoriaPlan, string> Colores = new()
    {
        [CategoriaPlan.Fotos] = "#2F6FDB",
        [CategoriaPlan.Videos] = "#7C4DDB",
        [CategoriaPlan.LivePhotos] = "#B455C7",
        [CategoriaPlan.Capturas] = "#0E9AA7",
        [CategoriaPlan.WhatsApp] = "#25A35A",
        [CategoriaPlan.Guardadas] = "#5C8A2E",
        [CategoriaPlan.Mensajes] = "#2E8FC4",
        [CategoriaPlan.Apps] = "#8A7F6E",
        [CategoriaPlan.Miniaturas] = "#9AA3AE",
        [CategoriaPlan.Duplicados] = "#D9822B",
        [CategoriaPlan.Documentos] = "#4F5D73",
        [CategoriaPlan.DocumentosDuplicados] = "#C9732A",
        [CategoriaPlan.JpgReemplazado] = "#C25B3F",
        [CategoriaPlan.Descartar] = "#A0A7B1",
    };

    public CategoriaPlan? Categoria { get; } = categoria;

    public string Nombre => Categoria is CategoriaPlan c ? Formato.Categoria(c) : "Todo";

    public string Descripcion => Categoria is CategoriaPlan c ? Formato.DescripcionCategoria(c) : "Todos los archivos del backup";

    public string Cantidad { get; } = Formato.Cantidad(archivos);

    public string Tamano { get; } = Formato.Tamano(bytes);

    public IBrush Color { get; } = ColorDe(categoria);

    /// <summary>Si sus archivos estan seleccionados para organizar: todos, ninguno o algunos (null).</summary>
    [ObservableProperty]
    public partial bool? Eleccion { get; set; }

    public static IBrush ColorDe(CategoriaPlan? categoria) => new ImmutableSolidColorBrush(Avalonia.Media.Color.Parse(
        categoria is CategoriaPlan c ? Colores.GetValueOrDefault(c, "#9AA3AE") : "#7D8794"));
}

public enum FiltroDuplicados { Todos, Duplicados, SinDuplicar }

/// <summary>Una opcion del filtro de duplicados, con cuanto hay en ella.</summary>
public partial class OpcionDuplicados(FiltroDuplicados filtro, string nombre) : ObservableObject
{
    public FiltroDuplicados Filtro { get; } = filtro;

    public string Nombre { get; } = nombre;

    [ObservableProperty]
    public partial string Cantidad { get; set; } = "";

    [ObservableProperty]
    public partial string Tamano { get; set; } = "";

    public bool Incluye(FilaPlan fila) => Filtro switch
    {
        FiltroDuplicados.Duplicados => fila.EsDuplicado,
        FiltroDuplicados.SinDuplicar => !fila.EsDuplicado,
        _ => true,
    };

    public void Contar(IEnumerable<FilaPlan> filas)
    {
        int archivos = 0;
        long bytes = 0;
        foreach (var f in filas)
        {
            archivos++;
            bytes += f.Bytes;
        }
        Cantidad = Formato.Cantidad(archivos);
        Tamano = Formato.Tamano(bytes);
    }
}

/// <summary>Una subcarpeta del backup con sus archivos; sin carpeta es "Todas".</summary>
public sealed partial class SubcarpetaVista(string? carpeta, int archivos, long bytes) : ObservableObject
{
    /// <summary>En el menu: si sus archivos estan seleccionados para organizar (null: algunos).</summary>
    [ObservableProperty]
    public partial bool? Eleccion { get; set; }

    public string? Carpeta { get; } = carpeta;

    public string Nombre => Carpeta switch
    {
        null => "Todas las subcarpetas",
        "" => "(carpeta principal)",
        var c => c,
    };

    public string Resumen { get; } = $"{Formato.Cantidad(archivos)} {(archivos == 1 ? "archivo" : "archivos")} · {Formato.Tamano(bytes)}";
}

/// <summary>Una fila del plan preparada para mostrar.</summary>
public sealed partial class FilaVista : ObservableObject
{
    readonly Action? alCambiarEleccion;

    public FilaVista(FilaPlan fila, string origen, string destino, Action? alCambiarEleccion = null)
    {
        this.alCambiarEleccion = alCambiarEleccion;
        Fila = fila;
        Nombre = Path.GetFileName(fila.Origen);
        Carpeta = Analizador.CarpetaDe(fila, origen);
        Destino = fila.Destino is null ? "Queda en su lugar" : Relativa(destino, fila.Destino) ?? fila.Destino;
        Color = CategoriaVista.ColorDe(fila.Categoria);
    }

    public FilaPlan Fila { get; }

    /// <summary>Seleccionado para organizar. Al empezar esta todo seleccionado, como el plan completo.</summary>
    [ObservableProperty]
    public partial bool Elegida { get; set; } = true;

    partial void OnElegidaChanged(bool value) => alCambiarEleccion?.Invoke();

    public string Nombre { get; }

    /// <summary>Carpeta de origen, relativa al backup.</summary>
    public string Carpeta { get; }

    /// <summary>Destino relativo a la carpeta organizada.</summary>
    public string Destino { get; }

    public IBrush Color { get; }

    public string NombreCategoria => Formato.Categoria(Fila.Categoria);

    public string Fecha => Formato.Fecha(Fila.Fecha);

    /// <summary>Los duplicados y documentos no se ordenan por fecha.</summary>
    public bool TieneFecha => Fila.Fecha is not null;

    public string Fuente => Formato.Fuente(Fila.FuenteFecha);

    public bool EsDudosa => Fila.FuenteFecha == FuenteFecha.ArchivoDudoso;

    public string Tamano => Formato.Tamano(Fila.Bytes);

    public bool TieneNota => Fila.Nota.Length > 0;

    public bool TieneRelacionado => Fila.Relacionado is not null;

    /// <summary>Que es el otro archivo que se muestra al lado.</summary>
    public string EtiquetaRelacionado => Fila.Nota switch
    {
        _ when Fila.Categoria == CategoriaPlan.JpgReemplazado => "HEIC que lo reemplaza",
        var n when n.StartsWith("Igual a:") => "Copia que se conserva",
        var n when n.StartsWith("Ya existe en:") => "Ya está organizado",
        var n when n.StartsWith("Vista previa") => "Original",
        var n when n.StartsWith("Mismo nombre y mes") => "Parecido ya organizado",
        var n when n.StartsWith("Reemplaza JPG") => "JPG convertido que reemplaza",
        _ => "Relacionado",
    };

    static string? Relativa(string carpeta, string ruta)
    {
        var relativa = Path.GetRelativePath(carpeta, ruta);
        return relativa.StartsWith("..") || Path.IsPathRooted(relativa) ? null : relativa == "." ? "" : relativa;
    }
}
