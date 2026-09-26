namespace PapaPersonas.Core.Import.Paso2;

public sealed record SergioStagePreviewRequest(
    string SergioXlsxPath,
    string DatabasePath,
    DateOnly ImportDate,
    bool AllowUnknownHeaders = false,
    string? ExpectedSourceShapeFingerprint = null,
    SergioSourceIdentity? ExpectedSourceIdentity = null,
    string? SourceSnapshotPath = null);
