using System.Buffers.Binary;
using System.Text;

namespace BackupTidy.Core.Itunes;

/// <summary>Referencia de NSKeyedArchiver (CF$UID): la posicion de un objeto en <c>$objects</c>.</summary>
public readonly record struct UidPlist(long Valor);

/// <summary>
/// Lector de listas de propiedades binarias de Apple (<c>bplist00</c>): el formato de <c>Manifest.plist</c>
/// y de los metadatos de cada archivo en <c>Manifest.db</c>.
/// </summary>
/// <remarks>
/// Devuelve <c>Dictionary&lt;string, object?&gt;</c>, <c>List&lt;object?&gt;</c>, <c>string</c>, <c>long</c>,
/// <c>double</c>, <c>bool</c>, <c>byte[]</c>, <c>DateTime</c> (UTC) o <see cref="UidPlist"/>.
/// </remarks>
public static class ListaPropiedades
{
    static readonly DateTime Epoca2001 = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly double SegundosMinimos = (DateTime.MinValue - Epoca2001).TotalSeconds + 1;
    static readonly double SegundosMaximos = (DateTime.MaxValue - Epoca2001).TotalSeconds - 1;

    public static bool EsBinaria(ReadOnlySpan<byte> datos) => datos.StartsWith("bplist00"u8);

    /// <exception cref="FormatException">No es una lista binaria o esta danada.</exception>
    public static object? Leer(byte[] datos)
    {
        if (datos.Length < 8 + 32 || !EsBinaria(datos)) throw new FormatException("No es una lista de propiedades binaria");
        try
        {
            var cola = datos.AsSpan(datos.Length - 32);
            var lector = new Lector(datos, cola[6], cola[7], (long)Entero(cola[8..16]), (long)Entero(cola[24..32]));
            return lector.Objeto((long)Entero(cola[16..24]), 0);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            throw new FormatException("Lista de propiedades danada", e);
        }
    }

    /// <summary>El objeto raiz de un NSKeyedArchiver (<c>$top.root</c> dentro de <c>$objects</c>).</summary>
    public static Dictionary<string, object?>? RaizArchivada(object? lista) =>
        lista is Dictionary<string, object?> archivo
        && archivo.GetValueOrDefault("$top") is Dictionary<string, object?> top
        && top.GetValueOrDefault("root") is UidPlist raiz
        && archivo.GetValueOrDefault("$objects") is List<object?> objetos
        && raiz.Valor >= 0 && raiz.Valor < objetos.Count
            ? objetos[(int)raiz.Valor] as Dictionary<string, object?>
            : null;

    static ulong Entero(ReadOnlySpan<byte> bytes)
    {
        ulong valor = 0;
        foreach (var b in bytes) valor = valor << 8 | b;
        return valor;
    }

    sealed class Lector(byte[] datos, int tamOffset, int tamReferencia, long cantidad, long tablaOffsets)
    {
        // Los objetos pueden referenciarse entre si: el limite evita recorrer un ciclo para siempre
        const int ProfundidadMaxima = 64;

        public object? Objeto(long indice, int profundidad)
        {
            if (indice < 0 || indice >= cantidad || profundidad > ProfundidadMaxima)
                throw new FormatException("Lista de propiedades danada");
            var pos = (long)Entero(Bytes(tablaOffsets + indice * tamOffset, tamOffset));
            var marca = datos[pos];
            var info = marca & 0xF;
            switch (marca >> 4)
            {
                case 0x0:
                    return info switch { 0x8 => false, 0x9 => true, _ => null };
                case 0x1:
                {
                    // 1, 2 y 4 bytes sin signo; 8 con signo; 16 (enteros enormes) se toman los 8 de abajo
                    var bytes = Bytes(pos + 1, 1 << info);
                    return (long)Entero(bytes.Length > 8 ? bytes[^8..] : bytes);
                }
                case 0x2:
                    return info == 2
                        ? (double)BinaryPrimitives.ReadSingleBigEndian(Bytes(pos + 1, 4))
                        : BinaryPrimitives.ReadDoubleBigEndian(Bytes(pos + 1, 8));
                case 0x3:
                {
                    var segundos = BinaryPrimitives.ReadDoubleBigEndian(Bytes(pos + 1, 8));
                    if (!double.IsFinite(segundos) || segundos < SegundosMinimos || segundos > SegundosMaximos)
                        throw new FormatException("Lista de propiedades con una fecha fuera de rango");
                    return Epoca2001.AddSeconds(segundos);
                }
                case 0x4:
                {
                    var (n, inicio) = Largo(pos, info);
                    return Bytes(inicio, n).ToArray();
                }
                case 0x5:
                {
                    var (n, inicio) = Largo(pos, info);
                    return Encoding.ASCII.GetString(Bytes(inicio, n));
                }
                case 0x6:
                {
                    var (n, inicio) = Largo(pos, info);
                    return Encoding.BigEndianUnicode.GetString(Bytes(inicio, n * 2));
                }
                case 0x8:
                    return new UidPlist((long)Entero(Bytes(pos + 1, info + 1)));
                case 0xA or 0xC:
                {
                    var (n, inicio) = Largo(pos, info);
                    var lista = new List<object?>(n);
                    for (var i = 0; i < n; i++) lista.Add(Objeto(Referencia(inicio, i), profundidad + 1));
                    return lista;
                }
                case 0xD:
                {
                    var (n, inicio) = Largo(pos, info);
                    var dic = new Dictionary<string, object?>(n);
                    for (var i = 0; i < n; i++)
                    {
                        if (Objeto(Referencia(inicio, i), profundidad + 1) is not string clave)
                            throw new FormatException("Lista de propiedades con una clave que no es texto");
                        dic[clave] = Objeto(Referencia(inicio, n + i), profundidad + 1);
                    }
                    return dic;
                }
                default:
                    throw new FormatException($"Tipo de objeto desconocido: 0x{marca:X2}");
            }
        }

        // Cantidad de elementos: en la marca, o en un entero a continuacion si no entra (15 o mas)
        (int Cantidad, long Inicio) Largo(long pos, int info)
        {
            if (info != 0xF) return (info, pos + 1);
            var marca = datos[pos + 1];
            if (marca >> 4 != 0x1) throw new FormatException("Lista de propiedades danada");
            var tam = 1 << (marca & 0xF);
            var n = Entero(Bytes(pos + 2, tam));
            // Cada elemento ocupa al menos un byte: mas que eso es un largo danado (y reservar lugar se quedaria sin memoria)
            if (n > (ulong)datos.Length) throw new FormatException("Lista de propiedades danada");
            return ((int)n, pos + 2 + tam);
        }

        long Referencia(long inicio, int i) => (long)Entero(Bytes(inicio + (long)i * tamReferencia, tamReferencia));

        ReadOnlySpan<byte> Bytes(long inicio, int n) =>
            inicio < 0 || n < 0 || inicio + n > datos.Length
                ? throw new FormatException("Lista de propiedades danada")
                : datos.AsSpan((int)inicio, n);
    }
}
