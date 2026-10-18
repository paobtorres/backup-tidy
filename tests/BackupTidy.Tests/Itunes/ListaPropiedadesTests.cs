using BackupTidy.Core.Itunes;

namespace BackupTidy.Tests.Itunes;

public sealed class ListaPropiedadesTests
{
    [Fact]
    public void Lee_todos_los_tipos()
    {
        var fecha = new DateTime(2026, 10, 12, 14, 10, 42, DateTimeKind.Utc);
        var leido = (Dictionary<string, object?>)ListaPropiedades.Leer(EscritorPlist.Escribir(new Dictionary<string, object?>
        {
            ["texto"] = "Manifest",
            ["largo"] = "Media/DCIM/106APPLE/IMG_6671.HEIC",
            ["acentos"] = "iPhone de Paola ñandú",
            ["entero"] = 1784389125L,
            ["negativo"] = -5L,
            ["real"] = 2.5,
            ["fecha"] = fecha,
            ["si"] = true,
            ["no"] = false,
            ["nada"] = null,
            ["datos"] = new byte[] { 1, 2, 3 },
            ["uid"] = new UidPlist(7),
            ["lista"] = new List<object?> { "a", 1L, new Dictionary<string, object?> { ["b"] = "c" } },
        }))!;

        Assert.Equal("Manifest", leido["texto"]);
        Assert.Equal("Media/DCIM/106APPLE/IMG_6671.HEIC", leido["largo"]);
        Assert.Equal("iPhone de Paola ñandú", leido["acentos"]);
        Assert.Equal(1784389125L, leido["entero"]);
        Assert.Equal(-5L, leido["negativo"]);
        Assert.Equal(2.5, leido["real"]);
        Assert.Equal(fecha, leido["fecha"]);
        Assert.Equal(true, leido["si"]);
        Assert.Equal(false, leido["no"]);
        Assert.Null(leido["nada"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, leido["datos"]);
        Assert.Equal(new UidPlist(7), leido["uid"]);
        var lista = (List<object?>)leido["lista"]!;
        Assert.Equal(["a", 1L], lista.Take(2));
        Assert.Equal("c", ((Dictionary<string, object?>)lista[2]!)["b"]);
    }

    [Fact]
    public void Lee_enteros_de_un_byte_y_referencias_de_un_byte()
    {
        // { "a": 5 } escrito a mano como lo hace Apple con listas chicas
        byte[] datos =
        [
            .. "bplist00"u8,
            0xD1, 0x01, 0x02,   // 8: diccionario de 1 par, clave = objeto 1, valor = objeto 2
            0x51, (byte)'a',    // 11: "a"
            0x10, 0x05,         // 13: entero de 1 byte
            8, 11, 13,          // 15: offsets de 1 byte
            0, 0, 0, 0, 0, 0, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 3,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 15,
        ];

        var leido = (Dictionary<string, object?>)ListaPropiedades.Leer(datos)!;

        Assert.Equal(5L, leido["a"]);
    }

    [Fact]
    public void Raiz_de_un_NSKeyedArchiver()
    {
        var modificado = new DateTime(2025, 9, 9, 16, 8, 0, DateTimeKind.Utc);
        var raiz = ListaPropiedades.RaizArchivada(ListaPropiedades.Leer(EscritorPlist.MBFile("Media/DCIM/a.HEIC", 10, modificado, null)));

        Assert.NotNull(raiz);
        Assert.Equal((long)(modificado - DateTime.UnixEpoch).TotalSeconds, raiz["LastModified"]);
        Assert.Equal(10L, raiz["Size"]);
    }

    [Fact]
    public void Rechaza_lo_que_no_es_lista_binaria()
    {
        Assert.Throws<FormatException>(() => ListaPropiedades.Leer("<?xml version=\"1.0\"?><plist></plist>"u8.ToArray()));
        Assert.Throws<FormatException>(() => ListaPropiedades.Leer([]));
    }

    [Fact]
    public void Lista_danada_da_FormatException()
    {
        var datos = EscritorPlist.Escribir(new Dictionary<string, object?> { ["a"] = "b" });
        // La tabla de offsets apunta fuera del archivo
        datos[^1] = 0xFF;

        Assert.Throws<FormatException>(() => ListaPropiedades.Leer(datos));
    }

    [Fact]
    public void Una_lista_que_se_contiene_a_si_misma_no_se_recorre_para_siempre()
    {
        byte[] datos =
        [
            .. "bplist00"u8,
            0xA1, 0x00,         // 8: lista cuyo unico elemento es ella misma
            8,                  // 10: offsets
            0, 0, 0, 0, 0, 0, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 1,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 10,
        ];

        Assert.Throws<FormatException>(() => ListaPropiedades.Leer(datos));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e300)]
    public void Una_fecha_fuera_de_rango_da_FormatException(double segundos)
    {
        var fecha = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleBigEndian(fecha, segundos);
        byte[] datos =
        [
            .. "bplist00"u8,
            0x33, .. fecha,     // 8: fecha
            8,                  // 17: offsets
            0, 0, 0, 0, 0, 0, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 1,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 17,
        ];

        Assert.Throws<FormatException>(() => ListaPropiedades.Leer(datos));
    }

    [Fact]
    public void Un_largo_imposible_da_FormatException_sin_reservar_memoria()
    {
        byte[] datos =
        [
            .. "bplist00"u8,
            0xAF, 0x12, 0x7F, 0xFF, 0xFF, 0xFF, // 8: lista de 2.147.483.647 elementos en un archivo de 47 bytes
            8,                                  // 14: offsets
            0, 0, 0, 0, 0, 0, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 1,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 14,
        ];

        Assert.Throws<FormatException>(() => ListaPropiedades.Leer(datos));
    }
}
