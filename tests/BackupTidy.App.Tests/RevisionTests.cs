using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BackupTidy.App.ViewModels;
using BackupTidy.App.Views;
using BackupTidy.Core.Plan;
using BackupTidy.Tests;

namespace BackupTidy.App.Tests;

/// <summary>Paso 3: filtros, subcarpetas, seleccion y que se organiza solo lo seleccionado.</summary>
public sealed class RevisionTests : IDisposable
{
    readonly CarpetaTemporal origen = new();
    readonly CarpetaTemporal destino = new();
    readonly DialogosDePrueba dialogos = new();

    public RevisionTests()
    {
        var rnd = new Random(1);
        byte[] Datos(int bytes)
        {
            var b = new byte[bytes];
            rnd.NextBytes(b);
            return b;
        }
        var repetida = Datos(3000);
        origen.Crear(@"DCIM\100APPLE\IMG_0001.JPG", repetida);
        origen.Crear(@"DCIM\100APPLE\IMG_0002.JPG", Datos(2000));
        origen.Crear(@"DCIM\100APPLE\IMG_0003.JPG", Datos(2500));
        origen.Crear(@"Copias\IMG_0001.JPG", repetida);
        origen.Crear(@"Copias\IMG_0001 (1).JPG", repetida);
        origen.Crear(@"WhatsApp\Media\IMG-20240101-WA0001.jpg", Datos(1000));
        origen.Crear(@"WhatsApp\Media\IMG-20240101-WA0002.jpg", Datos(1100));
        origen.Crear("nota.opus", "audio");
    }

    public void Dispose()
    {
        origen.Dispose();
        destino.Dispose();
    }

    RevisionViewModel Abrir(out MainViewModel principal)
    {
        var opciones = new OpcionesAnalisis(origen.Ruta, destino.Ruta);
        principal = new MainViewModel(dialogos);
        var revision = new RevisionViewModel(principal, opciones, Analizador.Analizar(opciones));
        principal.IrA(revision, 3);
        return revision;
    }

    RevisionViewModel Abrir() => Abrir(out _);

    static IReadOnlyList<FilaVista> Filas(RevisionViewModel r) => r.TodasLasFilas;

    [Fact]
    public Task El_filtro_de_duplicados_separa_y_cuenta() => Ui.Ejecutar(() =>
    {
        var r = Abrir();
        var todas = Filas(r).ToList();
        var duplicados = todas.Count(f => f.Fila.EsDuplicado);
        Assert.Equal(2, duplicados);

        Assert.Equal([todas.Count.ToString(), duplicados.ToString(), (todas.Count - duplicados).ToString()],
                     r.OpcionesDuplicados.Select(o => o.Cantidad));

        r.DuplicadosSeleccionado = r.OpcionesDuplicados[1];
        Assert.Equal(duplicados, r.FilasVisibles.Count);
        Assert.All(r.FilasVisibles, f => Assert.True(f.Fila.EsDuplicado));
        // Las subcarpetas cuentan solo lo que pasa el filtro
        Assert.Equal(duplicados, r.Subcarpetas.Skip(1).Sum(s => int.Parse(s.Resumen.Split(' ')[0])));

        r.DuplicadosSeleccionado = r.OpcionesDuplicados[2];
        Assert.All(r.FilasVisibles, f => Assert.False(f.Fila.EsDuplicado));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Una_subcarpeta_muestra_solo_sus_archivos() => Ui.Ejecutar(() =>
    {
        var r = Abrir();

        r.SubcarpetaSeleccionada = r.Subcarpetas.Single(s => s.Carpeta == @"WhatsApp\Media");

        Assert.Equal(["IMG-20240101-WA0001.jpg", "IMG-20240101-WA0002.jpg"], r.FilasVisibles.Select(f => f.Nombre).Order());
        Assert.Equal("2 archivos · 2 KB", r.SubcarpetaSeleccionada.Resumen);
        Assert.Equal(r.SubcarpetaSeleccionada.Carpeta, r.CarpetaDelBackupSeleccionada?.Carpeta);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Al_empezar_esta_todo_seleccionado() => Ui.Ejecutar(() =>
    {
        var r = Abrir();
        var todas = Filas(r).ToList();

        Assert.All(todas, f => Assert.True(f.Elegida));
        Assert.All(r.Categorias, c => Assert.True(c.Eleccion));
        Assert.All(r.CarpetasDelBackup, c => Assert.True(c.Eleccion));
        Assert.StartsWith($"{todas.Count - 1} archivos", r.TextoAMover);   // sin el descarte
        Assert.True(r.PedirConfirmacionCommand.CanExecute(null));
        return Task.CompletedTask;
    });

    [Fact]
    public Task La_casilla_de_una_categoria_la_quita_y_la_vuelve_a_poner_entera() => Ui.Ejecutar(() =>
    {
        var r = Abrir();
        var categoria = r.Categorias.Single(c => c.Categoria == CategoriaPlan.Duplicados);

        r.ElegirCategoriaCommand.Execute(categoria);

        var todas = Filas(r).ToList();
        Assert.All(todas, f => Assert.Equal(!f.Fila.EsDuplicado, f.Elegida));
        Assert.False(categoria.Eleccion);
        Assert.Null(r.Categorias[0].Eleccion);    // "Todo" queda a medias

        r.ElegirCategoriaCommand.Execute(categoria);
        Assert.All(todas, f => Assert.True(f.Elegida));
        Assert.True(r.Categorias[0].Eleccion);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_medias_la_casilla_de_la_categoria_selecciona_todo() => Ui.Ejecutar(() =>
    {
        var r = Abrir();
        var todo = r.Categorias[0];
        r.FilasVisibles[0].Elegida = false;
        Assert.Null(todo.Eleccion);

        r.ElegirCategoriaCommand.Execute(todo);

        Assert.True(todo.Eleccion);
        Assert.All(Filas(r), f => Assert.True(f.Elegida));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Seleccionar_los_de_la_lista_solo_toca_lo_visible() => Ui.Ejecutar(() =>
    {
        var r = Abrir();
        r.DuplicadosSeleccionado = r.OpcionesDuplicados[1];

        r.ElegirVisiblesCommand.Execute(null);

        Assert.False(r.TodosVisiblesElegidos);
        Assert.All(Filas(r), f => Assert.Equal(!f.Fila.EsDuplicado, f.Elegida));
        Assert.Contains("6 de 8 archivos seleccionados", r.TextoSeleccion);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Una_subcarpeta_se_selecciona_entera_con_todas_sus_categorias() => Ui.Ejecutar(() =>
    {
        var r = Abrir();
        var copias = r.CarpetasDelBackup.Single(c => c.Carpeta == "Copias");
        // Filtrando otra categoria igual se quita la subcarpeta entera
        r.CategoriaSeleccionada = r.Categorias.Single(c => c.Categoria == CategoriaPlan.WhatsApp);

        r.ElegirCarpetaCommand.Execute(copias);

        Assert.False(copias.Eleccion);
        Assert.All(Filas(r), f => Assert.Equal(f.Carpeta != "Copias", f.Elegida));
        Assert.True(r.CarpetasDelBackup.Single(c => c.Carpeta == @"WhatsApp\Media").Eleccion);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Elegir_una_subcarpeta_en_el_menu_la_muestra_aunque_otro_filtro_la_ocultaba() => Ui.Ejecutar(() =>
    {
        var r = Abrir();
        r.CategoriaSeleccionada = r.Categorias.Single(c => c.Categoria == CategoriaPlan.WhatsApp);

        r.CarpetaDelBackupSeleccionada = r.CarpetasDelBackup.Single(c => c.Carpeta == "Copias");

        Assert.Null(r.CategoriaSeleccionada?.Categoria);   // vuelve a "Todo"
        Assert.Equal("Copias", r.SubcarpetaSeleccionada?.Carpeta);
        Assert.Equal(2, r.FilasVisibles.Count);
        Assert.All(r.FilasVisibles, f => Assert.Equal("Copias", f.Carpeta));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Sin_nada_seleccionado_no_se_puede_organizar() => Ui.Ejecutar(() =>
    {
        var r = Abrir();
        r.ElegirCategoriaCommand.Execute(r.Categorias[0]);
        Assert.False(r.PedirConfirmacionCommand.CanExecute(null));

        // Solo el descarte: hay algo que hacer si va a la Papelera
        r.ElegirCategoriaCommand.Execute(r.Categorias.Single(c => c.Categoria == CategoriaPlan.Descartar));
        Assert.False(r.PedirConfirmacionCommand.CanExecute(null));
        r.EnviarDescartesAPapelera = true;
        Assert.True(r.PedirConfirmacionCommand.CanExecute(null));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Guardar_plan_guarda_solo_lo_que_se_organiza() => Ui.Ejecutar(async () =>
    {
        var r = Abrir();
        r.ElegirCategoriaCommand.Execute(r.Categorias.Single(c => c.Categoria == CategoriaPlan.Duplicados));
        dialogos.Csv = destino["plan.csv"];

        await r.GuardarPlanCommand.ExecuteAsync(null);

        var guardado = PlanCsv.Leer(destino["plan.csv"]);
        Assert.Equal(Filas(r).Count(f => f.Elegida), guardado.Count);
        Assert.DoesNotContain(guardado, f => f.EsDuplicado);
        Assert.Contains("archivos seleccionados", r.Mensaje);
    });

    [Fact]
    public Task Organizar_mueve_solo_lo_seleccionado() => Ui.Ejecutar(async () =>
    {
        var r = Abrir(out var principal);
        r.ElegirCategoriaCommand.Execute(r.Categorias.Single(c => c.Categoria == CategoriaPlan.Duplicados));
        var quitada = Filas(r).First(f => f.Fila.Categoria == CategoriaPlan.WhatsApp);
        quitada.Elegida = false;
        var aMover = Filas(r).Where(f => f.Elegida && f.Fila.Categoria != CategoriaPlan.Descartar).ToList();

        r.PedirConfirmacionCommand.Execute(null);
        await r.OrganizarCommand.ExecuteAsync(null);
        var resultado = Assert.IsType<ResultadoViewModel>(principal.Pagina);
        await Ui.Esperar(() => !resultado.Trabajando, "terminar de organizar");

        Assert.All(aMover, f => Assert.True(File.Exists(f.Fila.Destino), f.Fila.Destino));
        Assert.All(aMover, f => Assert.False(File.Exists(f.Fila.Origen)));
        // Lo que no estaba seleccionado quedo donde estaba
        Assert.True(File.Exists(quitada.Fila.Origen));
        Assert.All(Filas(r).Where(f => f.Fila.EsDuplicado), f => Assert.True(File.Exists(f.Fila.Origen)));
    });

    [Fact]
    public Task En_la_pantalla_la_casilla_de_una_categoria_responde_al_clic() => Ui.Ejecutar(async () =>
    {
        var r = Abrir();
        var ventana = new Window { Width = 1240, Height = 800, Content = new RevisionView { DataContext = r } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        var categoria = r.Categorias.Single(c => c.Categoria == CategoriaPlan.Duplicados);
        var casilla = ventana.GetVisualDescendants().OfType<CheckBox>()
                             .Single(c => c.Classes.Contains("eleccion") && ReferenceEquals(c.DataContext, categoria));

        void Clic()
        {
            var centro = casilla.TranslatePoint(new Point(casilla.Bounds.Width / 2, casilla.Bounds.Height / 2), ventana)!.Value;
            ventana.MouseDown(centro, MouseButton.Left);
            ventana.MouseUp(centro, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        Clic();
        Assert.False(categoria.Eleccion);
        Assert.False(casilla.IsChecked);
        // A medias, un clic selecciona todo (no la quita)
        r.FilasVisibles.First(f => f.Fila.EsDuplicado).Elegida = true;
        Assert.Null(casilla.IsChecked);
        Clic();
        Assert.True(categoria.Eleccion);
        Assert.True(casilla.IsChecked);
        ventana.Close();
        await Task.CompletedTask;
    });
}
