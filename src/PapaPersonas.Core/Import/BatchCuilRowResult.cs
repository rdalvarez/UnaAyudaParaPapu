namespace PapaPersonas.Core.Import;

public sealed record BatchCuilRowResult(
    int SourceRowNumber,
    string? RawCuil,
    string? NormalizedCuil,
    bool IsValid,
    bool IsDuplicate,
    ValidationIssue? Issue);
