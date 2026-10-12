using BackupTidy.Core.Fechas;
using BackupTidy.Core.Plan;

namespace BackupTidy.Tests.Plan;

public sealed class AnalizadorTests : IDisposable
{
    static readonly DateTime Ahora = new(2026, 10, 12, 15, 0, 0);
    static readonly DateTime Restore = new(2026, 9, 29, 10, 0, 0);

    readonly CarpetaTemporal origen = new();
    readonly CarpetaTemporal destino = new();

    public void Dispose()
    {
        origen.Dispose();
        destino.Dispose();
    }

    static byte[] Foto(string fecha, string marca = "Apple") => ArchivosDePrueba.Jpeg(marca: marca, original: fecha);

    List<FilaPlan> Analizar(bool compararConDestino = false) =>
        Analizador.Analizar(new OpcionesAnalisis(origen.Ruta, destino.Ruta, compararConDestino ? [destino.Ruta] : null, Ahora))
                  .Filas.ToList();

    FilaPlan Fila(List<FilaPlan> plan, string relativa) =>
        plan.Single(f => f.Origen.Equals(origen[relativa], StringComparison.OrdinalIgnoreCase));

    string Destino(params string[] partes) => Path.Combine([destino.Ruta, .. partes]);

    [Fact]
    public void Foto_con_exif_va_a_su_mes()
    {
        origen.Crear(@"iPhone\DCIM\IMG_0001.JPG", Foto("2019:03:15 12:12:00"));

        var f = Fila(Analizar(), @"iPhone\DCIM\IMG_0001.JPG");

        Assert.Equal(CategoriaPlan.Fotos, f.Categoria);
        Assert.Equal(Destino("Fotos", "2019", "2019-03", "IMG_0001.JPG"), f.Destino);
        Assert.Equal((new DateTime(2019, 3, 15, 12, 12, 0), FuenteFecha.Exif), (f.Fecha!.Value, f.FuenteFecha!.Value));
        Assert.Equal("", f.Nota);
    }

    [Fact]
    public void Duplicado_exacto_dentro_del_origen()
    {
        var foto = Foto("2019:03:15 12:12:00");
        origen.Crear(@"iPhone\DCIM\IMG_0001.JPG", foto);
        origen.Crear(@"iPhone2\DCIM\IMG_0001.JPG", foto);

        var plan = Analizar();
        var dup = Fila(plan, @"iPhone2\DCIM\IMG_0001.JPG");

        Assert.Equal(CategoriaPlan.Fotos, Fila(plan, @"iPhone\DCIM\IMG_0001.JPG").Categoria);
        Assert.Equal(CategoriaPlan.Duplicados, dup.Categoria);
        Assert.Equal(Destino("_duplicados", "iPhone2", "DCIM", "IMG_0001.JPG"), dup.Destino);
        Assert.Equal(@"Igual a: iPhone\DCIM\IMG_0001.JPG", dup.Nota);
        Assert.NotNull(dup.Hash);
    }

    [Fact]
    public void Vista_previa_con_el_original_al_lado()
    {
        origen.Crear(@"DCIM\IMG_0002.HEIC", Foto("2019:03:15 12:12:00"));
        origen.Crear(@"DCIM\IMG_0002.HEIC.jpg", Foto("2019:03:15 12:12:01"));
        origen.Crear(@"DCIM\IMG_0003.HEIC.jpg", Foto("2019:03:15 12:12:02"));   // sin original: es una foto mas

        var plan = Analizar();

        Assert.Equal(Destino("_duplicados_conversiones", "DCIM", "IMG_0002.HEIC.jpg"), Fila(plan, @"DCIM\IMG_0002.HEIC.jpg").Destino);
        Assert.Equal(CategoriaPlan.Fotos, Fila(plan, @"DCIM\IMG_0003.HEIC.jpg").Categoria);
    }

    [Fact]
    public void Live_photo_y_jpeg_con_extension_de_video()
    {
        origen.Crear(@"iPhone\DCIM\IMG_0003.JPG", Foto("2019:03:15 12:12:00"));
        origen.Crear(@"iPhone\DCIM\IMG_0003.MOV", ArchivosDePrueba.Mov(new DateTime(2019, 3, 15, 15, 12, 0, DateTimeKind.Utc)));
        origen.Crear(@"iPhone\DCIM\IMG_8979.MOV", Foto("2024:03:07 16:20:52"));

        var plan = Analizar();
        var live = Fila(plan, @"iPhone\DCIM\IMG_0003.MOV");
        var jpegMov = Fila(plan, @"iPhone\DCIM\IMG_8979.MOV");

        Assert.Equal((CategoriaPlan.LivePhotos, FuenteFecha.Metadatos), (live.Categoria, live.FuenteFecha!.Value));
        Assert.Equal(CategoriaPlan.Fotos, jpegMov.Categoria);
        Assert.Equal(Destino("Fotos", "2024", "2024-03", "IMG_8979.MOV"), jpegMov.Destino);
        Assert.Equal("Es JPG con extension .MOV", jpegMov.Nota);
    }

    [Fact]
    public void Whatsapp_sin_exif_usa_la_fecha_del_nombre()
    {
        origen.Crear(@"Android\WhatsApp Images\IMG-20190315-WA0001.jpg", ArchivosDePrueba.JpegSinExif());

        var f = Fila(Analizar(), @"Android\WhatsApp Images\IMG-20190315-WA0001.jpg");

        Assert.Equal((CategoriaPlan.WhatsApp, FuenteFecha.Nombre), (f.Categoria, f.FuenteFecha!.Value));
        Assert.Equal(Destino("WhatsApp", "2019", "2019-03", "IMG-20190315-WA0001.jpg"), f.Destino);
    }

    [Fact]
    public void Whatsapp_attachments_usa_dia_mes_anio_del_nombre_aunque_la_carpeta_sea_dudosa()
    {
        // Caso real del iPhone 14 Pro: todo con la fecha de la exportacion
        for (var i = 0; i < 20; i++)
            origen.Crear($@"Backup\WhatsApp Attachments\{i + 1}_9_2025 18_06.jpg", [0xFF, 0xD8, 0xFF, 0xD9, (byte)i], Restore);
        origen.Crear(@"Backup\Otros\18_9_2025 18_06.jpg", [0xFF, 0xD8, 0xFF, 0xD9, 0xAA], Restore);

        var plan = Analizar();
        var whatsapp = Fila(plan, @"Backup\WhatsApp Attachments\18_9_2025 18_06.jpg");
        var otro = Fila(plan, @"Backup\Otros\18_9_2025 18_06.jpg");

        Assert.Equal((new DateTime(2025, 9, 18, 18, 6, 0), FuenteFecha.Nombre), (whatsapp.Fecha!.Value, whatsapp.FuenteFecha!.Value));
        Assert.Equal(Destino("WhatsApp", "2025", "2025-09", "18_9_2025 18_06.jpg"), whatsapp.Destino);
        Assert.Equal(FuenteFecha.Archivo, otro.FuenteFecha);   // fuera de WhatsApp no se interpreta
    }

    [Fact]
    public void Carpeta_restaurada_va_a_fecha_dudosa()
    {
        for (var i = 0; i < 20; i++)
            origen.Crear($@"restaurado\IMG_{i:0000}.jpg", [0xFF, 0xD8, 0xFF, 0xD9, (byte)i], Restore);

        var f = Fila(Analizar(), @"restaurado\IMG_0000.jpg");

        Assert.Equal(FuenteFecha.ArchivoDudoso, f.FuenteFecha);
        Assert.Equal(Destino("Guardadas", "_fecha_dudosa", "2026", "2026-09", "IMG_0000.jpg"), f.Destino);
    }

    [Fact]
    public void Documentos_y_descartes()
    {
        origen.Crear(@"docs\informe.pdf", "%PDF contenido");
        origen.Crear(@"docs\copia\informe.pdf", "%PDF contenido");
        origen.Crear(@"docs\planilla.xlsx", "xlsx");
        origen.Crear(@"audios\nota.opus", "opus");

        var plan = Analizar();

        Assert.Equal(Destino("Documentos", "Office", "planilla.xlsx"), Fila(plan, @"docs\planilla.xlsx").Destino);
        var pdfs = new[] { Fila(plan, @"docs\informe.pdf"), Fila(plan, @"docs\copia\informe.pdf") };
        Assert.Contains(pdfs, f => f.Categoria == CategoriaPlan.Documentos && f.Destino == Destino("Documentos", "PDF", "informe.pdf"));
        Assert.Contains(pdfs, f => f.Categoria == CategoriaPlan.DocumentosDuplicados && f.Destino == Destino("Documentos", "_duplicados", "informe.pdf"));
        var opus = Fila(plan, @"audios\nota.opus");
        Assert.Equal((CategoriaPlan.Descartar, null), (opus.Categoria, opus.Destino));
    }

    [Fact]
    public void Nunca_repite_un_destino_ni_pisa_lo_existente()
    {
        destino.Crear(@"Fotos\2019\2019-03\IMG_0001.JPG", "ya organizado");
        origen.Crear(@"a\IMG_0001.JPG", Foto("2019:03:15 12:12:00"));
        origen.Crear(@"b\IMG_0001.JPG", Foto("2019:03:20 08:00:00"));

        var destinos = Analizar().Select(f => Path.GetFileName(f.Destino)).Order().ToList();

        Assert.Equal(["IMG_0001_2.JPG", "IMG_0001_3.JPG"], destinos);
    }

    [Fact]
    public void Lo_que_ya_esta_organizado_va_a_duplicados()
    {
        var foto = Foto("2019:03:15 12:12:00");
        var organizado = destino.Crear(@"Fotos\2019\2019-03\IMG_0001.JPG", foto);
        origen.Crear(@"iPhone\IMG_0001.JPG", foto);

        var f = Fila(Analizar(compararConDestino: true), @"iPhone\IMG_0001.JPG");

        Assert.Equal(CategoriaPlan.Duplicados, f.Categoria);
        Assert.Equal("Ya existe en: " + organizado, f.Nota);
    }

    [Fact]
    public void Heic_nuevo_reemplaza_al_jpg_convertido_organizado()
    {
        var jpg = destino.Crear(@"Fotos\2019\2019-03\IMG_0005.jpg", Foto("2019:03:15 12:12:00"));
        origen.Crear(@"iPhone\IMG_0005.HEIC", Foto("2019:03:15 12:12:09"));

        var plan = Analizar(compararConDestino: true);
        var heic = Fila(plan, @"iPhone\IMG_0005.HEIC");
        var reemplazado = plan.Single(f => f.Categoria == CategoriaPlan.JpgReemplazado);

        Assert.Equal((CategoriaPlan.Fotos, Destino("Fotos", "2019", "2019-03", "IMG_0005.HEIC")), (heic.Categoria, heic.Destino));
        Assert.Equal("Reemplaza JPG convertido: " + jpg, heic.Nota);
        Assert.Equal((jpg, Destino("_jpg_reemplazados", "Fotos", "2019", "2019-03", "IMG_0005.jpg")), (reemplazado.Origen, reemplazado.Destino));
    }

    [Fact]
    public void Mismo_nombre_y_mes_que_algo_organizado_es_probable()
    {
        var heic = destino.Crear(@"Fotos\2019\2019-03\IMG_0006.HEIC", Foto("2019:03:15 12:12:00"));
        origen.Crear(@"iPhone\IMG_0006.JPG", Foto("2019:03:15 12:12:07"));

        var f = Fila(Analizar(compararConDestino: true), @"iPhone\IMG_0006.JPG");

        Assert.Equal((CategoriaPlan.Duplicados, Destino("_duplicados_probables", "iPhone", "IMG_0006.JPG")), (f.Categoria, f.Destino));
        Assert.Equal("Mismo nombre y mes que: " + heic, f.Nota);
    }

    [Fact]
    public void No_modifica_ningun_archivo()
    {
        var ruta = origen.Crear(@"iPhone\IMG_0001.JPG", Foto("2019:03:15 12:12:00"), Restore);
        origen.Crear(@"iPhone\nota.opus", "opus");

        Analizar();

        Assert.Equal(Restore, File.GetLastWriteTime(ruta));
        Assert.Equal(2, Directory.GetFiles(origen.Ruta, "*", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.GetFileSystemEntries(destino.Ruta));
    }

    [Fact]
    public void Destino_dentro_del_origen_no_se_permite() =>
        Assert.Throws<ArgumentException>(() =>
            Analizador.Analizar(new OpcionesAnalisis(origen.Ruta, origen["organizado"])));

    [Fact]
    public void Avisa_si_la_carpeta_para_comparar_no_existe()
    {
        var r = Analizador.Analizar(new OpcionesAnalisis(origen.Ruta, destino.Ruta, [destino["no-existe"]], Ahora));

        Assert.Single(r.Avisos);
    }

    [Fact]
    public void Resumen_por_categoria()
    {
        origen.Crear(@"a\IMG_0001.JPG", Foto("2019:03:15 12:12:00"));
        origen.Crear(@"a\IMG_0002.JPG", Foto("2019:03:16 12:12:00"));
        origen.Crear(@"a\nota.opus", "opus");

        var resumen = Analizador.Resumen(Analizar());

        Assert.Equal(2, resumen.Single(r => r.Categoria == CategoriaPlan.Fotos).Archivos);
        Assert.Equal(4, resumen.Single(r => r.Categoria == CategoriaPlan.Descartar).Bytes);
    }
}
