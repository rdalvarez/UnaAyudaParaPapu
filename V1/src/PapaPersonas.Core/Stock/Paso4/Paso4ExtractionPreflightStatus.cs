namespace PapaPersonas.Core.Stock.Paso4;

public enum Paso4ExtractionPreflightStatus
{
    Ready = 0,
    NoStock = 1,
    Pending = 2,
    InvalidRequest = 3,
    GroupNotFound = 4,
    InsufficientStock = 5,
    Failed = 6
}
