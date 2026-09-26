namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4ReExportExtractionResult(
    Paso4RecoveryStatus Status,
    Guid? Token,
    int RowsWritten,
    string? OutputPath,
    string? FailureMessage)
{
    public bool IsSuccess => Status == Paso4RecoveryStatus.Completed;

    public static Paso4ReExportExtractionResult Completed(Guid token, int rowsWritten, string outputPath) =>
        new(Paso4RecoveryStatus.Completed, token, rowsWritten, outputPath, null);

    public static Paso4ReExportExtractionResult ValidationFailed(string message) =>
        new(Paso4RecoveryStatus.ValidationFailed, null, 0, null, message);

    public static Paso4ReExportExtractionResult Failed(string message) =>
        new(Paso4RecoveryStatus.Failed, null, 0, null, message);
}
