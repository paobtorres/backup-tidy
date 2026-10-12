using System.Text;

namespace BackupTidy.Core.Plan;

/// <summary>
/// CSV para abrir en Excel en espanol: separador ';', UTF-8 con BOM y todos los campos entre comillas.
/// Al leer acepta ';' o ',' (se detecta en la primera linea) y campos con comillas o saltos de linea.
/// </summary>
static class Csv
{
    public const char Separador = ';';
    static readonly Encoding Utf8ConBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static StreamWriter AbrirEscritura(string ruta) =>
        new(ruta, append: false, Utf8ConBom) { AutoFlush = true };

    public static void EscribirLinea(TextWriter salida, params string?[] campos) =>
        salida.WriteLine(string.Join(Separador, campos.Select(c => '"' + (c ?? "").Replace("\"", "\"\"") + '"')));

    /// <summary>Filas como diccionarios por nombre de columna (la primera linea es la cabecera).</summary>
    public static List<Dictionary<string, string>> Leer(string ruta)
    {
        var texto = File.ReadAllText(ruta, Encoding.UTF8);
        var finCabecera = texto.IndexOf('\n');
        var cabecera = finCabecera < 0 ? texto : texto[..finCabecera];
        var separador = cabecera.Contains(';') ? ';' : ',';

        var registros = Separar(texto, separador);
        if (registros.Count == 0) return [];
        var columnas = registros[0];
        return registros.Skip(1)
                        .Where(r => r.Count > 1 || r[0] != "")
                        .Select(r => columnas.Select((c, i) => (c, v: i < r.Count ? r[i] : ""))
                                             .ToDictionary(x => x.c, x => x.v, StringComparer.OrdinalIgnoreCase))
                        .ToList();
    }

    static List<List<string>> Separar(string texto, char separador)
    {
        var registros = new List<List<string>>();
        var actual = new List<string>();
        var campo = new StringBuilder();
        var entreComillas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreComillas)
            {
                if (c != '"') campo.Append(c);
                else if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                else entreComillas = false;
            }
            else if (c == '"') entreComillas = true;
            else if (c == separador) { actual.Add(campo.ToString()); campo.Clear(); }
            else if (c == '\n')
            {
                actual.Add(campo.ToString()); campo.Clear();
                registros.Add(actual); actual = [];
            }
            else if (c != '\r') campo.Append(c);
        }
        if (campo.Length > 0 || actual.Count > 0)
        {
            actual.Add(campo.ToString());
            registros.Add(actual);
        }
        return registros;
    }
}
