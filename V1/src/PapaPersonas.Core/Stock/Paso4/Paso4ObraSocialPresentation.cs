namespace PapaPersonas.Core.Stock.Paso4;

public static class Paso4ObraSocialPresentation
{
    public const string EmptyDisplayName = "(Vacío)";

    public static string NormalizeCode(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    public static string ResolvePresentationName(string? catalogName, string? candidateName)
    {
        if (!string.IsNullOrWhiteSpace(catalogName))
        {
            return catalogName.Trim();
        }

        return (candidateName ?? string.Empty).Trim();
    }

    public static string ToEditableDisplayName(string? presentationName)
        => Paso4ProcessUiLogic.ToDisplayValue(presentationName);

    public static string? ParseEditableDisplayName(string? displayedName)
    {
        if (string.IsNullOrWhiteSpace(displayedName)
            || string.Equals(displayedName.Trim(), EmptyDisplayName, StringComparison.Ordinal))
        {
            return null;
        }

        return displayedName.Trim();
    }

    public static IReadOnlyDictionary<string, string> MergeCatalog(
        IReadOnlyDictionary<string, string> existingNames,
        IEnumerable<(string NormalizedCode, string DisplayedName)> visibleRows)
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentNullException.ThrowIfNull(visibleRows);

        var merged = new Dictionary<string, string>(existingNames, StringComparer.Ordinal);
        foreach (var (normalizedCode, displayedName) in visibleRows)
        {
            var code = NormalizeCode(normalizedCode);
            var parsed = ParseEditableDisplayName(displayedName);
            if (parsed is null)
            {
                merged.Remove(code);
                continue;
            }

            merged[code] = parsed;
        }

        return merged;
    }
}
