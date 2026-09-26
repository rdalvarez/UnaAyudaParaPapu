namespace PapaPersonas.Core.Import.Paso2;

public sealed record SergioApplySummary(
    int AppliedRows,
    int InsertedRows,
    int UpdatedRows,
    int RejectedRowsExported);
