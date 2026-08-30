namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4BeginExtractionRequest(
    string DatabasePath,
    string DestinationCsvPath,
    IReadOnlyList<string> SelectedColumns,
    IReadOnlyList<Paso4ExtractionGroupQuantityRequest> GroupRequests);
