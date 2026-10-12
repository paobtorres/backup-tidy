using BackupTidy.Core.Ejecucion;
using BackupTidy.Core.Plan;

namespace BackupTidy.Tests.Ejecucion;

public sealed class EjecutorTests : IDisposable
{
    readonly CarpetaTemporal origen = new();
    readonly CarpetaTemporal destino = new();
    readonly CarpetaTemporal reportes = new();

    public void Dispose()
    {
        origen.Dispose();
        destino.Dispose();
        reportes.Dispose();
    }

    string Registro => reportes["movimientos.csv"];

    FilaPlan Mover(string relativa, string relativaDestino, string contenido = "contenido")
    {
        var ruta = origen.Crear(relativa, contenido);
        return new(CategoriaPlan.Fotos, ruta, destino[relativaDestino], contenido.Length);
    }

    FilaPlan Descartar(string relativa)
    {
        var ruta = origen.Crear(relativa, "audio");
        return new(CategoriaPlan.Descartar, ruta, null, 5);
    }

    ResultadoEjecucion Ejecutar(IEnumerable<FilaPlan> filas, IPapelera? papelera = null) =>
        Ejecutor.Ejecutar(filas.ToList(), Registro, origen.Ruta, papelera);

    [Fact]
    public void Mueve_registra_y_deshace()
    {
        FilaPlan[] plan = [Mover(@"iPhone\a.jpg", @"Fotos\2019\2019-03\a.jpg"), Mover(@"iPhone\b.jpg", @"Fotos\2019\2019-04\b.jpg")];

        var r = Ejecutar(plan);

        Assert.Equal((2, 0, 0, false), (r.Movidos, r.Omitidos, r.Errores, r.Cancelado));
        Assert.True(File.Exists(destino[@"Fotos\2019\2019-03\a.jpg"]));
        Assert.False(File.Exists(origen[@"iPhone\a.jpg"]));

        var d = Ejecutor.Deshacer(Registro);

        Assert.Equal(new ResultadoDeshacer(2, 0, 0), d);
        Assert.Equal("contenido", File.ReadAllText(origen[@"iPhone\a.jpg"]));
        Assert.False(File.Exists(destino[@"Fotos\2019\2019-03\a.jpg"]));
    }

    [Fact]
    public void Nunca_sobrescribe_un_destino_existente()
    {
        destino.Crear(@"Fotos\a.jpg", "otro archivo");

        var r = Ejecutar([Mover(@"iPhone\a.jpg", @"Fotos\a.jpg")]);

        Assert.Equal((0, 1), (r.Movidos, r.Omitidos));
        Assert.Equal("otro archivo", File.ReadAllText(destino[@"Fotos\a.jpg"]));
        Assert.True(File.Exists(origen[@"iPhone\a.jpg"]));
    }

    [Fact]
    public void Omite_si_el_archivo_cambio_o_ya_no_esta()
    {
        var cambiado = Mover(@"iPhone\a.jpg", @"Fotos\a.jpg");
        File.AppendAllText(cambiado.Origen, " y algo mas");
        var borrado = Mover(@"iPhone\b.jpg", @"Fotos\b.jpg");
        File.Delete(borrado.Origen);

        var r = Ejecutar([cambiado, borrado]);

        Assert.Equal((0, 2, 0), (r.Movidos, r.Omitidos, r.Errores));
        Assert.Empty(Directory.GetFileSystemEntries(destino.Ruta));
    }

    [Fact]
    public void Cancelar_detiene_y_lo_movido_se_puede_deshacer()
    {
        using var cancelar = new CancellationTokenSource();
        var plan = new[] { Mover("a.jpg", "a.jpg"), Mover("b.jpg", "b.jpg"), Mover("c.jpg", "c.jpg") };
        var progreso = new ProgresoInmediato(hechos => { if (hechos == 2) cancelar.Cancel(); });

        var r = Ejecutor.Ejecutar(plan, Registro, origen.Ruta, progreso: progreso, cancelar: cancelar.Token);

        Assert.Equal((2, true), (r.Movidos, r.Cancelado));
        Assert.True(File.Exists(origen["c.jpg"]));
        Assert.Equal(2, Ejecutor.Deshacer(Registro).Restaurados);
        Assert.Equal(3, Directory.GetFiles(origen.Ruta).Length);
    }

    [Fact]
    public void Deshacer_no_pisa_un_archivo_que_volvio_a_aparecer()
    {
        Ejecutar([Mover("a.jpg", "a.jpg")]);
        origen.Crear("a.jpg", "nuevo");

        var d = Ejecutor.Deshacer(Registro);

        Assert.Equal((0, 1), (d.Restaurados, d.Omitidos));
        Assert.Equal("nuevo", File.ReadAllText(origen["a.jpg"]));
    }

    [Fact]
    public void Sin_papelera_los_descartes_quedan_en_su_lugar()
    {
        var r = Ejecutar([Descartar(@"audios\nota.opus")]);

        Assert.Equal(0, r.EnviadosAPapelera);
        Assert.True(File.Exists(origen[@"audios\nota.opus"]));
    }

    [Fact]
    public void Carpeta_con_solo_descartes_se_envia_entera()
    {
        var papelera = new PapeleraFalsa(capacidad: 1_000_000);
        FilaPlan[] plan = [Descartar(@"audios\nota1.opus"), Descartar(@"audios\sub\nota2.opus"),
                           Descartar(@"mixta\contacto.vcf"), Mover(@"mixta\a.jpg", "a.jpg"), Descartar("raiz.opus")];
        origen.Crear(@"mixta\queda.txt", "no esta en el plan");

        var r = Ejecutar(plan, papelera);

        Assert.Equal(4, r.EnviadosAPapelera);
        Assert.Equal([origen["audios"]], papelera.Carpetas);
        Assert.Equal([origen[@"mixta\contacto.vcf"], origen["raiz.opus"]], papelera.Archivos.Order());
        Assert.DoesNotContain(origen.Ruta, papelera.Carpetas);
        Assert.Equal(new ResultadoDeshacer(1, 0, 4), Ejecutor.Deshacer(Registro));
    }

    [Fact]
    public void Carpeta_que_queda_vacia_tras_mover_se_envia_entera()
    {
        var papelera = new PapeleraFalsa(capacidad: 1_000_000);

        Ejecutar([Mover(@"DCIM\a.jpg", "a.jpg"), Descartar(@"DCIM\a.aae")], papelera);

        Assert.Equal([origen["DCIM"]], papelera.Carpetas);
    }

    [Theory]
    [InlineData(null)]   // Papelera no confirmada o con borrado directo
    [InlineData(5L)]     // No alcanza: hay 10 bytes de descartes
    public void Si_la_papelera_no_alcanza_no_se_envia_nada(long? capacidad)
    {
        var papelera = new PapeleraFalsa(capacidad);

        var r = Ejecutar([Descartar("a.opus"), Descartar("b.opus")], papelera);

        Assert.Equal(0, r.EnviadosAPapelera);
        Assert.Empty(papelera.Archivos);
        Assert.Single(r.Avisos);
    }

    [Fact]
    public void Descartes_fuera_del_origen_no_se_envian()
    {
        var papelera = new PapeleraFalsa(capacidad: 1_000_000);
        var ajeno = destino.Crear("ajeno.opus", "audio");

        var r = Ejecutar([new FilaPlan(CategoriaPlan.Descartar, ajeno, null, 5)], papelera);

        Assert.Equal(0, r.EnviadosAPapelera);
        Assert.True(File.Exists(ajeno));
    }

    sealed class PapeleraFalsa(long? capacidad) : IPapelera
    {
        public List<string> Archivos { get; } = [];
        public List<string> Carpetas { get; } = [];

        public long? Capacidad(string ruta) => capacidad;

        public void EnviarArchivo(string ruta)
        {
            Archivos.Add(ruta);
            File.Delete(ruta);
        }

        public void EnviarCarpeta(string ruta)
        {
            Carpetas.Add(ruta);
            Directory.Delete(ruta, recursive: true);
        }
    }

    sealed class ProgresoInmediato(Action<int> alInformar) : IProgress<(int Hechos, int Total)>
    {
        public void Report((int Hechos, int Total) valor) => alInformar(valor.Hechos);
    }
}
