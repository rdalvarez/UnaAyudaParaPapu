namespace PapaPersonas.Core.Import.Paso2;

/// <summary>
/// Process 2B apply request.
///
/// `ImportId` must reference a pre-analyzed Sergio run in ready_for_confirmation state.
/// </summary>
public sealed record SergioApplyRequest(
    string DatabasePath,
    Guid ImportId,
    string? RejectedCsvOutputPath = null,
    string? RejectedCsvOutputDirectory = null);
