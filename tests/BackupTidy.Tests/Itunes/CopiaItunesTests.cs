using BackupTidy.Core.Itunes;

namespace BackupTidy.Tests.Itunes;

public sealed class CopiaItunesTests : IDisposable
{
    const string Carrete = "CameraRollDomain";
    const string WhatsApp = "AppDomainGroup-group.net.whatsapp.WhatsApp.shared";

    readonly CopiaDePrueba copia = new("iPhone de Paola");

    public void Dispose() => copia.Dispose();

    [Theory]
    [InlineData(Carrete, "Media/DCIM/106APPLE/IMG_6671.HEIC", ContenidoItunes.Carrete, @"DCIM\106APPLE\IMG_6671.HEIC")]
    [InlineData(Carrete, "Media/DCIM/106APPLE/IMG_6671.MOV", ContenidoItunes.Carrete, @"DCIM\106APPLE\IMG_6671.MOV")]
    [InlineData(Carrete, "Media/PhotoData/Mutations/DCIM/106APPLE/IMG_6352/Adjustments/FullSizeRender.heic",
                ContenidoItunes.Editadas, @"DCIM\106APPLE\IMG_6352_editada.heic")]
    [InlineData(Carrete, "Media/PhotoData/Mutations/DCIM/106APPLE/IMG_6352/Adjustments/FullSizeRender.mov",
                ContenidoItunes.Editadas, @"DCIM\106APPLE\IMG_6352_editada.mov")]
    [InlineData(WhatsApp, "Message/Media/5491100000000@s.whatsapp.net/0/a/0a44eef2.jpg", ContenidoItunes.WhatsApp, @"WhatsApp\0a44eef2.jpg")]
    [InlineData("AppDomainGroup-group.net.whatsapp.WhatsAppSMB.shared", "Message/Media/x@g.us/1/2/v.mp4",
                ContenidoItunes.WhatsApp, @"WhatsApp Business\v.mp4")]
    public void Ruta_de_salida_con_el_nombre_real(string dominio, string ruta, ContenidoItunes contenido, string salida)
    {
        Assert.Equal((contenido, salida), CopiaItunes.RutaDeSalida(dominio, ruta));
    }

    [Theory]
    [InlineData(Carrete, "Media/DCIM/106APPLE/IMG_6671.AAE")]                                        // ajustes de edicion
    [InlineData(Carrete, "Media/PhotoData/Thumbnails/V2/DCIM/100APPLE/IMG_0004.MOV/5005.JPG")]       // miniatura
    [InlineData(Carrete, "Media/PhotoData/Mutations/DCIM/100APPLE/IMG_0004/Adjustments/IMG_0004.JPG")]
    [InlineData(Carrete, "Media/PhotoData/UBF/resources/derivatives/cvt/5/x.jpg")]
    [InlineData(WhatsApp, "Message/Media/x@s.whatsapp.net/0/a/sticker.webp")]
    [InlineData(WhatsApp, "Message/Media/x@s.whatsapp.net/0/a/nota.opus")]
    [InlineData(WhatsApp, "Message/Media/x@s.whatsapp.net/0/a/foto.jpg.thumb")]
    [InlineData(WhatsApp, "Library/WallpaperV3/light/fondo.jpg")]
    [InlineData(WhatsApp, "Media/Profile/contacto.jpg")]
    [InlineData("AppDomain-com.burbn.instagram", "Documents/foto.jpg")]
    public void Lo_que_no_se_extrae(string dominio, string ruta)
    {
        Assert.Null(CopiaItunes.RutaDeSalida(dominio, ruta));
    }

    [Fact]
    public void Abrir_lee_el_dispositivo()
    {
        var c = CopiaItunes.Abrir(copia.Ruta);

        Assert.Equal("iPhone de Paola", c.Dispositivo);
        Assert.Equal("26.5.2", c.VersionIos);
        Assert.Equal(new DateTime(2026, 10, 12, 14, 10, 0, DateTimeKind.Utc), c.Fecha);
        Assert.False(c.Encriptada);
        Assert.StartsWith("iPhone de Paola 2026-10-12", c.NombreSugerido);
    }

    [Fact]
    public void Una_carpeta_que_no_es_copia()
    {
        using var otra = new CarpetaTemporal();
        Assert.Contains("falta Manifest.plist", Assert.Throws<ArgumentException>(() => CopiaItunes.Abrir(otra.Ruta)).Message);

        File.WriteAllBytes(otra["Manifest.plist"], EscritorPlist.Escribir(new Dictionary<string, object?>()));
        otra.Crear("Manifest.mbdb", "viejo");
        Assert.Contains("iOS 9 o anterior", Assert.Throws<ArgumentException>(() => CopiaItunes.Abrir(otra.Ruta)).Message);
    }

    [Fact]
    public void Lista_con_nombres_reales_y_fechas_del_iPhone()
    {
        var modificado = new DateTime(2025, 9, 9, 16, 8, 0, DateTimeKind.Utc);
        var foto = copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC", "heic", modificado);
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.AAE", "ajustes");
        copia.Agregar(Carrete, "Media/PhotoData/Thumbnails/V2/DCIM/100APPLE/IMG_0001.HEIC/5005.JPG", "mini");
        copia.Agregar(WhatsApp, "Message/Media/x@s.whatsapp.net/0/a/0a44.jpg", "wa");
        copia.Agregar("AppDomain-com.otra.app", "Documents/foto.jpg", "app");
        copia.AgregarCarpeta(Carrete, "Media/DCIM/100APPLE");

        var archivos = CopiaItunes.Abrir(copia.Ruta).Listar(ContenidoItunes.Todo);

        Assert.Equal([@"DCIM\100APPLE\IMG_0001.HEIC", @"WhatsApp\0a44.jpg"], archivos.Select(a => a.Destino).Order());
        var heic = archivos.Single(a => a.Contenido == ContenidoItunes.Carrete);
        Assert.Equal((foto, 4L, modificado), (heic.Origen, heic.Bytes, heic.Modificado));
    }

    [Fact]
    public void Solo_lo_pedido()
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC");
        copia.Agregar(Carrete, "Media/PhotoData/Mutations/DCIM/100APPLE/IMG_0001/Adjustments/FullSizeRender.heic");
        copia.Agregar(WhatsApp, "Message/Media/x@s.whatsapp.net/0/a/0a44.jpg");

        var c = CopiaItunes.Abrir(copia.Ruta);

        Assert.Equal([ContenidoItunes.WhatsApp], c.Listar(ContenidoItunes.WhatsApp).Select(a => a.Contenido));
        Assert.Equal([ContenidoItunes.Carrete, ContenidoItunes.Editadas],
                     c.Listar(ContenidoItunes.Carrete | ContenidoItunes.Editadas).Select(a => a.Contenido).Order());
    }

    [Fact]
    public void La_imagen_de_un_video_editado_no_se_extrae()
    {
        // Video editado: Fotos guarda el video y un JPG con un cuadro, que no debe pasar por Live Photo
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0004.MOV");
        copia.Agregar(Carrete, "Media/PhotoData/Mutations/DCIM/100APPLE/IMG_0004/Adjustments/FullSizeRender.mov");
        copia.Agregar(Carrete, "Media/PhotoData/Mutations/DCIM/100APPLE/IMG_0004/Adjustments/FullSizeRender.jpg");
        // Live Photo editada: foto y video
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0010.HEIC");
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0010.MOV");
        copia.Agregar(Carrete, "Media/PhotoData/Mutations/DCIM/100APPLE/IMG_0010/Adjustments/FullSizeRender.heic");
        copia.Agregar(Carrete, "Media/PhotoData/Mutations/DCIM/100APPLE/IMG_0010/Adjustments/FullSizeRender.mov");

        // Aunque solo se pidan las editadas, el original del carrete decide
        var editadas = CopiaItunes.Abrir(copia.Ruta).Listar(ContenidoItunes.Editadas).Select(a => a.Destino).Order();

        Assert.Equal([@"DCIM\100APPLE\IMG_0004_editada.mov", @"DCIM\100APPLE\IMG_0010_editada.heic", @"DCIM\100APPLE\IMG_0010_editada.mov"],
                     editadas);
    }

    [Fact]
    public void Nombres_repetidos_no_se_pisan_y_lo_que_falta_en_disco_se_omite()
    {
        copia.Agregar(WhatsApp, "Message/Media/a@s.whatsapp.net/0/a/igual.jpg", "uno");
        copia.Agregar(WhatsApp, "Message/Media/b@s.whatsapp.net/0/a/igual.jpg", "dos");
        copia.Agregar(WhatsApp, "Message/Media/c@s.whatsapp.net/0/a/falta.jpg", [], enDisco: false);

        var archivos = CopiaItunes.Abrir(copia.Ruta).Listar(ContenidoItunes.Todo);

        Assert.Equal([@"WhatsApp\igual.jpg", @"WhatsApp\igual_2.jpg"], archivos.Select(a => a.Destino));
    }

    [Fact]
    public void Una_copia_que_iTunes_no_termino_no_se_lista()
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC");
        var c = CopiaItunes.Abrir(copia.Ruta);
        Assert.True(c.EstaTerminada());

        copia.Estado("uploading");

        Assert.False(c.EstaTerminada());
        Assert.Contains("no está terminada", Assert.Throws<IOException>(() => c.Listar(ContenidoItunes.Todo)).Message);
    }

    [Fact]
    public void Sin_Status_plist_se_toma_como_terminada()
    {
        File.Delete(Path.Combine(copia.Ruta, "Status.plist"));

        Assert.True(CopiaItunes.Abrir(copia.Ruta).EstaTerminada());
    }

    [Fact]
    public void Una_fecha_danada_se_pierde_sola()
    {
        copia.AgregarConMetadatos(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC", "a"u8.ToArray(),
                                  EscritorPlist.MBFile("Media/DCIM/100APPLE/IMG_0001.HEIC", 1, long.MaxValue, 1_700_000_000));
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0002.HEIC");

        var archivos = CopiaItunes.Abrir(copia.Ruta).Listar(ContenidoItunes.Todo);

        Assert.Equal(2, archivos.Count);
        var danada = archivos.Single(a => a.Destino.EndsWith("IMG_0001.HEIC"));
        Assert.Null(danada.Modificado);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(1_700_000_000), danada.Creado);
    }

    [Fact]
    public void Las_filas_con_tipos_inesperados_se_omiten()
    {
        copia.Agregar(Carrete, "Media/DCIM/100APPLE/IMG_0001.HEIC");
        copia.AgregarFila(12345L, Carrete, "Media/DCIM/100APPLE/IMG_0002.HEIC", null);
        copia.AgregarFila("ab", Carrete, "Media/DCIM/100APPLE/IMG_0003.HEIC", null);
        copia.AgregarFila(new string('a', 40), null, "Media/DCIM/100APPLE/IMG_0004.HEIC", null);
        copia.AgregarFila(new string('b', 40), Carrete, 7L, null);
        // Metadatos que no son blob: el archivo se lista, sin fecha
        var id = new string('c', 40);
        Directory.CreateDirectory(Path.Combine(copia.Ruta, "cc"));
        File.WriteAllText(Path.Combine(copia.Ruta, "cc", id), "x");
        copia.AgregarFila(id, Carrete, "Media/DCIM/100APPLE/IMG_0005.HEIC", "no es un plist");

        var archivos = CopiaItunes.Abrir(copia.Ruta).Listar(ContenidoItunes.Todo);

        Assert.Equal([@"DCIM\100APPLE\IMG_0001.HEIC", @"DCIM\100APPLE\IMG_0005.HEIC"], archivos.Select(a => a.Destino).Order());
        Assert.Null(archivos.Single(a => a.Destino.EndsWith("IMG_0005.HEIC")).Modificado);
    }

    [Theory]
    [InlineData(Carrete, "Media/DCIM/../IMG_0001.HEIC")]
    [InlineData(Carrete, @"Media/DCIM/100APPLE/..\..\IMG_0001.HEIC")]
    [InlineData(Carrete, "Media/DCIM/100APPLE/C:IMG_0001.HEIC")]
    [InlineData(Carrete, "Media/PhotoData/Mutations/DCIM/../IMG_0001/Adjustments/FullSizeRender.heic")]
    [InlineData(WhatsApp, @"Message/Media/x@s.whatsapp.net/0/a/..\..\foto.jpg")]
    public void Un_nombre_que_saldria_de_la_carpeta_no_se_extrae(string dominio, string ruta)
    {
        Assert.Null(CopiaItunes.RutaDeSalida(dominio, ruta));
    }

    [Fact]
    public void Una_copia_encriptada_no_se_lista()
    {
        using var encriptada = new CopiaDePrueba(encriptada: true);
        var c = CopiaItunes.Abrir(encriptada.Ruta);

        Assert.True(c.Encriptada);
        Assert.Contains("encriptada", Assert.Throws<ArgumentException>(() => c.Listar(ContenidoItunes.Todo)).Message);
    }

    [Fact]
    public void Buscar_encuentra_las_copias_y_omite_lo_demas()
    {
        using var ubicacion = new CarpetaTemporal();
        using var vieja = new CopiaDePrueba("iPhone viejo", fecha: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        foreach (var origen in new[] { copia.Ruta, vieja.Ruta })
            CopiarCarpeta(origen, ubicacion[Path.GetFileName(origen)]);
        ubicacion.Crear(@"no_es_copia\algo.txt", "x");

        var copias = CopiaItunes.Buscar([ubicacion.Ruta, ubicacion["no existe"]]);

        Assert.Equal(["iPhone de Paola", "iPhone viejo"], copias.Select(c => c.Dispositivo));
    }

    static void CopiarCarpeta(string origen, string destino)
    {
        foreach (var archivo in Directory.EnumerateFiles(origen, "*", SearchOption.AllDirectories))
        {
            var nuevo = Path.Combine(destino, Path.GetRelativePath(origen, archivo));
            Directory.CreateDirectory(Path.GetDirectoryName(nuevo)!);
            File.Copy(archivo, nuevo);
        }
    }
}
