namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4ExtractionGroupQuantityRequest(
    string NormalizedCodigoObraSocial,
    string NormalizedObraSocial,
    int Quantity);
