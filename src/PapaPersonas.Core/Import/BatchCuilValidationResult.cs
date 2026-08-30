namespace PapaPersonas.Core.Import;

public sealed record BatchCuilValidationResult(
    IReadOnlyList<BatchCuilRowResult> Rows,
    BatchValidationSummary Summary);
