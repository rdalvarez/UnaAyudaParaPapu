using PapaPersonas.Core.App;

namespace PapaPersonas.Tests.App;

public sealed class MainWindowDatabaseStateTests
{
    [Fact]
    public void SetReadyStatus_SplitsReadyAndConfiguredPaths()
    {
        var state = new MainWindowDatabaseState();

        Assert.Null(state.GetResolvedPath());
        Assert.Null(state.GetConfiguredPath());

        state.SetReadyStatus(isReady: true, databasePath: @"C:\data\PapaPersonas.duckdb");
        Assert.Equal(@"C:\data\PapaPersonas.duckdb", state.GetResolvedPath());
        Assert.Equal(@"C:\data\PapaPersonas.duckdb", state.GetConfiguredPath());

        state.SetReadyStatus(isReady: false, databasePath: @"C:\data\PapaPersonas.duckdb");
        Assert.Null(state.GetResolvedPath());
        Assert.Equal(@"C:\data\PapaPersonas.duckdb", state.GetConfiguredPath());
    }
}
