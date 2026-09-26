namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4StockOverview(
    IReadOnlyList<Paso4DateOption> AvailableDates,
    Paso4StockHeaderSummary? CurrentStock,
    DateOnly? LatestSuccessfulImportDate,
    Guid? LatestSuccessfulImportId);
