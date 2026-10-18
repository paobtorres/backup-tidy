using System.Buffers.Binary;
using System.Text;
using BackupTidy.Core.Itunes;

namespace BackupTidy.Tests.Itunes;

/// <summary>Escribe listas de propiedades binarias (bplist00) para armar copias de iTunes de prueba.</summary>
static class EscritorPlist
{
    static readonly DateTime Epoca2001 = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static byte[] Escribir(object? raiz)
    {
        var objetos = new List<byte[]>();
        Agregar(raiz, objetos);

        // Referencias de 2 bytes y offsets de 4: alcanza para las pruebas
        var archivo = new List<byte>("bplist00"u8.ToArray());
        var offsets = new List<int>();
        foreach (var o in objetos)
        {
            offsets.Add(archivo.Count);
            archivo.AddRange(o);
        }
        var tabla = archivo.Count;
        foreach (var o in offsets) archivo.AddRange(BigEndian(o, 4));
        archivo.AddRange(new byte[6]);
        archivo.Add(4);
        archivo.Add(2);
        archivo.AddRange(BigEndian(objetos.Count, 8));
        archivo.AddRange(BigEndian(0, 8));
        archivo.AddRange(BigEndian(tabla, 8));
        return [.. archivo];
    }

    /// <summary>Metadatos de un archivo como los guarda Manifest.db: un MBFile en NSKeyedArchiver.</summary>
    public static byte[] MBFile(string rutaRelativa, long bytes, DateTime? modificado, DateTime? creado) =>
        MBFile(rutaRelativa, bytes, Unix(modificado), Unix(creado));

    /// <summary>Con las fechas en segundos Unix tal cual, para probar valores danados.</summary>
    public static byte[] MBFile(string rutaRelativa, long bytes, long modificado, long creado) => Escribir(new Dictionary<string, object?>
    {
        ["$version"] = 100000L,
        ["$archiver"] = "NSKeyedArchiver",
        ["$top"] = new Dictionary<string, object?> { ["root"] = new UidPlist(1) },
        ["$objects"] = new List<object?>
        {
            "$null",
            new Dictionary<string, object?>
            {
                ["RelativePath"] = new UidPlist(2),
                ["Size"] = bytes,
                ["LastModified"] = modificado,
                ["Birth"] = creado,
                ["Mode"] = 33188L,
                ["$class"] = new UidPlist(3),
            },
            rutaRelativa,
            new Dictionary<string, object?> { ["$classname"] = "MBFile", ["$classes"] = new List<object?> { "MBFile", "NSObject" } },
        },
    });

    static long Unix(DateTime? fecha) => fecha is DateTime f ? (long)(f - DateTime.UnixEpoch).TotalSeconds : 0;

    static int Agregar(object? valor, List<byte[]> objetos)
    {
        var indice = objetos.Count;
        objetos.Add([]);
        objetos[indice] = valor switch
        {
            null => [0x00],
            bool b => [b ? (byte)0x09 : (byte)0x08],
            long n => [0x13, .. BigEndian(n, 8)],
            int n => [0x13, .. BigEndian(n, 8)],
            double d => [0x23, .. BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(d)).Reverse()],
            DateTime f => [0x33, .. BitConverter.GetBytes(BitConverter.DoubleToInt64Bits((f.ToUniversalTime() - Epoca2001).TotalSeconds)).Reverse()],
            byte[] datos => [.. Cabecera(0x4, datos.Length), .. datos],
            string s when s.All(c => c < 128) => [.. Cabecera(0x5, s.Length), .. Encoding.ASCII.GetBytes(s)],
            string s => [.. Cabecera(0x6, s.Length), .. Encoding.BigEndianUnicode.GetBytes(s)],
            UidPlist uid => [0x83, .. BigEndian(uid.Valor, 4)],
            List<object?> lista => Coleccion(0xA, lista.Select(v => Agregar(v, objetos)).ToList()),
            Dictionary<string, object?> dic => Diccionario(dic, objetos),
            _ => throw new ArgumentException($"Tipo no soportado: {valor.GetType()}"),
        };
        return indice;
    }

    static byte[] Diccionario(Dictionary<string, object?> dic, List<byte[]> objetos)
    {
        var claves = dic.Keys.Select(k => Agregar(k, objetos)).ToList();
        var valores = dic.Values.Select(v => Agregar(v, objetos)).ToList();
        return [.. Cabecera(0xD, dic.Count), .. claves.Concat(valores).SelectMany(r => BigEndian(r, 2))];
    }

    static byte[] Coleccion(int tipo, List<int> referencias) =>
        [.. Cabecera(tipo, referencias.Count), .. referencias.SelectMany(r => BigEndian(r, 2))];

    // Con 15 elementos o mas, la cantidad va en un entero aparte
    static byte[] Cabecera(int tipo, int cantidad) =>
        cantidad < 15 ? [(byte)(tipo << 4 | cantidad)] : [(byte)(tipo << 4 | 0xF), 0x12, .. BigEndian(cantidad, 4)];

    static byte[] BigEndian(long valor, int bytes)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, valor);
        return b[^bytes..];
    }
}
