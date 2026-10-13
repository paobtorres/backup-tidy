using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace BackupTidy.App.Servicios;

/// <summary>
/// Vista previa reducida de una foto o video. Primero se decodifica por el contenido (un JPEG guardado como .MOV
/// se ve igual) y ya derecha segun la orientacion EXIF; lo que Skia no lee (HEIC, RAW, TIFF, videos) se le pide
/// a Windows, que da la misma miniatura que el Explorador si estan instalados los codecs.
/// </summary>
public static class Miniaturas
{
    public static Task<Bitmap?> Cargar(string? ruta, int lado) => Task.Run(() =>
    {
        if (string.IsNullOrEmpty(ruta)) return null;
        try
        {
            using (var archivo = File.OpenRead(ruta))
                if (DecodificarConSkia(archivo, lado) is { } imagen) return imagen;
            return OperatingSystem.IsWindows() ? MiniaturaDeWindows.Cargar(ruta, lado) : null;
        }
        catch (Exception)
        {
            // Archivo danado, bloqueado o que ya no existe: sin vista previa
            return null;
        }
    });

    static Bitmap? DecodificarConSkia(Stream contenido, int lado)
    {
        using var codec = SKCodec.Create(contenido);
        if (codec is null) return null;

        // JPEG se decodifica directo a 1/2, 1/4 u 1/8 del tamano: mucho mas rapido que leerlo entero
        var info = codec.Info;
        var escala = Math.Min(1f, (float)lado / Math.Max(info.Width, info.Height));
        var reducido = codec.GetScaledDimensions(escala);
        using var leido = new SKBitmap(new SKImageInfo(reducido.Width, reducido.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (codec.GetPixels(leido.Info, leido.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return null;

        // Las fotos del celular suelen guardarse acostadas con una marca de giro: se dibujan ya derechas
        var origen = codec.EncodedOrigin;
        var (w, h) = (leido.Width, leido.Height);
        var (anchoDerecho, altoDerecho) = origen >= SKEncodedOrigin.LeftTop ? (h, w) : (w, h);
        var ajuste = Math.Min(1f, (float)lado / Math.Max(anchoDerecho, altoDerecho));
        var final = new SKImageInfo(Math.Max(1, (int)(anchoDerecho * ajuste)), Math.Max(1, (int)(altoDerecho * ajuste)),
                                    SKColorType.Bgra8888, SKAlphaType.Premul);

        using var derecho = new SKBitmap(final);
        using (var lienzo = new SKCanvas(derecho))
        using (var imagen = SKImage.FromBitmap(leido))
        {
            lienzo.Scale((float)final.Width / anchoDerecho, (float)final.Height / altoDerecho);
            lienzo.Concat(Enderezar(origen, w, h));
            lienzo.DrawImage(imagen, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, derecho.GetPixels(),
                          new PixelSize(final.Width, final.Height), new Vector(96, 96), derecho.RowBytes);
    }

    // Lleva cada pixel (x, y) de la imagen guardada a su lugar en la imagen derecha (orientaciones EXIF 1 a 8)
    static SKMatrix Enderezar(SKEncodedOrigin origen, float w, float h) => origen switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
}

/// <summary>La miniatura que muestra el Explorador de Windows (IShellItemImageFactory).</summary>
[SupportedOSPlatform("windows")]
static class MiniaturaDeWindows
{
    const int SoloMiniatura = 0x08;   // SIIGBF_THUMBNAILONLY: sin miniatura no devuelve el icono del tipo de archivo

    public static Bitmap? Cargar(string ruta, int lado)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(Path.GetFullPath(ruta), IntPtr.Zero, ref iid, out var fabrica) != 0) return null;
        var hbitmap = IntPtr.Zero;
        try
        {
            if (fabrica.GetImage(new Tamano { Ancho = lado, Alto = lado }, SoloMiniatura, out hbitmap) != 0) return null;
            return CopiarPixeles(hbitmap);
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
            Marshal.ReleaseComObject(fabrica);
        }
    }

    static Bitmap? CopiarPixeles(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return null;
        var (ancho, alto) = (bm.bmWidth, Math.Abs(bm.bmHeight));

        // Alto negativo: filas de arriba hacia abajo, como las espera Avalonia
        var cabecera = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = ancho, biHeight = -alto, biPlanes = 1, biBitCount = 32,
        };
        var pixeles = new byte[ancho * alto * 4];
        var pantalla = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(pantalla, hbitmap, 0, (uint)alto, pixeles, ref cabecera, 0) != alto) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, pantalla);
        }

        // Las fotos suelen venir sin canal alfa (todo en 0): se las trata como opacas
        var sinAlfa = true;
        for (var i = 3; i < pixeles.Length && sinAlfa; i += 4) sinAlfa = pixeles[i] == 0;
        if (sinAlfa)
            for (var i = 3; i < pixeles.Length; i += 4) pixeles[i] = 255;

        var fijados = GCHandle.Alloc(pixeles, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, fijados.AddrOfPinnedObject(),
                              new PixelSize(ancho, alto), new Vector(96, 96), ancho * 4);
        }
        finally
        {
            fijados.Free();
        }
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Tamano tamano, int opciones, out IntPtr hbitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Tamano
    {
        public int Ancho, Alto;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string ruta, IntPtr contexto, ref Guid iid, out IShellItemImageFactory fabrica);

    [DllImport("gdi32.dll")]
    static extern int GetObject(IntPtr objeto, int tamano, out BITMAP bm);

    [DllImport("gdi32.dll")]
    static extern int GetDIBits(IntPtr dc, IntPtr hbitmap, uint inicio, uint lineas, byte[] pixeles, ref BITMAPINFOHEADER info, uint uso);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr objeto);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr ventana);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr ventana, IntPtr dc);
}
