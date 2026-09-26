namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4SummaryExportRequest(
    string DatabasePath,
    string DestinationCsvPath);
