namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4RetryPendingExtractionResult(
    Paso4RecoveryStatus Status,
    Guid? Token,
    int ExportedRows,
    string? OutputPath,
    string? FailureMessage)
{
    public bool IsSuccess => Status == Paso4RecoveryStatus.Completed;

    public static Paso4RetryPendingExtractionResult Completed(Guid token, int exportedRows, string outputPath) =>
        new(Paso4RecoveryStatus.Completed, token, exportedRows, outputPath, null);

    public static Paso4RetryPendingExtractionResult ValidationFailed(string message) =>
        new(Paso4RecoveryStatus.ValidationFailed, null, 0, null, message);

    public static Paso4RetryPendingExtractionResult Failed(string message) =>
        new(Paso4RecoveryStatus.Failed, null, 0, null, message);
}
