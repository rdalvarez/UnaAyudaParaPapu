namespace PapaPersonas.Core.Query.Paso3;

public sealed record Paso3ExportResult(
    Paso3ExportStatus Status,
    int RowsWritten,
    string? OutputPath,
    string? FailureMessage)
{
    public bool IsSuccess => Status == Paso3ExportStatus.Completed;

    public static Paso3ExportResult Completed(int rowsWritten, string outputPath) =>
        new(Paso3ExportStatus.Completed, rowsWritten, outputPath, null);

    public static Paso3ExportResult Canceled(int rowsWritten) =>
        new(Paso3ExportStatus.Canceled, rowsWritten, null, null);

    public static Paso3ExportResult Failed(int rowsWritten, string failureMessage) =>
        new(Paso3ExportStatus.Failed, rowsWritten, null, failureMessage);
}
