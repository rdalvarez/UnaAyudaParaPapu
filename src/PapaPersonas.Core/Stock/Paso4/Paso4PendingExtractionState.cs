namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4PendingExtractionState(
    Guid StockId,
    Guid Token,
    DateTime StartedUtc,
    string OutputPath,
    int ExpectedRows,
    string SelectedColumnsJson,
    int ReservedRows);
