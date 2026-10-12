using Avalonia.Media.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace BackupTidy.App.Servicios;

public sealed record Miniatura(Bitmap Imagen, double Rotacion);

/// <summary>
/// Vista previa reducida de una imagen, decidida por el contenido (un JPEG guardado como .MOV se ve igual).
/// HEIC, RAW y videos todavia no tienen vista previa.
/// </summary>
public static class Miniaturas
{
    public static Task<Miniatura?> Cargar(string? ruta, int ancho) => Task.Run(() =>
    {
        if (string.IsNullOrEmpty(ruta)) return null;
        try
        {
            using var archivo = File.OpenRead(ruta);
            var cabecera = new byte[12];
            if (archivo.Read(cabecera) < cabecera.Length || !EsDecodificable(cabecera)) return null;

            archivo.Position = 0;
            var rotacion = Rotacion(archivo);
            archivo.Position = 0;
            return new Miniatura(Bitmap.DecodeToWidth(archivo, ancho, BitmapInterpolationMode.MediumQuality), rotacion);
        }
        catch (Exception)
        {
            // Archivo danado, bloqueado o que ya no existe: sin vista previa
            return null;
        }
    });

    static bool EsDecodificable(byte[] c) =>
        c is [0xFF, 0xD8, 0xFF, ..]                                              // JPEG
        || c is [0x89, (byte)'P', (byte)'N', (byte)'G', ..]                      // PNG
        || c is [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..]                  // GIF
        || c is [(byte)'B', (byte)'M', ..]                                       // BMP
        || c is [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P'];

    // Orientacion EXIF: las fotos del celular suelen guardarse acostadas con una marca de giro
    static double Rotacion(Stream contenido)
    {
        try
        {
            var ifd0 = ImageMetadataReader.ReadMetadata(contenido).OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is null || !ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientacion)) return 0;
            return orientacion switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
