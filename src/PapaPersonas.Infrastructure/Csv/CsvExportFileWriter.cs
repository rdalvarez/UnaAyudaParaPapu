using System.Globalization;
using System.Text;

namespace PapaPersonas.Infrastructure.Csv;

internal static class CsvExportFileWriter
{
    /// <summary>Escribe un CSV completo en un temporal y lo publica sólo cuando finaliza sin errores.</summary>
    public static async Task<int> WriteCsvAsync(
        string destinationPath,
        IReadOnlyList<string> headers,
        Func<StreamWriter, CancellationToken, Task<int>> writeRows,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(writeRows);

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("La ruta del CSV de destino debe incluir una carpeta.", nameof(destinationPath));
        }

        Directory.CreateDirectory(destinationDirectory);

        var tempPath = Path.Combine(destinationDirectory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            int rows;
            await using (var writer = new StreamWriter(tempPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                await WriteRowAsync(writer, headers, cancellationToken);
                rows = await writeRows(writer, cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            return rows;
        }
        catch
        {
            DeleteTempIfExists(tempPath);
            throw;
        }
    }

    public static Task WriteRowAsync(StreamWriter writer, IReadOnlyList<string?> values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var escaped = values.Select(Escape).ToArray();
        return writer.WriteLineAsync(string.Join(',', escaped));
    }

    public static string FormatValue(object? value)
    {
        if (value is null || value is DBNull)
        {
            return string.Empty;
        }

        return value switch
        {
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    public static string Escape(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return safe;
        }

        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void DeleteTempIfExists(string tempPath)
    {
        if (string.IsNullOrWhiteSpace(tempPath) || !File.Exists(tempPath))
        {
            return;
        }

        try
        {
            File.Delete(tempPath);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
