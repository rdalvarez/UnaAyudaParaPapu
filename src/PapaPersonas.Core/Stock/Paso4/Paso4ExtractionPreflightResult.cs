namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4ExtractionPreflightResult(
    Paso4ExtractionPreflightStatus Status,
    string? NormalizedCodigoObraSocial,
    string? NormalizedObraSocial,
    int RequestedQuantity,
    int AvailableQuantity,
    string? TechnicalMessage)
{
    public bool IsReady => Status == Paso4ExtractionPreflightStatus.Ready;

    public static Paso4ExtractionPreflightResult Ready() =>
        new(Paso4ExtractionPreflightStatus.Ready, null, null, 0, 0, null);

    public static Paso4ExtractionPreflightResult NoStock(string? technicalMessage = null) =>
        new(Paso4ExtractionPreflightStatus.NoStock, null, null, 0, 0, technicalMessage);

    public static Paso4ExtractionPreflightResult Pending(string? technicalMessage = null) =>
        new(Paso4ExtractionPreflightStatus.Pending, null, null, 0, 0, technicalMessage);

    public static Paso4ExtractionPreflightResult InvalidRequest(string? technicalMessage = null) =>
        new(Paso4ExtractionPreflightStatus.InvalidRequest, null, null, 0, 0, technicalMessage);

    public static Paso4ExtractionPreflightResult GroupNotFound(string codigo, string obra, int requestedQuantity) =>
        new(Paso4ExtractionPreflightStatus.GroupNotFound, codigo, obra, requestedQuantity, 0, null);

    public static Paso4ExtractionPreflightResult InsufficientStock(string codigo, string obra, int requestedQuantity, int availableQuantity) =>
        new(Paso4ExtractionPreflightStatus.InsufficientStock, codigo, obra, requestedQuantity, availableQuantity, null);

    public static Paso4ExtractionPreflightResult Failed(string? technicalMessage = null) =>
        new(Paso4ExtractionPreflightStatus.Failed, null, null, 0, 0, technicalMessage);
}
