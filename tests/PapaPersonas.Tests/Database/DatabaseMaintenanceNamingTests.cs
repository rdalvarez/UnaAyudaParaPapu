using PapaPersonas.Core.Database;

namespace PapaPersonas.Tests.Database;

public sealed class DatabaseMaintenanceNamingTests
{
    [Fact]
    public void BuildSuggestedBackupFileName_WithLatestImportDate_UsesDeterministicDatePattern()
    {
        var fileName = DatabaseMaintenanceNaming.BuildSuggestedBackupFileName(new DateOnly(2026, 8, 28));

        Assert.Equal("2026-08-28_BaseMaestra_BK.duckdb", fileName);
    }

    [Fact]
    public void BuildSuggestedBackupFileName_WithoutImportDate_UsesFallbackName()
    {
        var fileName = DatabaseMaintenanceNaming.BuildSuggestedBackupFileName(null);

        Assert.Equal("SinImportaciones_BaseMaestra_BK.duckdb", fileName);
    }

    [Fact]
    public void MaintenanceConfirmation_RestorePrompt_DescribesExclusiveTotalRollback()
    {
        Assert.Contains("DBeaver", DatabaseMaintenanceConfirmation.RestorePrompt, StringComparison.Ordinal);
        Assert.Contains("TODO el snapshot operativo", DatabaseMaintenanceConfirmation.RestorePrompt, StringComparison.Ordinal);
        Assert.Contains("personas", DatabaseMaintenanceConfirmation.RestorePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stock", DatabaseMaintenanceConfirmation.RestorePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ventas", DatabaseMaintenanceConfirmation.RestorePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("extracciones", DatabaseMaintenanceConfirmation.RestorePrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaintenanceRefreshPolicy_RestoreRequestsPendingDialog_ButResetDoesNot()
    {
        Assert.True(DatabaseMaintenanceRefreshPolicy.RequestsPendingDialog(DatabaseMaintenanceOperation.Restore));
        Assert.False(DatabaseMaintenanceRefreshPolicy.RequestsPendingDialog(DatabaseMaintenanceOperation.Reset));
    }
}
