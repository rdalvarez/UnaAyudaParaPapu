namespace PapaPersonas.Core.Import.Paso2;

/// <summary>
/// Represents a single Sergio workbook row mapped to canonical field names.
///
/// Invariant: values are raw textual payloads as read from Excel (after basic cell formatting);
/// conversion to typed DB values happens later in Process 2A so validation policy stays centralized.
/// </summary>
public sealed record SergioRowRecord(
    int SourceRowNumber,
    IReadOnlyDictionary<string, string?> CanonicalValues)
{
    public string? RawCuil => GetValue("cuil");

    public string? GetValue(string canonicalField)
    {
        return CanonicalValues.TryGetValue(canonicalField, out var value)
            ? value
            : null;
    }
}
