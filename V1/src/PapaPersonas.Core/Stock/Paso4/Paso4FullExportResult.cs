namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4FullExportResult(
    bool IsSuccess,
    int RowsWritten,
    string? OutputPath,
    string? FailureMessage)
{
    public static Paso4FullExportResult Completed(int rowsWritten, string outputPath) =>
        new(true, rowsWritten, outputPath, null);

    public static Paso4FullExportResult Failed(string message) =>
        new(false, 0, null, message);
}
