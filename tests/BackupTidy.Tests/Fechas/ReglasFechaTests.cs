using BackupTidy.Core.Fechas;

namespace BackupTidy.Tests.Fechas;

public class ReglasFechaTests
{
    static readonly DateTime Ahora = new(2026, 10, 12, 15, 0, 0);
    static readonly DateTime Modificacion = new(2026, 9, 29, 10, 0, 0);

    [Theory]
    [InlineData("IMG-20190315-WA0001.jpg", "2019-03-15")]
    [InlineData("Screenshot_2019-03-15-10-20-30.png", "2019-03-15")]
    [InlineData("20190315_123456.jpg", "2019-03-15")]
    [InlineData("VID_20190315.mp4", "2019-03-15")]
    [InlineData("foto 2019.03.15.jpg", "2019-03-15")]
    // 31 de febrero no existe: se usa la siguiente fecha del nombre
    [InlineData("IMG_20190231_20190301.jpg", "2019-03-01")]
    [InlineData("IMG_20190231.jpg", null)]
    [InlineData("IMG_20191315.jpg", null)]
    [InlineData("1234520190315.jpg", null)]
    [InlineData("IMG_1234.JPG", null)]
    public void FechaDeNombre(string nombre, string? esperado) =>
        Assert.Equal(esperado is null ? null : DateTime.Parse(esperado), ReglasFecha.FechaDeNombre(nombre));

    [Theory]
    [InlineData("2019:03:15 12:12:00", "2019-03-15 12:12:00")]
    [InlineData("2019:03:15 12:12:00\0", "2019-03-15 12:12:00")]
    [InlineData(" 2019-03-15 12:12:00 ", "2019-03-15 12:12:00")]
    [InlineData("2019:03:15 12:12", "2019-03-15 12:12:00")]
    [InlineData("0000:00:00 00:00:00", null)]
    [InlineData("    :  :     :  :  ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParsearExif(string? texto, string? esperado) =>
        Assert.Equal(esperado is null ? null : DateTime.Parse(esperado), ReglasFecha.ParsearExif(texto));

    [Theory]
    [InlineData("2016-11-17T13:49:09+01:00", "2016-11-17 13:49:09")]
    [InlineData("2021-12-07T12:37:32-08:00", "2021-12-07 12:37:32")]
    [InlineData("2017-11-04T02:49:44Z", "2017-11-04 02:49:44")]
    [InlineData("2016-11-17T13:49:09", "2016-11-17 13:49:09")]
    [InlineData("2016-11-17", "2016-11-17 00:00:00")]
    [InlineData("no es fecha", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParsearXmp(string? texto, string? esperado) =>
        Assert.Equal(esperado is null ? null : DateTime.Parse(esperado), ReglasFecha.ParsearXmp(texto));

    [Fact]
    public void EsValida_acepta_desde_2000_hasta_manana()
    {
        Assert.True(ReglasFecha.EsValida(new DateTime(2000, 1, 1), Ahora));
        Assert.False(ReglasFecha.EsValida(new DateTime(1999, 12, 31, 23, 59, 59), Ahora));
        Assert.True(ReglasFecha.EsValida(Ahora.AddDays(1), Ahora));
        Assert.False(ReglasFecha.EsValida(Ahora.AddDays(1).AddSeconds(1), Ahora));
    }

    [Fact]
    public void Resolver_prefiere_exif()
    {
        var exif = new DateTime(2019, 3, 15, 12, 12, 0);
        var m = new MetadatosMedia([exif], [new DateTime(2020, 1, 1)], "Apple");
        Assert.Equal(new FechaArchivo(exif, FuenteFecha.Exif), Resolver(m, "IMG-20210101-WA0001.jpg"));
    }

    [Fact]
    public void Resolver_salta_fechas_exif_fuera_de_rango()
    {
        var buena = new DateTime(2019, 3, 15, 12, 12, 0);
        var m = new MetadatosMedia([new DateTime(1980, 1, 1), Ahora.AddDays(30), buena], [], null);
        Assert.Equal(new FechaArchivo(buena, FuenteFecha.Exif), Resolver(m, "IMG_1.jpg"));
    }

    [Fact]
    public void Resolver_usa_metadatos_del_contenedor_si_no_hay_exif()
    {
        var video = new DateTime(2019, 3, 15, 12, 12, 0);
        var m = new MetadatosMedia([], [new DateTime(1904, 1, 1), video], null);
        Assert.Equal(new FechaArchivo(video, FuenteFecha.Metadatos), Resolver(m, "VID_20210101.mp4"));
    }

    [Fact]
    public void Resolver_usa_el_nombre_si_no_hay_metadatos() =>
        Assert.Equal(new FechaArchivo(new DateTime(2019, 3, 15), FuenteFecha.Nombre),
                     Resolver(MetadatosMedia.Vacio, "IMG-20190315-WA0001.jpg"));

    [Fact]
    public void Resolver_ignora_fechas_futuras_en_el_nombre() =>
        Assert.Equal(new FechaArchivo(Modificacion, FuenteFecha.Archivo),
                     Resolver(MetadatosMedia.Vacio, "IMG_20270101_000000.jpg"));

    [Theory]
    [InlineData(false, FuenteFecha.Archivo)]
    [InlineData(true, FuenteFecha.ArchivoDudoso)]
    public void Resolver_usa_la_fecha_de_modificacion_como_ultimo_recurso(bool carpetaDudosa, FuenteFecha fuente) =>
        Assert.Equal(new FechaArchivo(Modificacion, fuente),
                     ReglasFecha.Resolver(MetadatosMedia.Vacio, "IMG_1.jpg", Modificacion, carpetaDudosa, Ahora));

    [Fact]
    public void Resolver_usa_la_fecha_de_modificacion_aunque_este_fuera_de_rango()
    {
        var vieja = new DateTime(1980, 1, 1);
        Assert.Equal(new FechaArchivo(vieja, FuenteFecha.Archivo),
                     ReglasFecha.Resolver(MetadatosMedia.Vacio, "IMG_1.jpg", vieja, carpetaDudosa: false, Ahora));
    }

    [Theory]
    [InlineData(20, 10, true)]    // 50 % el mismo dia
    [InlineData(20, 9, false)]
    [InlineData(19, 19, false)]   // pocos archivos: no se puede saber
    [InlineData(100, 100, true)]
    public void CarpetasConFechaDudosa(int total, int mismoDia, bool dudosa)
    {
        var restore = new DateTime(2026, 9, 29);
        var archivos = Enumerable.Range(0, total).Select(i =>
            (Carpeta: i % 2 == 0 ? @"iPhone\DCIM" : @"IPHONE\dcim",   // mayusculas distintas = misma carpeta
             FechaModificacion: i < mismoDia ? restore.AddHours(i % 24) : new DateTime(2018, 1, 1).AddDays(i)));

        var dudosas = ReglasFecha.CarpetasConFechaDudosa(archivos);

        Assert.Equal(dudosa, dudosas.Contains(@"iPhone\DCIM"));
    }

    [Fact]
    public void CarpetasConFechaDudosa_evalua_cada_carpeta_por_separado()
    {
        var restore = new DateTime(2026, 9, 29);
        var archivos = Enumerable.Range(0, 20).Select(_ => (@"a\restaurada", restore))
            .Concat(Enumerable.Range(0, 20).Select(i => (@"a\normal", new DateTime(2018, 1, 1).AddDays(i))));

        Assert.Equal([@"a\restaurada"], ReglasFecha.CarpetasConFechaDudosa(archivos));
    }

    static FechaArchivo Resolver(MetadatosMedia m, string nombre) =>
        ReglasFecha.Resolver(m, nombre, Modificacion, carpetaDudosa: false, Ahora);
}
