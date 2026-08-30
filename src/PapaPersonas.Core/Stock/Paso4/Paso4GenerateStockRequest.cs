namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4GenerateStockRequest(
    string DatabasePath,
    DateOnly SelectedFechaImportacion);
