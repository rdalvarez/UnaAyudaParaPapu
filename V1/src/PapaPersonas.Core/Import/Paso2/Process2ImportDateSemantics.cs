using System.Globalization;
using System.Text.RegularExpressions;

namespace PapaPersonas.Core.Import.Paso2;

public static partial class Process2ImportDateSemantics
{
    public const string OldDateWarningMessage = "Después de aplicar un archivo antiguo, más adelante deberás cargar uno más nuevo para recuperar la actualidad.";

    /// <summary>Extrae una única fecha acotada del nombre de archivo para evitar prefijos o sufijos ambiguos.</summary>
    public static bool TryExtractSingleBoundedFilenameDateToken(string filePath, out DateOnly importDate)
    {
        importDate = default;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var matches = FileNameDateTokenRegex().Matches(fileName);
        if (matches.Count != 1)
        {
            return false;
        }

        var token = matches[0].Value;
        return DateOnly.TryParseExact(token, "dd_MM_yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out importDate);
    }

    /// <summary>Convierte las partes de fecha en una fecha válida sin aceptar valores incompletos o fuera de rango.</summary>
    public static bool TryParseImportDateFromParts(string dayText, string monthText, string yearText, out DateOnly importDate)
    {
        importDate = default;

        if (!int.TryParse(dayText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || !int.TryParse(monthText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(yearText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return false;
        }

        if (year < 1 || month is < 1 or > 12 || day < 1)
        {
            return false;
        }

        try
        {
            importDate = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static bool IsFutureDate(DateOnly importDate, DateOnly today)
    {
        return importDate > today;
    }

    public static bool IsOlderThanLatestApplied(DateOnly importDate, DateOnly latestAppliedDate)
    {
        return importDate < latestAppliedDate;
    }

    [GeneratedRegex(@"(?<!\d)\d{2}_\d{2}_\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex FileNameDateTokenRegex();
}
