namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4GenerateStockResult(
    Paso4GenerateStockStatus Status,
    Guid? StockId,
    DateOnly? SourceFechaImportacion,
    Guid? SourceImportId,
    int MembersSnapshotted,
    int LegacyFallbackAssignedCount,
    string? FailureMessage)
{
    public bool IsSuccess => Status == Paso4GenerateStockStatus.Completed;

    public static Paso4GenerateStockResult Completed(
        Guid stockId,
        DateOnly sourceFechaImportacion,
        Guid? sourceImportId,
        int membersSnapshotted,
        int legacyFallbackAssignedCount) =>
        new(
            Paso4GenerateStockStatus.Completed,
            stockId,
            sourceFechaImportacion,
            sourceImportId,
            membersSnapshotted,
            legacyFallbackAssignedCount,
            null);

    public static Paso4GenerateStockResult ValidationFailed(string message) =>
        new(Paso4GenerateStockStatus.ValidationFailed, null, null, null, 0, 0, message);

    public static Paso4GenerateStockResult Failed(string message) =>
        new(Paso4GenerateStockStatus.Failed, null, null, null, 0, 0, message);
}
