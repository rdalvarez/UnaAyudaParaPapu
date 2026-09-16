namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4StockSummaryResult(
    Paso4StockHeaderSummary Header,
    IReadOnlyList<Paso4StockGroupSummaryRow> Groups,
    IReadOnlyList<string> CatalogWarnings);
