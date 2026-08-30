namespace PapaPersonas.Core.Database;

public sealed record DatabaseBootstrapResult(bool IsSuccess, string DatabasePath, string Message)
{
    public DatabaseBootstrapFailureCode FailureCode { get; init; } =
        IsSuccess ? DatabaseBootstrapFailureCode.None : DatabaseBootstrapFailureCode.Other;

    public static DatabaseBootstrapResult Success(string databasePath) =>
        new(true, databasePath, "La base de datos está lista.")
        {
            FailureCode = DatabaseBootstrapFailureCode.None
        };

    public static DatabaseBootstrapResult Failure(string databasePath, string message) =>
        new(false, databasePath, message);

    public static DatabaseBootstrapResult Failure(
        string databasePath,
        string message,
        DatabaseBootstrapFailureCode failureCode) =>
        new(false, databasePath, message)
        {
            FailureCode = failureCode
        };
}
