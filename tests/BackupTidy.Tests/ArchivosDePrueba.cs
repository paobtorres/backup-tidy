using System.Text;

namespace BackupTidy.Tests;

/// <summary>Genera en memoria JPG y MOV minimos con los metadatos que lee el motor.</summary>
static class ArchivosDePrueba
{
    const ushort TipoAscii = 2, TipoLong = 4;
    const ushort TagMake = 0x010F, TagDateTime = 0x0132, TagExifIfd = 0x8769,
                 TagDateTimeOriginal = 0x9003, TagDateTimeDigitized = 0x9004;

    /// <summary>JPG con un segmento EXIF (APP1). Las fechas van en formato EXIF: "2019:03:15 12:12:00".</summary>
    public static byte[] Jpeg(string? marca = null, string? original = null, string? digitalizada = null, string? fechaHora = null)
    {
        var ifd0 = new List<(ushort, string)>();
        if (marca != null) ifd0.Add((TagMake, marca));
        if (fechaHora != null) ifd0.Add((TagDateTime, fechaHora));
        var exif = new List<(ushort, string)>();
        if (original != null) exif.Add((TagDateTimeOriginal, original));
        if (digitalizada != null) exif.Add((TagDateTimeDigitized, digitalizada));

        var tiff = Tiff(ifd0, exif);
        var largo = 2 + 6 + tiff.Length;
        return [0xFF, 0xD8, 0xFF, 0xE1, (byte)(largo >> 8), (byte)largo, .. "Exif\0\0"u8, .. tiff, 0xFF, 0xD9];
    }

    /// <summary>JPG con un segmento XMP (APP1) con xmp:CreateDate, como los stickers y fondos editados.</summary>
    public static byte[] JpegConXmp(string fechaCreacion)
    {
        var xmp = Encoding.UTF8.GetBytes(
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
            $"<rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" xmp:CreateDate=\"{fechaCreacion}\"/>" +
            "</rdf:RDF></x:xmpmeta>");
        byte[] cuerpo = [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. xmp];
        var largo = 2 + cuerpo.Length;
        return [0xFF, 0xD8, 0xFF, 0xE1, (byte)(largo >> 8), (byte)largo, .. cuerpo, 0xFF, 0xD9];
    }

    /// <summary>JPG sin EXIF.</summary>
    public static byte[] JpegSinExif() => [0xFF, 0xD8, 0xFF, 0xD9];

    /// <summary>MOV con cabecera mvhd (fecha en UTC) y, opcionalmente, com.apple.quicktime.creationdate.</summary>
    public static byte[] Mov(DateTime? creadoUtc, string? fechaApple = null)
    {
        var mvhd = new byte[100];
        if (creadoUtc is DateTime c) Be32((uint)(c - new DateTime(1904, 1, 1)).TotalSeconds).CopyTo(mvhd, 4);
        Be32(600).CopyTo(mvhd, 12);   // escala de tiempo
        var hijos = new List<byte[]> { Atomo("mvhd", mvhd) };

        if (fechaApple != null)
        {
            var clave = "com.apple.quicktime.creationdate"u8.ToArray();
            var keys = Atomo("keys", new byte[4], Be32(1), Be32((uint)(8 + clave.Length)), "mdta"u8.ToArray(), clave);
            var data = Atomo("data", Be32(1), new byte[4], Encoding.UTF8.GetBytes(fechaApple));
            byte[] item = [.. Be32((uint)(8 + data.Length)), .. Be32(1), .. data];
            var hdlr = Atomo("hdlr", new byte[8], "mdta"u8.ToArray(), new byte[13]);
            hijos.Add(Atomo("meta", hdlr, keys, Atomo("ilst", item)));
        }
        var ftyp = Atomo("ftyp", "qt  "u8.ToArray(), new byte[4], "qt  "u8.ToArray());
        return [.. ftyp, .. Atomo("moov", [.. hijos])];
    }

    // TIFF little endian: IFD0 (+ puntero al Exif IFD) seguido del Exif IFD
    static byte[] Tiff(List<(ushort Tag, string Valor)> ifd0, List<(ushort Tag, string Valor)> exif)
    {
        var entradas0 = ifd0.Select(e => (e.Tag, TipoAscii, Ascii(e.Valor))).ToList();
        if (exif.Count > 0) entradas0.Add((TagExifIfd, TipoLong, new byte[4]));
        var largo0 = Ifd(8, entradas0).Length;

        if (exif.Count > 0)
            entradas0[^1] = (TagExifIfd, TipoLong, BitConverter.GetBytes((uint)(8 + largo0)));
        var exifIfd = exif.Count > 0 ? Ifd((uint)(8 + largo0), exif.Select(e => (e.Tag, TipoAscii, Ascii(e.Valor))).ToList()) : [];

        return [.. "II"u8, 42, 0, 8, 0, 0, 0, .. Ifd(8, entradas0), .. exifIfd];
    }

    static byte[] Ifd(uint inicio, List<(ushort Tag, ushort Tipo, byte[] Datos)> entradas)
    {
        entradas = [.. entradas.OrderBy(e => e.Tag)];
        var largoIfd = 2 + entradas.Count * 12 + 4;
        using var ms = new MemoryStream();
        using var extra = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((ushort)entradas.Count);
        foreach (var (tag, tipo, datos) in entradas)
        {
            w.Write(tag);
            w.Write(tipo);
            w.Write((uint)(tipo == TipoAscii ? datos.Length : 1));
            if (datos.Length <= 4)
            {
                var valor = new byte[4];
                datos.CopyTo(valor, 0);
                w.Write(valor);
            }
            else
            {
                w.Write((uint)(inicio + largoIfd + extra.Length));
                extra.Write(datos);
            }
        }
        w.Write(0u);
        w.Write(extra.ToArray());
        return ms.ToArray();
    }

    static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s + "\0");

    static byte[] Be32(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    static byte[] Atomo(string tipo, params byte[][] partes)
    {
        byte[] cuerpo = [.. partes.SelectMany(p => p)];
        return [.. Be32((uint)(8 + cuerpo.Length)), .. Encoding.ASCII.GetBytes(tipo), .. cuerpo];
    }
}
