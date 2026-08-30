using PapaPersonas.Core.Database;

namespace PapaPersonas.Tests.Database;

public sealed class AppDatabasePathResolverTests
{
    [Fact]
    public void Resolve_WithoutOverride_UsesDefaultLocalAppDataPath()
    {
        var result = AppDatabasePathResolver.Resolve(
            localAppDataDirectory: @"C:\Users\dad\AppData\Local",
            overridePathRaw: null,
            currentDirectory: @"C:\repo\PapaPersonas");

        Assert.False(result.IsOverrideApplied);
        Assert.Equal(@"C:\Users\dad\AppData\Local\PapaPersonas\data\PapaPersonas.duckdb", result.DatabasePath);
    }

    [Fact]
    public void Resolve_WithWhitespaceOverride_FallsBackToDefault()
    {
        var result = AppDatabasePathResolver.Resolve(
            localAppDataDirectory: @"C:\Users\dad\AppData\Local",
            overridePathRaw: "   ",
            currentDirectory: @"C:\repo\PapaPersonas");

        Assert.False(result.IsOverrideApplied);
        Assert.Equal(@"C:\Users\dad\AppData\Local\PapaPersonas\data\PapaPersonas.duckdb", result.DatabasePath);
    }

    [Fact]
    public void Resolve_WithAbsoluteOverride_UsesNormalizedFullPath()
    {
        var result = AppDatabasePathResolver.Resolve(
            localAppDataDirectory: @"C:\Users\dad\AppData\Local",
            overridePathRaw: @"C:\temp\smoke\..\smoke\run.duckdb",
            currentDirectory: @"C:\repo\PapaPersonas");

        Assert.True(result.IsOverrideApplied);
        Assert.Equal(@"C:\temp\smoke\run.duckdb", result.DatabasePath);
    }

    [Fact]
    public void Resolve_WithRelativeOverride_NormalizesAgainstCurrentDirectory()
    {
        var result = AppDatabasePathResolver.Resolve(
            localAppDataDirectory: @"C:\Users\dad\AppData\Local",
            overridePathRaw: @"tmp\smoke\run.duckdb",
            currentDirectory: @"C:\repo\PapaPersonas");

        Assert.True(result.IsOverrideApplied);
        Assert.Equal(@"C:\repo\PapaPersonas\tmp\smoke\run.duckdb", result.DatabasePath);
    }
}
