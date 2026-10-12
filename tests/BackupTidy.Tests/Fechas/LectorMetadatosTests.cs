using BackupTidy.Core.Fechas;

namespace BackupTidy.Tests.Fechas;

public class LectorMetadatosTests
{
    static MetadatosMedia Leer(byte[] contenido) => LectorMetadatos.Leer(new MemoryStream(contenido));

    [Fact]
    public void Jpeg_lee_fechas_exif_en_orden_de_prioridad_y_la_marca()
    {
        var m = Leer(ArchivosDePrueba.Jpeg(marca: "Apple", original: "2019:03:15 12:12:00",
                                           digitalizada: "2019:03:15 12:12:01", fechaHora: "2020:01:01 08:00:00"));

        Assert.Equal([new DateTime(2019, 3, 15, 12, 12, 0), new DateTime(2019, 3, 15, 12, 12, 1), new DateTime(2020, 1, 1, 8, 0, 0)],
                     m.FechasExif);
        Assert.Empty(m.FechasContenedor);
        Assert.Equal("Apple", m.MarcaCamara);
        Assert.True(m.TieneMarcaCamara);
    }

    [Fact]
    public void Jpeg_sin_marca_de_camara()
    {
        var m = Leer(ArchivosDePrueba.Jpeg(fechaHora: "2020:01:01 08:00:00"));

        Assert.Equal([new DateTime(2020, 1, 1, 8, 0, 0)], m.FechasExif);
        Assert.False(m.TieneMarcaCamara);
    }

    [Fact]
    public void Jpeg_descarta_fechas_exif_vacias()
    {
        var m = Leer(ArchivosDePrueba.Jpeg(marca: "samsung", original: "0000:00:00 00:00:00", fechaHora: "2020:01:01 08:00:00"));

        Assert.Equal([new DateTime(2020, 1, 1, 8, 0, 0)], m.FechasExif);
        Assert.Equal("samsung", m.MarcaCamara);
    }

    [Fact]
    public void Jpeg_sin_exif() => Assert.Equal(MetadatosMediaVacio(), Resumen(Leer(ArchivosDePrueba.JpegSinExif())));

    [Fact]
    public void Video_convierte_la_fecha_de_la_cabecera_de_utc_a_hora_local()
    {
        var utc = new DateTime(2019, 3, 15, 15, 12, 0, DateTimeKind.Utc);
        var m = Leer(ArchivosDePrueba.Mov(utc));

        Assert.Empty(m.FechasExif);
        Assert.Equal([utc.ToLocalTime()], m.FechasContenedor);
    }

    [Fact]
    public void Video_prefiere_la_fecha_de_apple()
    {
        var m = Leer(ArchivosDePrueba.Mov(new DateTime(2019, 3, 15, 15, 12, 30, DateTimeKind.Utc), "2019-03-15T12:12:00-0300"));

        Assert.Equal(2, m.FechasContenedor.Count);
        Assert.Equal(DateTimeOffset.Parse("2019-03-15T12:12:00-03:00").LocalDateTime, m.FechasContenedor[0]);
    }

    [Fact]
    public void Detecta_el_formato_por_el_contenido()
    {
        Assert.Equal(".jpg", Leer(ArchivosDePrueba.Jpeg(marca: "Apple")).ExtensionContenido);
        Assert.Equal(".jpg", Leer(ArchivosDePrueba.JpegSinExif()).ExtensionContenido);
        Assert.Equal(".mov", Leer(ArchivosDePrueba.Mov(new DateTime(2019, 3, 15))).ExtensionContenido);
        Assert.Null(Leer("no es una imagen"u8.ToArray()).ExtensionContenido);
    }

    [Fact]
    public void Archivo_que_no_es_imagen_ni_video() =>
        Assert.Equal(MetadatosMediaVacio(), Resumen(Leer("no es una imagen"u8.ToArray())));

    [Fact]
    public void Archivo_inexistente() =>
        Assert.Equal(MetadatosMediaVacio(), Resumen(LectorMetadatos.Leer(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg"))));

    [Fact]
    public void Desde_disco()
    {
        var ruta = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        File.WriteAllBytes(ruta, ArchivosDePrueba.Jpeg(marca: "Apple", original: "2019:03:15 12:12:00"));
        try
        {
            var m = LectorMetadatos.Leer(ruta);
            Assert.Equal([new DateTime(2019, 3, 15, 12, 12, 0)], m.FechasExif);
            Assert.Equal("Apple", m.MarcaCamara);
        }
        finally
        {
            File.Delete(ruta);
        }
    }

    static (int, int, string?) Resumen(MetadatosMedia m) => (m.FechasExif.Count, m.FechasContenedor.Count, m.MarcaCamara);
    static (int, int, string?) MetadatosMediaVacio() => (0, 0, null);
}
