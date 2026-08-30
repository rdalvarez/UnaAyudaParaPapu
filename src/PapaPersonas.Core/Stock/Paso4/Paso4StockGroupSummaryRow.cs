namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4StockGroupSummaryRow(
    int Orden,
    string RawCodigoObraSocial,
    string RawObraSocial,
    string NormalizedCodigoObraSocial,
    string NormalizedObraSocial,
    int Total,
    int Sold,
    int Available);
