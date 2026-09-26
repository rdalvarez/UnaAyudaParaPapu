namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4BeginExtractionResult(
    Paso4BeginExtractionStatus Status,
    Guid? Token,
    int ExpectedRows,
    int ExportedRows,
    string? OutputPath,
    string? FailureMessage)
{
    public bool IsSuccess => Status == Paso4BeginExtractionStatus.Completed;

    public static Paso4BeginExtractionResult Completed(Guid token, int expectedRows, int exportedRows, string outputPath) =>
        new(Paso4BeginExtractionStatus.Completed, token, expectedRows, exportedRows, outputPath, null);

    public static Paso4BeginExtractionResult ValidationFailed(string message) =>
        new(Paso4BeginExtractionStatus.ValidationFailed, null, 0, 0, null, message);

    public static Paso4BeginExtractionResult Failed(string message) =>
        new(Paso4BeginExtractionStatus.Failed, null, 0, 0, null, message);
}
