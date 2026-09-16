using System.Text;
using PapaPersonas.Core.Import;

namespace PapaPersonas.Core.Import.Paso2;

public sealed record SergioValidHeaderReferenceRow(
    string EncabezadoValido,
    string CampoDestino,
    string Tipo,
    string Obligatoria);

public static class SergioValidHeadersReferenceCsv
{
    public const string SuggestedFileName = "columnas_validas_paso2.csv";

    public static IReadOnlyList<string> CsvHeaders { get; } =
    [
        "ENCABEZADO_VALIDO",
        "CAMPO_DESTINO",
        "TIPO",
        "OBLIGATORIA"
    ];

    /// <summary>Construye una fila por cada encabezado aceptado de Sergio, sin duplicar el contrato.</summary>
    public static IReadOnlyList<SergioValidHeaderReferenceRow> BuildRows()
    {
        var map = HeaderContracts.GetHeaderToCanonicalMap(ImportStage.SergioReturn);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<SergioValidHeaderReferenceRow>(map.Count);

        foreach (var templateHeader in HeaderContracts.SergioTemplateHeaders36)
        {
            if (!map.TryGetValue(templateHeader, out var canonical))
            {
                throw new InvalidOperationException(
                    $"El encabezado de plantilla de Sergio '{templateHeader}' no está en el mapa aceptado.");
            }

            rows.Add(CreateRow(templateHeader, canonical, tipo: "Principal"));
            consumed.Add(templateHeader);
        }

        var aliases = map.Keys
            .Where(header => !consumed.Contains(header))
            .OrderBy(header => header, StringComparer.Ordinal);

        foreach (var alias in aliases)
        {
            rows.Add(CreateRow(alias, map[alias], tipo: "Alias"));
        }

        return rows;
    }

    public static string FormatCsv(IReadOnlyList<SergioValidHeaderReferenceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendLine(builder, CsvHeaders);
        foreach (var row in rows)
        {
            AppendLine(builder, [row.EncabezadoValido, row.CampoDestino, row.Tipo, row.Obligatoria]);
        }

        return builder.ToString();
    }

    public static void Write(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("La ruta del CSV de destino debe incluir una carpeta.", nameof(destinationPath));
        }

        Directory.CreateDirectory(destinationDirectory);

        var csv = FormatCsv(BuildRows());
        var tempPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static SergioValidHeaderReferenceRow CreateRow(string sourceHeader, string canonical, string tipo)
    {
        var obligatoria = string.Equals(canonical, "cuil", StringComparison.Ordinal) ? "SI" : "NO";
        return new SergioValidHeaderReferenceRow(sourceHeader, canonical, tipo, obligatoria);
    }

    private static void AppendLine(StringBuilder builder, IReadOnlyList<string> values)
    {
        builder.Append(string.Join(',', values.Select(Escape)));
        builder.AppendLine();
    }

    internal static string Escape(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return safe;
        }

        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of the unpublished temp file.
        }
    }
}
