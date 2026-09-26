namespace PapaPersonas.Core.Database;

public interface IDatabaseMaintenanceService
{
    DateOnly? GetLatestImportDate(string databasePath);

    DatabaseMaintenanceBackupResult BackupCurrentDatabase(string databasePath, string destinationPath);

    DatabaseMaintenanceMutationResult RestoreDatabase(string liveDatabasePath, string backupSourcePath, string safetyBackupRootDirectory);

    DatabaseMaintenanceMutationResult ResetDatabase(string liveDatabasePath, string safetyBackupRootDirectory);
}
