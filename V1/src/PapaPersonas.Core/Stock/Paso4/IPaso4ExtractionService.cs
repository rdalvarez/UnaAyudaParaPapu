namespace PapaPersonas.Core.Stock.Paso4;

public interface IPaso4ExtractionService
{
    Paso4ExtractionPreflightResult PreflightExtraction(string databasePath, IReadOnlyList<Paso4ExtractionGroupQuantityRequest> groupRequests);

    Task<Paso4BeginExtractionResult> BeginExtractionAsync(Paso4BeginExtractionRequest request, CancellationToken cancellationToken);

    Paso4PendingExtractionState? GetPendingState(string databasePath);

    Task<Paso4RetryPendingExtractionResult> RetryPendingExtractionAsync(string databasePath, CancellationToken cancellationToken);

    Paso4CancelPendingExtractionResult CancelPendingExtraction(string databasePath);

    IReadOnlyList<Paso4CompletedExtractionOption> ListCompletedExtractions(string databasePath);

    Task<Paso4ReExportExtractionResult> ReExportCompletedAsync(Paso4ReExportExtractionRequest request, CancellationToken cancellationToken);
}
