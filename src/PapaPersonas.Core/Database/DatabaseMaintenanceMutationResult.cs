namespace PapaPersonas.Core.Database;

public sealed record DatabaseMaintenanceMutationResult(
    bool IsSuccess,
    string? LiveDatabasePath,
    string? SafetyBackupPath,
    string? Message,
    bool PreservedRawDatabase,
    DatabaseMaintenanceFailureCode FailureCode = DatabaseMaintenanceFailureCode.None)
{
    public static DatabaseMaintenanceMutationResult Completed(
        string liveDatabasePath,
        string safetyBackupPath,
        bool preservedRawDatabase,
        string? message = null) =>
        new(true, liveDatabasePath, safetyBackupPath, message, preservedRawDatabase, DatabaseMaintenanceFailureCode.None);

    public static DatabaseMaintenanceMutationResult Failed(
        string message,
        string? safetyBackupPath = null,
        DatabaseMaintenanceFailureCode failureCode = DatabaseMaintenanceFailureCode.OperationFailed) =>
        new(false, null, safetyBackupPath, message, PreservedRawDatabase: false, failureCode);
}
