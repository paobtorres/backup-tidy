using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace BackupTidy.Core.Ejecucion;

/// <summary>Papelera de reciclaje. Nunca borra en forma definitiva.</summary>
public interface IPapelera
{
    /// <summary>
    /// Bytes que admite la Papelera del disco de <paramref name="ruta"/>, o null si no se puede confirmar
    /// que guarda los archivos: sin configuracion conocida, o configurada para borrar directamente.
    /// </summary>
    long? Capacidad(string ruta);

    void EnviarArchivo(string ruta);

    void EnviarCarpeta(string ruta);
}

/// <summary>Papelera de Windows (SHFileOperation con deshacer, via Microsoft.VisualBasic.FileIO).</summary>
[SupportedOSPlatform("windows")]
public sealed class PapeleraWindows : IPapelera
{
    const string ClaveBitBucket = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\";

    public long? Capacidad(string ruta)
    {
        // \\?\Volume{GUID}\ -> {GUID}
        var volumen = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPoint(Path.GetPathRoot(Path.GetFullPath(ruta))!, volumen, volumen.Capacity)) return null;
        var texto = volumen.ToString();
        var inicio = texto.IndexOf('{');
        if (inicio < 0) return null;
        var guid = texto[inicio..(texto.IndexOf('}') + 1)];

        using var clave = Registry.CurrentUser.OpenSubKey(ClaveBitBucket + guid);
        if (clave?.GetValue("MaxCapacity") is not int maxMb) return null;
        // Si supera la capacidad, Windows borraria definitivamente sin avisar
        if (clave.GetValue("NukeOnDelete") is 1) return null;
        return maxMb * 1024L * 1024;
    }

    public void EnviarArchivo(string ruta) =>
        FileSystem.DeleteFile(ruta, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

    public void EnviarCarpeta(string ruta) =>
        FileSystem.DeleteDirectory(ruta, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetVolumeNameForVolumeMountPoint(string puntoMontaje, StringBuilder nombreVolumen, int largo);
}
