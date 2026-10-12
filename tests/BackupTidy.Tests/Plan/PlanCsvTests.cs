using System.Text;
using BackupTidy.Core.Fechas;
using BackupTidy.Core.Plan;

namespace BackupTidy.Tests.Plan;

public sealed class PlanCsvTests : IDisposable
{
    readonly CarpetaTemporal carpeta = new();

    public void Dispose() => carpeta.Dispose();

    [Fact]
    public void Guarda_y_lee_el_mismo_plan()
    {
        FilaPlan[] filas =
        [
            new(CategoriaPlan.Fotos, @"D:\b\IMG_1.JPG", @"D:\o\Fotos\2019\2019-03\IMG_1.JPG", 3_552_252,
                new DateTime(2019, 3, 15, 12, 12, 0), FuenteFecha.Exif, "ABC123", "Es JPG con extension .MOV", @"D:\b\otra\IMG_1.JPG"),
            new(CategoriaPlan.Duplicados, @"D:\b\con ""comillas""; y punto y coma.jpg", @"D:\o\_duplicados\x.jpg", 1,
                FuenteFecha: FuenteFecha.ArchivoDudoso, Nota: "linea 1\nlinea 2"),
            new(CategoriaPlan.Descartar, @"D:\b\nota.opus", null, 10, Nota: "Va a la Papelera"),
        ];
        var ruta = carpeta["plan.csv"];

        PlanCsv.Escribir(filas, ruta);

        Assert.Equal(filas, PlanCsv.Leer(ruta));
    }

    [Fact]
    public void Formato_para_excel_y_compatible_con_el_script()
    {
        var ruta = carpeta["plan.csv"];
        PlanCsv.Escribir([new(CategoriaPlan.Fotos, @"D:\b\a.jpg", @"D:\o\a.jpg", 5, new DateTime(2019, 3, 15, 12, 12, 30), FuenteFecha.ArchivoDudoso)], ruta);

        var bytes = File.ReadAllBytes(ruta);
        var lineas = Encoding.UTF8.GetString(bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);   // BOM: Excel lo abre como UTF-8
        Assert.Equal("\uFEFF\"Categoria\";\"Fecha\";\"FechaFuente\";\"TamanoMB\";\"Bytes\";\"Origen\";\"Destino\";\"Hash\";\"Nota\";\"Relacionado\"", lineas[0]);
        Assert.StartsWith("\"Fotos\";\"2019-03-15 12:12\";\"archivo_dudoso\";", lineas[1]);
    }

    [Fact]
    public void Lee_un_plan_separado_por_comas()
    {
        var ruta = carpeta["plan.csv"];
        File.WriteAllText(ruta, "\"Categoria\",\"Fecha\",\"FechaFuente\",\"TamanoMB\",\"Bytes\",\"Origen\",\"Destino\",\"Hash\",\"Nota\"\r\n" +
                                "\"Videos\",\"2019-03-15 12:12\",\"metadatos\",\"0.5\",\"524288\",\"D:\\b\\v.mov\",\"D:\\o\\v.mov\",\"\",\"\"\r\n");

        var f = Assert.Single(PlanCsv.Leer(ruta));

        Assert.Equal((CategoriaPlan.Videos, FuenteFecha.Metadatos, 524288L, (string?)null), (f.Categoria, f.FuenteFecha!.Value, f.Bytes, f.Hash));
    }
}
