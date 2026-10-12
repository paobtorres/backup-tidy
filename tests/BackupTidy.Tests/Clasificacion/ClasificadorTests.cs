using BackupTidy.Core.Clasificacion;

namespace BackupTidy.Tests.Clasificacion;

public class ClasificadorTests
{
    const long MB = 1024 * 1024;

    [Theory]
    [InlineData("IMG_0001.JPG", TipoArchivo.Foto)]
    [InlineData(@"iPhone\DCIM\IMG_0001.heic", TipoArchivo.Foto)]
    [InlineData("android/DCIM/raw.CR3", TipoArchivo.Foto)]
    [InlineData("VID_0001.MOV", TipoArchivo.Video)]
    [InlineData("clip.3gp", TipoArchivo.Video)]
    [InlineData("nota de voz.opus", TipoArchivo.Descarte)]
    [InlineData("contacto.vcf", TipoArchivo.Descarte)]
    [InlineData("IMG_0001.AAE", TipoArchivo.Descarte)]
    [InlineData("sticker.was", TipoArchivo.Descarte)]
    [InlineData("foto.jpg.Zone.Identifier", TipoArchivo.Descarte)]
    [InlineData(@"DCIM\Thumbs.db", TipoArchivo.Descarte)]
    [InlineData("Desktop.ini", TipoArchivo.Descarte)]
    [InlineData(".picasa.ini", TipoArchivo.Descarte)]
    [InlineData("config.ini", TipoArchivo.Documento)]
    [InlineData("informe.pdf", TipoArchivo.Documento)]
    [InlineData("LEEME", TipoArchivo.Documento)]
    public void TipoDe(string ruta, TipoArchivo esperado) => Assert.Equal(esperado, Clasificador.TipoDe(ruta));

    [Theory]
    [InlineData("a.PDF", TipoDocumento.PDF)]
    [InlineData("a.docx", TipoDocumento.Office)]
    [InlineData("tablero.pbix", TipoDocumento.Office)]
    [InlineData("a.7z", TipoDocumento.Comprimidos)]
    [InlineData("a.tar", TipoDocumento.Comprimidos)]
    [InlineData("a.txt", TipoDocumento.Otros)]
    [InlineData("sin_extension", TipoDocumento.Otros)]
    public void TipoDocumentoDe(string ruta, TipoDocumento esperado) =>
        Assert.Equal(esperado, Clasificador.TipoDocumentoDe(ruta));

    [Theory]
    // Apps tiene la maxima prioridad
    [InlineData(@"iPhone\Notes\App Photos\x.jpg", true, CategoriaMedia.Apps)]
    [InlineData(@"Android\WhatsApp\App Photos\x.jpg", true, CategoriaMedia.Apps)]
    // WhatsApp gana a capturas y mensajes
    [InlineData(@"Android\WhatsApp Images\IMG-20190315-WA0001.jpg", false, CategoriaMedia.WhatsApp)]
    [InlineData(@"Android\WhatsApp\Media\Screenshot_1.png", false, CategoriaMedia.WhatsApp)]
    [InlineData("Android/whatsapp business/a.mp4", false, CategoriaMedia.WhatsApp)]
    // Miniaturas
    [InlineData(@"iPhone\DCIM\VID_0001.THM.jpg", true, CategoriaMedia.Miniaturas)]
    // Capturas por nombre, aunque tengan marca de camara o esten en Messages
    [InlineData(@"iPhone\DCIM\Screenshot_2019-03-15.png", false, CategoriaMedia.Capturas)]
    [InlineData(@"iPhone\Captura de pantalla 1.jpg", true, CategoriaMedia.Capturas)]
    [InlineData(@"iPhone\Messages\Screen Shot 2.jpg", true, CategoriaMedia.Capturas)]
    // Mensajes: solo por carpeta
    [InlineData(@"iPhone\Messages\IMG_1.jpg", true, CategoriaMedia.Mensajes)]
    [InlineData(@"iPhone\attachments\IMG_1.HEIC", true, CategoriaMedia.Mensajes)]
    [InlineData(@"Android\Telegram Images\a.jpg", true, CategoriaMedia.Mensajes)]
    [InlineData(@"iPhone\MyMessages\IMG_1.jpg", true, CategoriaMedia.Fotos)]
    [InlineData(@"iPhone\Messages.jpg", true, CategoriaMedia.Fotos)]
    // Sin marca de camara: PNG -> Capturas, JPG/TIFF -> Guardadas, el resto sigue siendo Fotos
    [InlineData(@"iPhone\DCIM\IMG_1.PNG", false, CategoriaMedia.Capturas)]
    [InlineData(@"iPhone\DCIM\IMG_1.PNG", true, CategoriaMedia.Fotos)]
    [InlineData(@"iPhone\DCIM\IMG_1.JPG", false, CategoriaMedia.Guardadas)]
    [InlineData(@"iPhone\DCIM\IMG_1.tiff", false, CategoriaMedia.Guardadas)]
    [InlineData(@"iPhone\DCIM\IMG_1.JPG", true, CategoriaMedia.Fotos)]
    [InlineData(@"iPhone\DCIM\IMG_1.HEIC", false, CategoriaMedia.Fotos)]
    [InlineData(@"iPhone\DCIM\IMG_1.DNG", false, CategoriaMedia.Fotos)]
    public void CategoriaDeFoto(string ruta, bool marcaCamara, CategoriaMedia esperado) =>
        Assert.Equal(esperado, Clasificador.CategoriaDe(new DatosMedia(ruta, 3 * MB, marcaCamara, TieneFotoLive: false)));

    [Theory]
    [InlineData(@"iPhone\DCIM\RPReplay_Final1600000000.MP4", 5 * MB, true, CategoriaMedia.Capturas)]
    [InlineData(@"Android\DCIM\Screen_Recording_20190315.mp4", 5 * MB, false, CategoriaMedia.Capturas)]
    [InlineData(@"iPhone\Message Attachments\a.mov", 5 * MB, true, CategoriaMedia.Mensajes)]
    [InlineData(@"iPhone\DCIM\IMG_1.MOV", 3 * MB, true, CategoriaMedia.LivePhotos)]
    [InlineData(@"iPhone\DCIM\IMG_1.MOV", 30 * MB, true, CategoriaMedia.LivePhotos)]
    [InlineData(@"iPhone\DCIM\IMG_1.MOV", 30 * MB + 1, true, CategoriaMedia.Videos)]
    [InlineData(@"iPhone\DCIM\IMG_1.MOV", 3 * MB, false, CategoriaMedia.Videos)]
    public void CategoriaDeVideo(string ruta, long tamano, bool fotoLive, CategoriaMedia esperado) =>
        Assert.Equal(esperado, Clasificador.CategoriaDe(new DatosMedia(ruta, tamano, TieneMarcaCamara: false, fotoLive)));

    [Theory]
    // JPEG guardado como .MOV (caso real): es una foto, nunca Live Photo ni Video
    [InlineData(@"iPhone\DCIM\IMG_8979.MOV", ".jpg", true, CategoriaMedia.Fotos)]
    [InlineData(@"iPhone\DCIM\IMG_8979.MOV", ".jpg", false, CategoriaMedia.Guardadas)]
    [InlineData(@"iPhone\DCIM\IMG_8979.MP4", ".png", false, CategoriaMedia.Capturas)]
    // Video guardado como .jpg (todas estas filas tienen foto Live al lado)
    [InlineData(@"iPhone\DCIM\IMG_1.jpg", ".mov", false, CategoriaMedia.LivePhotos)]
    // Mismo tipo con otro formato: manda la extension del nombre
    [InlineData(@"iPhone\DCIM\IMG_1.DNG", ".tiff", false, CategoriaMedia.Fotos)]
    [InlineData(@"iPhone\DCIM\IMG_1.MP4", ".mov", false, CategoriaMedia.LivePhotos)]
    // Contenido no reconocido o que no es foto/video: manda la extension del nombre
    [InlineData(@"iPhone\DCIM\IMG_1.MOV", null, false, CategoriaMedia.LivePhotos)]
    [InlineData(@"iPhone\DCIM\IMG_1.MOV", ".pdf", false, CategoriaMedia.LivePhotos)]
    public void CategoriaDe_segun_el_contenido(string ruta, string? extContenido, bool marcaCamara, CategoriaMedia esperado) =>
        Assert.Equal(esperado, Clasificador.CategoriaDe(new DatosMedia(ruta, 3 * MB, marcaCamara, TieneFotoLive: true, extContenido)));

    [Theory]
    [InlineData("IMG_8979.MOV", ".jpg", "Es JPG con extension .MOV")]
    [InlineData(@"DCIM\IMG_1.jpg", ".mov", "Es MOV con extension .jpg")]
    [InlineData("IMG_1.DNG", ".tiff", null)]
    [InlineData("IMG_1.mp4", ".mov", null)]
    [InlineData("IMG_1.jpg", null, null)]
    [InlineData("IMG_1.jpg", ".pdf", null)]
    public void NotaContenido(string ruta, string? extContenido, string? esperado) =>
        Assert.Equal(esperado, Clasificador.NotaContenido(ruta, extContenido));

    [Fact]
    public void Un_jpeg_guardado_como_mov_cuenta_como_foto_live() =>
        Assert.True(Clasificador.PuedeSerFotoLive("IMG_1.MOV", ".jpg"));

    [Theory]
    [InlineData("informe.pdf")]
    [InlineData("nota.opus")]
    public void CategoriaDe_rechaza_lo_que_no_es_foto_ni_video(string ruta) =>
        Assert.Throws<ArgumentException>(() => Clasificador.CategoriaDe(new DatosMedia(ruta, 1, false, false)));

    [Theory]
    [InlineData(@"iPhone\DCIM\IMG_1234.HEIC.jpg", @"iPhone\DCIM\IMG_1234.HEIC")]
    [InlineData("IMG_1234.heif.JPEG", "IMG_1234.heif")]
    [InlineData("IMG_1234.PNG.jpg", "IMG_1234.PNG")]
    [InlineData("IMG_1234.DNG.jpg", "IMG_1234.DNG")]
    [InlineData("IMG_1234.MOV.jpg", "IMG_1234.MOV")]
    [InlineData("VID_1234.mp4.jpg", "VID_1234.mp4")]
    [InlineData("IMG_1234.jpg", null)]
    [InlineData("IMG_1234.gif.jpg", null)]
    [InlineData("IMG_1234.HEIC", null)]
    public void OriginalDeVistaPrevia(string ruta, string? esperado) =>
        Assert.Equal(esperado, Clasificador.OriginalDeVistaPrevia(ruta));

    [Theory]
    [InlineData("IMG_1.HEIC", true)]
    [InlineData("IMG_1.jpeg", true)]
    [InlineData("IMG_1.png", false)]
    [InlineData("IMG_1.MOV", false)]
    public void PuedeSerFotoLive(string ruta, bool esperado) => Assert.Equal(esperado, Clasificador.PuedeSerFotoLive(ruta));

    [Theory]
    // Misma foto y video del mismo backup, aunque cambien separador y mayusculas
    [InlineData(@"iPhone1\DCIM\100APPLE\IMG_0001.HEIC", "iphone1/dcim/100apple/img_0001.mov", true)]
    // Solo cuentan los 2 primeros niveles (como en el script de referencia)
    [InlineData(@"iPhone1\DCIM\100APPLE\IMG_0001.HEIC", @"iPhone1\DCIM\101APPLE\IMG_0001.MOV", true)]
    [InlineData(@"iPhone1\DCIM\100APPLE\IMG_0001.HEIC", @"iPhone2\DCIM\100APPLE\IMG_0001.MOV", false)]
    [InlineData(@"iPhone1\DCIM\IMG_0001.HEIC", @"iPhone1\DCIM\IMG_0002.MOV", false)]
    [InlineData("IMG_0001.HEIC", "IMG_0001.MOV", true)]
    [InlineData("IMG_0001.HEIC", @"iPhone1\IMG_0001.MOV", false)]
    public void ClaveLive(string foto, string video, bool emparejan) =>
        Assert.Equal(emparejan, Clasificador.ClaveLive(foto) == Clasificador.ClaveLive(video));

    [Theory]
    [InlineData(@"iPhone\Messages\a.jpg", true)]
    [InlineData(@"Android\WhatsApp Images\a.jpg", true)]
    [InlineData(@"Android\Telegram\a.jpg", true)]
    [InlineData(@"iPhone\DCIM\a.jpg", false)]
    [InlineData("WhatsApp.jpg", false)]
    public void EstaEnMensajes(string ruta, bool esperado) => Assert.Equal(esperado, Clasificador.EstaEnMensajes(ruta));
}
