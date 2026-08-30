using PapaPersonas.Core.Import;

namespace PapaPersonas.Core.Import.Paso2;

public sealed record SergioApplyResult(
    bool IsSuccess,
    SergioApplyStatus Status,
    Guid ImportId,
    SergioApplySummary Summary,
    string? RejectedCsvPath,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Notices,
    string? FailureMessage,
    TimeSpan Elapsed)
{
    public static SergioApplyResult ValidationFailed(
        Guid importId,
        IReadOnlyList<ValidationIssue> errors,
        string failureMessage,
        TimeSpan elapsed) =>
        new(
            false,
            SergioApplyStatus.ValidationFailed,
            importId,
            new SergioApplySummary(0, 0, 0, 0),
            null,
            errors,
            [],
            failureMessage,
            elapsed);

    public static SergioApplyResult Failed(
        Guid importId,
        string failureMessage,
        TimeSpan elapsed,
        IReadOnlyList<ValidationIssue>? notices = null) =>
        new(
            false,
            SergioApplyStatus.Failed,
            importId,
            new SergioApplySummary(0, 0, 0, 0),
            null,
            [],
            notices ?? [],
            failureMessage,
            elapsed);

    public static SergioApplyResult Success(
        Guid importId,
        SergioApplySummary summary,
        string? rejectedCsvPath,
        IReadOnlyList<ValidationIssue> notices,
        TimeSpan elapsed) =>
        new(
            true,
            SergioApplyStatus.Completed,
            importId,
            summary,
            rejectedCsvPath,
            [],
            notices,
            null,
            elapsed);
}
