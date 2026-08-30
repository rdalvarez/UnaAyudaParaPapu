namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4StockHeaderSummary(
    Guid StockId,
    DateOnly SourceFechaImportacion,
    Guid? SourceImportId,
    DateTime GeneratedUtc,
    bool HasPendingExtraction,
    bool IsStale,
    int TotalMembers,
    int SoldMembers,
    int AvailableMembers,
    int GroupCount);
