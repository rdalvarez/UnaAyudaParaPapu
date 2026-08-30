namespace PapaPersonas.Core.Database;

public static class DatabaseMaintenanceNaming
{
    public const string SuggestedBackupFileNameFallback = "SinImportaciones_BaseMaestra_BK.duckdb";

    public static string BuildSuggestedBackupFileName(DateOnly? latestImportDate)
    {
        return latestImportDate.HasValue
            ? $"{latestImportDate.Value:yyyy-MM-dd}_BaseMaestra_BK.duckdb"
            : SuggestedBackupFileNameFallback;
    }

    public static string BuildSafetyBackupRootDirectory(string localAppDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(localAppDataDirectory))
        {
            throw new ArgumentException("La carpeta de datos locales de la aplicación es obligatoria.", nameof(localAppDataDirectory));
        }

        return Path.Combine(localAppDataDirectory, "PapaPersonas", "backups");
    }
}
