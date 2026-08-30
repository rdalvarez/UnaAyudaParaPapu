namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4CancelPendingExtractionResult(
    Paso4RecoveryStatus Status,
    Guid? Token,
    int ReleasedRows,
    string? FailureMessage)
{
    public bool IsSuccess => Status == Paso4RecoveryStatus.Completed;

    public static Paso4CancelPendingExtractionResult Completed(Guid token, int releasedRows) =>
        new(Paso4RecoveryStatus.Completed, token, releasedRows, null);

    public static Paso4CancelPendingExtractionResult Blocked(Guid token, string message) =>
        new(Paso4RecoveryStatus.Blocked, token, 0, message);

    public static Paso4CancelPendingExtractionResult ValidationFailed(string message) =>
        new(Paso4RecoveryStatus.ValidationFailed, null, 0, message);

    public static Paso4CancelPendingExtractionResult Failed(string message) =>
        new(Paso4RecoveryStatus.Failed, null, 0, message);
}
