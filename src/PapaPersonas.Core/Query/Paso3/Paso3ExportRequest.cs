namespace PapaPersonas.Core.Query.Paso3;

public sealed record Paso3ExportRequest(
    Paso3PreviewRequest Query,
    string DestinationCsvPath);
