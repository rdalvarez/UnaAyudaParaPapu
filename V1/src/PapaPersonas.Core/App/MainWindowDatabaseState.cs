namespace PapaPersonas.Core.App;

public sealed class MainWindowDatabaseState
{
    private string? _configuredDatabasePath;
    private string? _readyDatabasePath;

    public void SetReadyStatus(bool isReady, string databasePath)
    {
        _configuredDatabasePath = databasePath;
        _readyDatabasePath = isReady ? databasePath : null;
    }

    public string? GetResolvedPath() => _readyDatabasePath;

    public string? GetConfiguredPath() => _configuredDatabasePath;
}
