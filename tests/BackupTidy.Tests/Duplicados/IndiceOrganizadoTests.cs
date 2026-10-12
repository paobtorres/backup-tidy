using BackupTidy.Core.Duplicados;

namespace BackupTidy.Tests.Duplicados;

public class IndiceOrganizadoTests
{
    const string Organizado = @"D:\organizado";
    static readonly DateTime Marzo = new(2019, 3, 15, 12, 0, 0);

    static IndiceOrganizado Indice(params string[] relativas) =>
        new(relativas.Select(r => new ArchivoComparable(Path.Combine(Organizado, r), r, 1, EsReferencia: true)));

    [Theory]
    [InlineData("IMG_1234.HEIC.jpg", "img_1234")]
    [InlineData("IMG_1234_2.JPG", "img_1234")]
    [InlineData("IMG_1234.HEIC", "img_1234")]
    [InlineData("IMG_1234_10.jpg", "img_1234_10")]   // solo un digito de sufijo, como el script
    [InlineData(".oculto", ".oculto")]
    public void NombreBase(string nombre, string esperado) => Assert.Equal(esperado, IndiceOrganizado.NombreBase(nombre));

    [Fact]
    public void Heic_nuevo_reemplaza_al_jpg_convertido_del_mismo_mes()
    {
        var r = Indice(@"Fotos\2019\2019-03\IMG_1234.jpg", @"Fotos\2019\2019-03\IMG_1234_2.JPG").Evaluar("IMG_1234.HEIC", ".HEIC", Marzo);

        Assert.Equal(AccionMismaFoto.ReemplazaJpg, r.Accion);
        Assert.Equal([Path.Combine(Organizado, @"Fotos\2019\2019-03\IMG_1234.jpg"), Path.Combine(Organizado, @"Fotos\2019\2019-03\IMG_1234_2.JPG")],
                     r.Organizados);
    }

    [Fact]
    public void Jpg_nuevo_con_heic_organizado_es_probable_duplicado()
    {
        var r = Indice(@"Fotos\2019\2019-03\IMG_1234.HEIC").Evaluar("IMG_1234.HEIC.jpg", ".jpg", Marzo);

        Assert.Equal(new ResultadoMismaFoto(AccionMismaFoto.Probable, r.Organizados), r);
        Assert.Equal(Path.Combine(Organizado, @"Fotos\2019\2019-03\IMG_1234.HEIC"), r.Organizados.Single());
    }

    [Fact]
    public void Heic_nuevo_con_un_video_del_mismo_nombre_es_probable_no_reemplazo()
    {
        var r = Indice(@"LivePhotos\2019\2019-03\IMG_1234.MOV").Evaluar("IMG_1234.HEIC", ".heic", Marzo);

        Assert.Equal(AccionMismaFoto.Probable, r.Accion);
    }

    [Fact]
    public void Otro_mes_no_coincide() =>
        Assert.Same(ResultadoMismaFoto.Ninguna, Indice(@"Fotos\2019\2019-04\IMG_1234.jpg").Evaluar("IMG_1234.HEIC", ".heic", Marzo));

    [Theory]
    [InlineData(@"Fotos\_fecha_dudosa\2019\2019-03\IMG_1234.jpg")]
    [InlineData(@"_duplicados\iPhone\IMG_1234.jpg")]
    [InlineData(@"Capturas\2019\2019-03\IMG_1234.jpg")]
    [InlineData(@"Fotos\2019-03\IMG_1234.jpg")]
    public void Solo_se_indexa_lo_organizado_con_fecha_fiable(string relativa) =>
        Assert.Same(ResultadoMismaFoto.Ninguna, Indice(relativa).Evaluar("IMG_1234.HEIC", ".heic", Marzo));
}
