using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BackupTidy.Tests.Itunes;

/// <summary>
/// Copia de iTunes de prueba con la estructura real: Manifest.plist binario, Manifest.db con la tabla Files y
/// cada archivo guardado como <c>&lt;2 caracteres&gt;\&lt;SHA1 de "dominio-ruta"&gt;</c>.
/// </summary>
sealed class CopiaDePrueba : IDisposable
{
    readonly CarpetaTemporal carpeta = new();
    readonly string conexion;

    public CopiaDePrueba(string dispositivo = "iPhone de prueba", bool encriptada = false, DateTime? fecha = null)
    {
        File.WriteAllBytes(carpeta["Manifest.plist"], EscritorPlist.Escribir(new Dictionary<string, object?>
        {
            ["IsEncrypted"] = encriptada,
            ["Version"] = "10.0",
            ["Date"] = fecha ?? new DateTime(2026, 10, 12, 14, 10, 0, DateTimeKind.Utc),
            ["Lockdown"] = new Dictionary<string, object?>
            {
                ["DeviceName"] = dispositivo,
                ["ProductVersion"] = "26.5.2",
                ["ProductType"] = "iPhone15,2",
            },
        }));
        Estado("finished");
        conexion = new SqliteConnectionStringBuilder { DataSource = carpeta["Manifest.db"], Pooling = false }.ToString();
        Ejecutar("CREATE TABLE Files (fileID TEXT PRIMARY KEY, domain TEXT, relativePath TEXT, flags INTEGER, file BLOB)");
    }

    public string Ruta => carpeta.Ruta;

    /// <summary><c>Status.plist</c> como lo deja iTunes: <c>finished</c> al terminar la copia.</summary>
    public void Estado(string snapshotState) =>
        File.WriteAllBytes(carpeta["Status.plist"], EscritorPlist.Escribir(new Dictionary<string, object?>
        {
            ["IsFullBackup"] = false,
            ["Version"] = "3.3",
            ["BackupState"] = "new",
            ["SnapshotState"] = snapshotState,
        }));

    /// <summary>Una fila de archivo (flags = 1) con lo que sea en cada columna: SQLite no controla los tipos.</summary>
    public void AgregarFila(object? id, object? dominio, object? ruta, object? file) =>
        Ejecutar("INSERT INTO Files VALUES ($id, $dominio, $ruta, 1, $file)",
                 ("$id", id ?? DBNull.Value), ("$dominio", dominio ?? DBNull.Value), ("$ruta", ruta ?? DBNull.Value), ("$file", file ?? DBNull.Value));

    /// <summary>Agrega un archivo con los metadatos dados tal cual (el blob de la columna <c>file</c>).</summary>
    public string AgregarConMetadatos(string dominio, string rutaRelativa, byte[] contenido, byte[] metadatos)
    {
        var id = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes($"{dominio}-{rutaRelativa}")));
        var ruta = carpeta[Path.Combine(id[..2], id)];
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllBytes(ruta, contenido);
        AgregarFila(id, dominio, rutaRelativa, metadatos);
        return ruta;
    }

    /// <summary>Agrega un archivo y devuelve su ruta dentro de la copia (la del hash).</summary>
    public string Agregar(string dominio, string rutaRelativa, byte[] contenido, DateTime? modificado = null,
                          DateTime? creado = null, bool enDisco = true)
    {
        var id = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes($"{dominio}-{rutaRelativa}")));
        var ruta = carpeta[Path.Combine(id[..2], id)];
        if (enDisco)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllBytes(ruta, contenido);
        }
        Ejecutar("INSERT INTO Files VALUES ($id, $dominio, $ruta, 1, $file)",
                 ("$id", id), ("$dominio", dominio), ("$ruta", rutaRelativa),
                 ("$file", EscritorPlist.MBFile(rutaRelativa, contenido.Length, modificado, creado)));
        return ruta;
    }

    public string Agregar(string dominio, string rutaRelativa, string contenido = "contenido", DateTime? modificado = null) =>
        Agregar(dominio, rutaRelativa, Encoding.UTF8.GetBytes(contenido), modificado);

    /// <summary>Una carpeta en el indice (flags = 2): no es un archivo y no se extrae.</summary>
    public void AgregarCarpeta(string dominio, string rutaRelativa) =>
        Ejecutar("INSERT INTO Files VALUES ($id, $dominio, $ruta, 2, NULL)",
                 ("$id", Guid.NewGuid().ToString("N")), ("$dominio", dominio), ("$ruta", rutaRelativa));

    void Ejecutar(string sql, params (string Nombre, object Valor)[] parametros)
    {
        using var db = new SqliteConnection(conexion);
        db.Open();
        using var comando = db.CreateCommand();
        comando.CommandText = sql;
        foreach (var (nombre, valor) in parametros) comando.Parameters.AddWithValue(nombre, valor);
        comando.ExecuteNonQuery();
    }

    public void Dispose() => carpeta.Dispose();
}
