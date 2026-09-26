namespace PapaPersonas.Core.Import;

public sealed record ValidationIssue(
    ValidationErrorCode Code,
    string Message,
    int? SourceRowNumber = null,
    string? Header = null,
    string? CanonicalField = null,
    string? RawValue = null);
