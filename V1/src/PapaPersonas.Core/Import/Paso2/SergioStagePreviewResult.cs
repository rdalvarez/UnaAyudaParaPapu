using PapaPersonas.Core.Import;

namespace PapaPersonas.Core.Import.Paso2;

public sealed record SergioStagePreviewResult(
    bool IsSuccess,
    SergioStagePreviewStatus Status,
    Guid? ImportId,
    SergioStagePreviewSummary Summary,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Notices,
    IReadOnlyCollection<string> PresentCanonicalFields,
    IReadOnlyList<string> UnknownSourceHeaders,
    string? SourceShapeFingerprint,
    SergioSourceIdentity? SourceIdentity,
    string? SourceSnapshotPath,
    string? FailureMessage,
    TimeSpan Elapsed)
{
    public static SergioStagePreviewResult ValidationFailed(
        IReadOnlyList<ValidationIssue> errors,
        IReadOnlyList<ValidationIssue> notices,
        IReadOnlyCollection<string> presentCanonicalFields,
        TimeSpan elapsed) =>
        new(
            false,
            SergioStagePreviewStatus.ValidationFailed,
            null,
            new SergioStagePreviewSummary(0, 0, 0, 0, 0, 0, 0, 0, 0),
            errors,
            notices,
            presentCanonicalFields,
            [],
            null,
            null,
            null,
            "La validación falló.",
            elapsed);

    public static SergioStagePreviewResult ConfirmationRequired(
        IReadOnlyList<ValidationIssue> issues,
        IReadOnlyList<ValidationIssue> notices,
        IReadOnlyCollection<string> presentCanonicalFields,
        IReadOnlyList<string> unknownSourceHeaders,
        string sourceShapeFingerprint,
        SergioSourceIdentity sourceIdentity,
        string sourceSnapshotPath,
        TimeSpan elapsed) =>
        new(
            false,
            SergioStagePreviewStatus.ConfirmationRequired,
            null,
            new SergioStagePreviewSummary(0, 0, 0, 0, 0, 0, 0, 0, 0),
            issues,
            notices,
            presentCanonicalFields,
            unknownSourceHeaders,
            sourceShapeFingerprint,
            sourceIdentity,
            sourceSnapshotPath,
            "Se requiere confirmación para ignorar columnas desconocidas.",
            elapsed);

    public static SergioStagePreviewResult Success(
        Guid importId,
        SergioStagePreviewSummary summary,
        IReadOnlyList<ValidationIssue> notices,
        IReadOnlyCollection<string> presentCanonicalFields,
        TimeSpan elapsed,
        IReadOnlyList<string>? unknownSourceHeaders = null,
        string? sourceShapeFingerprint = null) =>
        new(
            true,
            SergioStagePreviewStatus.ReadyForConfirmation,
            importId,
            summary,
            [],
            notices,
            presentCanonicalFields,
            unknownSourceHeaders ?? [],
            sourceShapeFingerprint,
            null,
            null,
            null,
            elapsed);

    public static SergioStagePreviewResult Failed(
        string failureMessage,
        TimeSpan elapsed,
        Guid? importId = null,
        IReadOnlyList<ValidationIssue>? notices = null,
        IReadOnlyCollection<string>? presentCanonicalFields = null) =>
        new(
            false,
            SergioStagePreviewStatus.Failed,
            importId,
            new SergioStagePreviewSummary(0, 0, 0, 0, 0, 0, 0, 0, 0),
            [],
            notices ?? [],
            presentCanonicalFields ?? [],
            [],
            null,
            null,
            null,
            failureMessage,
            elapsed);
}
