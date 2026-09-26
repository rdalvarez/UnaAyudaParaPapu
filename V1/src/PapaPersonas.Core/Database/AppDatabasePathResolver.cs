namespace PapaPersonas.Core.Database;

public sealed record AppDatabasePathResolution(string DatabasePath, bool IsOverrideApplied);

public static class AppDatabasePathResolver
{
    private const string DefaultDatabaseFileName = "PapaPersonas.duckdb";

    /// <summary>Resuelve la ruta de la base aplicando la configuración opcional o la ubicación predeterminada.</summary>
    public static AppDatabasePathResolution Resolve(
        string localAppDataDirectory,
        string? overridePathRaw,
        string currentDirectory)
    {
        var trimmedOverride = overridePathRaw?.Trim();

        if (!string.IsNullOrWhiteSpace(trimmedOverride))
        {
            var fullOverridePath = Path.GetFullPath(trimmedOverride, currentDirectory);
            return new AppDatabasePathResolution(fullOverridePath, IsOverrideApplied: true);
        }

        var defaultPath = Path.Combine(localAppDataDirectory, "PapaPersonas", "data", DefaultDatabaseFileName);
        return new AppDatabasePathResolution(defaultPath, IsOverrideApplied: false);
    }
}
