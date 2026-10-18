using BackupTidy.Core.Fechas;
using BackupTidy.Core.Itunes;
using BackupTidy.Core.Plan;

namespace BackupTidy.Tests.Itunes;

public sealed class ExtractorItunesTests : IDisposable
{
    const string Carrete = "CameraRollDomain";
    const string WhatsApp = "AppDomainGroup-group.net.whatsapp.WhatsApp.shared";

    readonly CopiaDePrueba copia = new();
    readonly CarpetaTemporal salida = new();

    public void Dispose()
    {
        copia.Dispose();
        salida.Dispose();
    }

    IReadOnlyList<ArchivoItunes> Listar() => CopiaItunes.Abrir(copia.Ruta).Listar(ContenidoItunes.Todo);

    [Fact]
    public void Copia_con_nombre_y_fecha_reales_sin_tocar_la_copia()
    {
        var modificado = new DateTime(2025, 9, 9, 16, 8, 0, DateTimeKind.Utc);
        var creado = new DateTime(2025, 9, 9, 16, 7, 0, DateTimeKind.Utc);
        var enCopia = copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC", "heic"u8.ToArray(), modificado, creado);

        var r = ExtractorItunes.Extraer(Listar(), salida.Ruta);

        var extraido = salida[@"DCIM\100APPLE\IMG_0001.HEIC"];
        Assert.Equal((1, 0, 4L, 0), (r.Copiados, r.YaEstaban, r.BytesCopiados, r.Errores.Count));
        Assert.Equal("heic", File.ReadAllText(extraido));
        Assert.Equal((modificado, creado), (File.GetLastWriteTimeUtc(extraido), File.GetCreationTimeUtc(extraido)));
        Assert.Equal("heic", File.ReadAllText(enCopia));
        Assert.Empty(Directory.EnumerateFiles(salida.Ruta, "*.parcial", SearchOption.AllDirectories));
    }

    [Fact]
    public void Repetir_sigue_donde_quedo()
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC");
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0002.HEIC");
        var archivos = Listar();
        ExtractorItunes.Extraer(archivos.Take(1).ToList(), salida.Ruta);

        Assert.Equal(archivos[1].Bytes, ExtractorItunes.BytesPendientes(archivos, salida.Ruta));
        var r = ExtractorItunes.Extraer(archivos, salida.Ruta);

        Assert.Equal((1, 1), (r.Copiados, r.YaEstaban));
        Assert.Equal(0, ExtractorItunes.BytesPendientes(archivos, salida.Ruta));
    }

    [Fact]
    public void Un_archivo_distinto_con_el_mismo_nombre_no_se_pisa_y_el_nuevo_va_con_2()
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC", "de la copia");
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001_2.HEIC", "otro de la copia");
        salida.Crear(@"DCIM\100APPLE\IMG_0001.HEIC", "otro archivo");
        var archivos = Listar();

        var r = ExtractorItunes.Extraer(archivos, salida.Ruta);

        Assert.Equal((2, 0), (r.Copiados, r.Errores.Count));
        Assert.Equal("otro archivo", File.ReadAllText(salida[@"DCIM\100APPLE\IMG_0001.HEIC"]));
        Assert.Equal("de la copia", File.ReadAllText(salida[@"DCIM\100APPLE\IMG_0001_2.HEIC"]));
        // El _2 de la copia no pisa al que acaba de quedar ahi
        Assert.Equal("otro de la copia", File.ReadAllText(salida[@"DCIM\100APPLE\IMG_0001_2_2.HEIC"]));
        Assert.Empty(Directory.EnumerateFiles(salida.Ruta, "*.parcial", SearchOption.AllDirectories));

        // Al repetir los encuentra donde quedaron
        Assert.Equal(0, ExtractorItunes.BytesPendientes(archivos, salida.Ruta));
        var repetida = ExtractorItunes.Extraer(archivos, salida.Ruta);
        Assert.Equal((0, 2), (repetida.Copiados, repetida.YaEstaban));
        Assert.Equal(3, Directory.EnumerateFiles(salida.Ruta, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public void Lo_pendiente_no_cuenta_lo_ya_copiado()
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC", "uno");
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0002.HEIC", "dos dos");
        var archivos = Listar();

        Assert.Equal(10, ExtractorItunes.BytesPendientes(archivos, salida["no existe todavia"]));
        ExtractorItunes.Extraer(archivos.Take(1).ToList(), salida.Ruta);
        Assert.Equal(7, ExtractorItunes.BytesPendientes(archivos, salida.Ruta));
    }

    [Fact]
    public void Sin_archivos_igual_crea_la_carpeta()
    {
        var destino = salida["vacia"];

        var r = ExtractorItunes.Extraer([], destino);

        Assert.Equal((0, 0), (r.Copiados, r.YaEstaban));
        Assert.True(Directory.Exists(destino));
    }

    [Fact]
    public void Cancelar_deja_lo_copiado_completo()
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC");
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0002.HEIC");
        using var cancelar = new CancellationTokenSource();
        var progreso = new ProgresoInmediato(p => { if (p.Hechos == 1) cancelar.Cancel(); });

        Assert.Throws<OperationCanceledException>(() => ExtractorItunes.Extraer(Listar(), salida.Ruta, progreso, cancelar.Token));

        Assert.Equal(["IMG_0001.HEIC"], Directory.EnumerateFiles(salida.Ruta, "*", SearchOption.AllDirectories).Select(Path.GetFileName));
    }

    [Fact]
    public void Lo_extraido_se_analiza_como_cualquier_backup()
    {
        var hace = new DateTime(2025, 9, 9, 16, 8, 0, DateTimeKind.Utc);
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC", ArchivosDePrueba.Jpeg(marca: "Apple", original: "2025:09:09 13:08:00"), hace);
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.MOV", ArchivosDePrueba.Mov(hace), hace);
        // 25 fotos de WhatsApp sin EXIF (distintas, para que no sean duplicados): con la fecha de hoy serian
        // "dudosas"; con la del iPhone van a su mes
        for (var i = 0; i < 25; i++)
            copia.Agregar(WhatsApp, $"Message/Media/x@s.whatsapp.net/0/a/{i:D2}.jpg", [.. ArchivosDePrueba.JpegSinExif(), (byte)i], hace.AddDays(-i));
        var origen = salida["iPhone"];
        using var organizado = new CarpetaTemporal();
        ExtractorItunes.Extraer(Listar(), origen);

        var plan = Analizador.Analizar(new OpcionesAnalisis(origen, organizado.Ruta, Ahora: new DateTime(2026, 10, 18))).Filas;

        Assert.Equal(CategoriaPlan.LivePhotos, plan.Single(f => f.Origen.EndsWith("IMG_0001.MOV")).Categoria);
        var whatsapp = plan.Where(f => f.Categoria == CategoriaPlan.WhatsApp).ToList();
        Assert.Equal(25, whatsapp.Count);
        Assert.All(whatsapp, f => Assert.Equal(FuenteFecha.Archivo, f.FuenteFecha));
        Assert.Equal(hace.ToLocalTime().Date, whatsapp.Single(f => f.Origen.EndsWith("00.jpg")).Fecha!.Value.Date);
    }

    // Progress<T> informa en otro hilo; para cancelar en un punto exacto hace falta que sea en el momento
    sealed class ProgresoInmediato(Action<(int Hechos, int Total)> accion) : IProgress<(int Hechos, int Total)>
    {
        public void Report((int Hechos, int Total) valor) => accion(valor);
    }
}
