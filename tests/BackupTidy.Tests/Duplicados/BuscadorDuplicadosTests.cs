using BackupTidy.Core.Duplicados;

namespace BackupTidy.Tests.Duplicados;

public class BuscadorDuplicadosTests
{
    const string Origen = @"D:\backup";
    const string Organizado = @"D:\organizado";

    // Contenido simulado: el hash es el propio texto; se cuentan las lecturas
    readonly Dictionary<string, string> contenidos = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> leidos = [];

    ArchivoComparable Nuevo(string rel, string contenido)
    {
        var ruta = Path.Combine(Origen, rel);
        contenidos[ruta] = contenido;
        return new(ruta, rel, contenido.Length);
    }

    ArchivoComparable Referencia(string rel, string contenido)
    {
        var ruta = Path.Combine(Organizado, rel);
        contenidos[ruta] = contenido;
        return new(ruta, rel, contenido.Length, EsReferencia: true);
    }

    ResultadoDuplicados Buscar(params ArchivoComparable[] archivos) =>
        BuscadorDuplicados.Buscar(archivos, ruta => { leidos.Add(ruta); return contenidos[ruta]; });

    [Fact]
    public void Tamanos_distintos_no_se_leen()
    {
        var r = Buscar(Nuevo("a.jpg", "AAAA"), Nuevo("b.jpg", "BBBBB"));

        Assert.Empty(r.Duplicados);
        Assert.Empty(leidos);
    }

    [Fact]
    public void Mismo_tamano_y_distinto_contenido_no_es_duplicado()
    {
        var r = Buscar(Nuevo("a.jpg", "AAAA"), Nuevo("b.jpg", "BBBB"));

        Assert.Empty(r.Duplicados);
        Assert.Equal(2, r.Hashes.Count);
    }

    [Fact]
    public void Se_conserva_el_nombre_mas_corto()
    {
        var r = Buscar(Nuevo(@"iPhone1\DCIM\IMG_0001 (1).jpg", "XXXX"),
                       Nuevo(@"iPhone2\DCIM\IMG_0001.jpg", "XXXX"),
                       Nuevo(@"iPhone3\DCIM\IMG_0001_copia.jpg", "XXXX"));

        Assert.Equal(2, r.Duplicados.Count);
        Assert.All(r.Duplicados.Values, d => Assert.Equal(new Duplicado(MotivoDuplicado.IgualA, @"iPhone2\DCIM\IMG_0001.jpg"), d));
        Assert.False(r.Duplicados.ContainsKey(Path.Combine(Origen, @"iPhone2\DCIM\IMG_0001.jpg")));
    }

    [Fact]
    public void Con_el_mismo_largo_de_nombre_gana_la_ruta_mas_corta_y_luego_el_orden_alfabetico()
    {
        var r = Buscar(Nuevo(@"b\x\IMG_1.jpg", "XXXX"), Nuevo(@"b\IMG_1.jpg", "XXXX"), Nuevo(@"a\IMG_1.jpg", "XXXX"));

        Assert.Equal(@"a\IMG_1.jpg", r.Duplicados.Values.Distinct().Single().Original);
    }

    [Fact]
    public void El_desempate_alfabetico_es_como_el_del_explorador()
    {
        // Caso real de WhatsApp Attachments: mismo largo y misma carpeta; '_' va antes que los digitos
        var r = Buscar(Nuevo(@"WhatsApp Attachments\17_9_2025 14_00.jpg", "XXXX"),
                       Nuevo(@"WhatsApp Attachments\1_10_2025 10_00.jpg", "XXXX"));

        Assert.Equal(@"WhatsApp Attachments\1_10_2025 10_00.jpg", r.Duplicados.Values.Single().Original);
    }

    [Fact]
    public void La_vista_previa_convertida_nunca_es_la_que_se_conserva()
    {
        var r = Buscar(Nuevo("IMG_1.HEIC.jpg", "XXXX"), Nuevo(@"carpeta\IMG_1_nombre_mas_largo.jpg", "XXXX"));

        Assert.Equal(@"carpeta\IMG_1_nombre_mas_largo.jpg", r.Duplicados[Path.Combine(Origen, "IMG_1.HEIC.jpg")].Original);
    }

    [Fact]
    public void Se_prefiere_la_copia_fuera_de_mensajes()
    {
        var r = Buscar(Nuevo(@"iPhone\Messages\a.jpg", "XXXX"), Nuevo(@"iPhone\DCIM\100APPLE\IMG_0001.jpg", "XXXX"));

        Assert.True(r.Duplicados.ContainsKey(Path.Combine(Origen, @"iPhone\Messages\a.jpg")));
    }

    [Fact]
    public void Si_ya_esta_organizado_todas_las_copias_nuevas_son_duplicados()
    {
        var r = Buscar(Nuevo(@"iPhone1\IMG_1.jpg", "XXXX"), Nuevo(@"iPhone2\IMG_1.jpg", "XXXX"),
                       Referencia(@"Fotos\2019\2019-03\IMG_1.jpg", "XXXX"));

        Assert.Equal(2, r.Duplicados.Count);
        Assert.All(r.Duplicados.Values, d =>
            Assert.Equal(new Duplicado(MotivoDuplicado.YaExisteEn, Path.Combine(Organizado, @"Fotos\2019\2019-03\IMG_1.jpg")), d));
    }

    [Theory]
    [InlineData(@"_duplicados\iPhone\IMG_1.jpg")]
    [InlineData(@"_duplicados_probables\iPhone\IMG_1.jpg")]
    [InlineData(@"_jpg_reemplazados\Fotos\2019\2019-03\IMG_1.jpg")]
    [InlineData(@"_revision_jpeg_con_extension_de_video\como_estan\IMG_1.jpg")]
    public void Como_referencia_se_prefiere_la_copia_que_no_esta_en_revision(string enRevision)
    {
        // "_..." ordena despues de "Fotos"; se pone una carpeta que ordena antes para probar la preferencia
        var r = Buscar(Nuevo("IMG_1.jpg", "XXXX"),
                       Referencia(enRevision, "XXXX"),
                       Referencia(@"Videos\2019\2019-03\IMG_1.jpg", "XXXX"));

        Assert.Equal(Path.Combine(Organizado, @"Videos\2019\2019-03\IMG_1.jpg"), r.Duplicados.Values.Single().Original);
    }

    [Fact]
    public void Grupos_solo_de_referencias_no_se_leen()
    {
        var r = Buscar(Nuevo("nuevo.jpg", "AAAAA"),
                       Referencia(@"Fotos\a.jpg", "XXXX"), Referencia(@"_duplicados\a.jpg", "XXXX"));

        Assert.Empty(r.Duplicados);
        Assert.Empty(leidos);
    }

    [Fact]
    public void Archivos_ilegibles_se_omiten()
    {
        var archivos = new[] { Nuevo("a.jpg", "XXXX"), Nuevo("b.jpg", "XXXX") };
        var r = BuscadorDuplicados.Buscar(archivos, ruta => ruta.EndsWith("a.jpg") ? null : "XXXX");

        Assert.Empty(r.Duplicados);
        Assert.Single(r.Hashes);
    }

    [Fact]
    public void Informa_el_progreso_y_se_puede_cancelar()
    {
        var archivos = new[] { Nuevo("a.jpg", "XXXX"), Nuevo("b.jpg", "XXXX"), Nuevo("c.jpg", "XXXX"), Nuevo("d.jpg", "XXXX") };
        var informes = new List<(int, int)>();
        using var cancelar = new CancellationTokenSource();

        // Se cancela mientras se lee el tercero: termina ese y no empieza el cuarto
        Assert.Throws<OperationCanceledException>(() => BuscadorDuplicados.Buscar(archivos, ruta =>
        {
            if (informes.Count == 2) cancelar.Cancel();
            return "XXXX";
        }, new ProgresoInmediato(informes), cancelar.Token));

        Assert.Equal([(1, 4), (2, 4), (3, 4)], informes);
    }

    [Fact]
    public void Md5_de_archivos_reales()
    {
        var dir = Directory.CreateTempSubdirectory("backuptidy-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "hola");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "hola");
            File.WriteAllText(Path.Combine(dir, "c.txt"), "chau");

            Assert.Equal("4D186321C1A7F0F354B297E8914AB240", BuscadorDuplicados.Md5(Path.Combine(dir, "a.txt")));
            Assert.Equal(BuscadorDuplicados.Md5(Path.Combine(dir, "a.txt")), BuscadorDuplicados.Md5(Path.Combine(dir, "b.txt")));
            Assert.NotEqual(BuscadorDuplicados.Md5(Path.Combine(dir, "a.txt")), BuscadorDuplicados.Md5(Path.Combine(dir, "c.txt")));
            Assert.Null(BuscadorDuplicados.Md5(Path.Combine(dir, "no-existe.txt")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Progress<T> informa en otro hilo; para las pruebas se necesita en el momento
    sealed class ProgresoInmediato(List<(int, int)> informes) : IProgress<(int Hechos, int Total)>
    {
        public void Report((int Hechos, int Total) valor) => informes.Add(valor);
    }
}
