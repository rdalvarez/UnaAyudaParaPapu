namespace PapaPersonas.Core.Database;

public sealed record DatabaseMaintenanceBackupResult(
    bool IsSuccess,
    string? OutputPath,
    string? Message,
    DateOnly? LatestImportDate)
{
    public static DatabaseMaintenanceBackupResult Completed(string outputPath, DateOnly? latestImportDate) =>
        new(true, outputPath, null, latestImportDate);

    public static DatabaseMaintenanceBackupResult Failed(string message) =>
        new(false, null, message, null);
}
