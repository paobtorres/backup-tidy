using BackupTidy.App.ViewModels;
using BackupTidy.Core.Itunes;
using BackupTidy.Tests;
using BackupTidy.Tests.Itunes;

namespace BackupTidy.App.Tests;

/// <summary>Pantalla para sacar las fotos de una copia de iTunes.</summary>
public sealed class ImportarItunesTests : IDisposable
{
    const string Carrete = "CameraRollDomain";

    readonly CarpetaTemporal trabajo = new();
    readonly CopiaDePrueba copia = new("iPhone de Pao");
    readonly DialogosDePrueba dialogos = new();
    readonly MainViewModel principal;

    public ImportarItunesTests()
    {
        principal = new MainViewModel(dialogos);
        // La salida se sugiere al lado de la carpeta organizada
        principal.Carpetas.Destino = trabajo["Organizado"];
    }

    public void Dispose()
    {
        copia.Dispose();
        trabajo.Dispose();
    }

    // Sin buscar en las carpetas de iTunes de la maquina: la copia se elige con el dialogo
    async Task<ImportarItunesViewModel> Abrir(params string[] copias)
    {
        var vm = new ImportarItunesViewModel(principal, []);
        await Ui.Esperar(() => !vm.Buscando, "terminar de buscar");
        foreach (var c in copias)
        {
            dialogos.Carpetas.Enqueue(c);
            await vm.ElegirCopiaCommand.ExecuteAsync(null);
        }
        if (copias.Length > 0) await Ui.Esperar(() => !vm.Leyendo, "leer el indice");
        return vm;
    }

    static Task EsperarEspacio(ImportarItunesViewModel vm) =>
        Ui.Esperar(() => vm.TextoEspacio is string t && !t.StartsWith("Calculando"), "calcular el espacio");

    [Fact]
    public Task Sin_copias_deja_de_buscar() => Ui.Ejecutar(async () =>
    {
        var vm = new ImportarItunesViewModel(principal, [trabajo["no existe"]]);

        await Ui.Esperar(() => !vm.Buscando, "terminar de buscar");

        Assert.True(vm.SinCopias);
        Assert.False(vm.HayCopias);
    });

    [Fact]
    public Task La_carpeta_sugerida_sigue_a_la_copia_elegida() => Ui.Ejecutar(async () =>
    {
        using var otra = new CopiaDePrueba("iPhone viejo");
        var vm = await Abrir(copia.Ruta);
        Assert.Equal(trabajo[CopiaItunes.Abrir(copia.Ruta).NombreSugerido], vm.Salida);

        dialogos.Carpetas.Enqueue(otra.Ruta);
        await vm.ElegirCopiaCommand.ExecuteAsync(null);
        Assert.Equal(trabajo[CopiaItunes.Abrir(otra.Ruta).NombreSugerido], vm.Salida);

        // Una carpeta elegida a mano no se cambia
        vm.Salida = trabajo["mi carpeta"];
        vm.Seleccionada = vm.Copias.Single(c => c.Nombre == "iPhone de Pao");
        Assert.Equal(trabajo["mi carpeta"], vm.Salida);
    });

    [Fact]
    public Task El_espacio_cuenta_solo_lo_que_falta_copiar() => Ui.Ejecutar(async () =>
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC", "primera");
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0002.HEIC", "segunda foto");
        var vm = await Abrir(copia.Ruta);
        await EsperarEspacio(vm);
        Assert.StartsWith("Se copian 19 bytes", vm.TextoEspacio);

        // Una extraccion anterior que se corto despues del primer archivo
        var archivos = CopiaItunes.Abrir(copia.Ruta).Listar(ContenidoItunes.Todo);
        ExtractorItunes.Extraer(archivos.Take(1).ToList(), vm.Salida);
        vm.Salida += Path.DirectorySeparatorChar;
        await EsperarEspacio(vm);

        Assert.StartsWith("Faltan copiar 12 bytes de 19 bytes", vm.TextoEspacio);
        Assert.True(vm.ExtraerCommand.CanExecute(null));
    });

    [Fact]
    public Task Sin_nada_que_copiar_no_se_puede_copiar() => Ui.Ejecutar(async () =>
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC");
        var vm = await Abrir(copia.Ruta);
        await EsperarEspacio(vm);
        Assert.True(vm.ExtraerCommand.CanExecute(null));

        vm.Contenidos.Single(c => c.Contenido == ContenidoItunes.Carrete).Incluir = false;

        // Quedan elegidos editadas y WhatsApp, pero no hay nada de eso en la copia
        Assert.False(vm.ExtraerCommand.CanExecute(null));
        Assert.Null(vm.TextoEspacio);
    });

    [Fact]
    public Task Una_copia_sin_terminar_avisa_y_no_se_lee() => Ui.Ejecutar(async () =>
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC");
        copia.Estado("uploading");

        var vm = await Abrir(copia.Ruta);

        Assert.Contains("no está terminada", vm.Error);
        Assert.False(vm.ExtraerCommand.CanExecute(null));
    });

    [Fact]
    public Task Copiar_deja_la_carpeta_lista_para_analizar() => Ui.Ejecutar(async () =>
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC", "foto");
        var vm = await Abrir(copia.Ruta);
        await EsperarEspacio(vm);
        var salida = vm.Salida;

        await vm.ExtraerCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(salida, "DCIM", "100APPLE", "IMG_0001.HEIC")));
        Assert.Equal(salida, principal.Carpetas.Origen);
        Assert.Contains("iPhone de Pao", principal.Carpetas.Aviso);
        Assert.Same(principal.Carpetas, principal.Pagina);
    });
}
