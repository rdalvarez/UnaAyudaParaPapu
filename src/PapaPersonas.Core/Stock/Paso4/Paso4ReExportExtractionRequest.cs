namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4ReExportExtractionRequest(
    string DatabasePath,
    Guid Token,
    string DestinationCsvPath,
    IReadOnlyList<string> SelectedColumns);
