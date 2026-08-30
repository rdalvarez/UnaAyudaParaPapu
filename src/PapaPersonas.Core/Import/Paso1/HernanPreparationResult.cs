namespace PapaPersonas.Core.Import.Paso1;

public sealed record HernanPreparationResult(
    bool IsSuccess,
    bool IsValidationFailure,
    string? SergioCsvPath,
    string? RejectedCsvPath,
    IReadOnlyList<HernanOutputColumnMapping> OutputColumns,
    BatchValidationSummary Summary,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Notices,
    IReadOnlyCollection<string> PresentCanonicalFields)
{
    public static HernanPreparationResult ValidationFailure(
        IReadOnlyList<ValidationIssue> errors,
        IReadOnlyList<ValidationIssue> notices,
        IReadOnlyCollection<string> presentCanonicalFields,
        IReadOnlyList<HernanOutputColumnMapping>? outputColumns = null) =>
        new(
            false,
            true,
            null,
            null,
            outputColumns ?? [],
            new BatchValidationSummary(0, 0, 0, 0, 0, 0),
            errors,
            notices,
            presentCanonicalFields);

    public static HernanPreparationResult Success(
        string sergioCsvPath,
        string rejectedCsvPath,
        IReadOnlyList<HernanOutputColumnMapping> outputColumns,
        BatchValidationSummary summary,
        IReadOnlyList<ValidationIssue> notices,
        IReadOnlyCollection<string> presentCanonicalFields) =>
        new(
            true,
            false,
            sergioCsvPath,
            rejectedCsvPath,
            outputColumns,
            summary,
            [],
            notices,
            presentCanonicalFields);

    public static HernanPreparationResult Failed(
        ValidationIssue error,
        IReadOnlyList<ValidationIssue> notices,
        IReadOnlyCollection<string> presentCanonicalFields,
        IReadOnlyList<HernanOutputColumnMapping>? outputColumns = null) =>
        new(
            false,
            false,
            null,
            null,
            outputColumns ?? [],
            new BatchValidationSummary(0, 0, 0, 0, 0, 0),
            [error],
            notices,
            presentCanonicalFields);
}
