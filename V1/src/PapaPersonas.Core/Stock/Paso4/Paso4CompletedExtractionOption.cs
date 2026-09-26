namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4CompletedExtractionOption(
    Guid Token,
    DateTime FechaVentaUtc,
    int RowCount);
