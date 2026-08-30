namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4FullExportRequest(
    string DatabasePath,
    string DestinationCsvPath,
    bool OnlyAvailable,
    IReadOnlyList<string> SelectedColumns);
