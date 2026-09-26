using System.Globalization;

namespace PapaPersonas.Infrastructure.Import.Paso1;

public static class CsvEscaper
{
    public static string Escape(string? value)
    {
        var safe = value ?? string.Empty;
        var escaped = safe.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    public static string FormatCellValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}
