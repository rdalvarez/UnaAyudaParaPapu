namespace PapaPersonas.Core.Stock.Paso4;

public interface IPaso4StockService
{
    Paso4StockOverview GetOverview(string databasePath);

    Paso4GenerateStockResult GenerateOrRegenerateStock(Paso4GenerateStockRequest request);

    Paso4StockSummaryResult GetCurrentStockSummary(string databasePath);

    Task<Paso4SummaryExportResult> ExportSummaryCsvAsync(Paso4SummaryExportRequest request, CancellationToken cancellationToken);

    Task<Paso4FullExportResult> ExportFullCsvAsync(Paso4FullExportRequest request, CancellationToken cancellationToken);
}
