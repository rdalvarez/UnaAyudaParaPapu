namespace PapaPersonas.Core.Import;

public sealed record HeaderValidationResult(
    bool IsValid,
    ImportStage Stage,
    IReadOnlyDictionary<string, string> SourceToCanonical,
    IReadOnlyCollection<string> PresentCanonicalFields,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<ValidationIssue> Notices,
    IReadOnlyList<string> UnknownSourceHeaders);
