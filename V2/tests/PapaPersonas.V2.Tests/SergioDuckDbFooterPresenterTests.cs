using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class SergioDuckDbFooterPresenterTests
{
    [Fact]
    public void InitializingPresentationUsesExpectedTextAndRespectsBusyFlags()
    {
        var ready = SergioDuckDbFooterPresenter.Initializing(@"C:\local\db\papapersonas-v2.duckdb", isAppBusy: false, isStatusBusy: false);
        var busy = SergioDuckDbFooterPresenter.Initializing(@"C:\local\db\papapersonas-v2.duckdb", isAppBusy: true, isStatusBusy: false);

        Assert.Equal("Base local: inicializando DuckDB...", ready.StatusText);
        Assert.Equal("Reiniciar base", ready.ResetButtonText);
        Assert.True(ready.CanReset);
        Assert.False(busy.CanReset);
        Assert.Equal(@"C:\local\db\papapersonas-v2.duckdb", ready.PathToolTip);
    }

    [Fact]
    public void ReadyWithoutOfficialLoadPresentationIsTruthfulAndActionable()
    {
        var status = new SergioDuckDbLifecycleStatus(
            SergioDuckDbLifecycleKind.ReadyWithoutOfficialLoad,
            "NO_OFFICIAL_LOAD",
            @"C:\local\db\papapersonas-v2.duckdb",
            MetadataRowCount: 0);

        var view = SergioDuckDbFooterPresenter.FromStatus(status, isAppBusy: false, isStatusBusy: false);

        Assert.Equal("Base local lista sin carga oficial de Sergio.", view.StatusText);
        Assert.Contains("Todavía no existe una carga oficial", view.DetailText);
        Assert.True(view.CanReset);
    }

    [Fact]
    public void ReadyWithOfficialLoadShowsSafeFileNameAndOfficialRows()
    {
        var status = new SergioDuckDbLifecycleStatus(
            SergioDuckDbLifecycleKind.ReadyWithOfficialLoad,
            "OFFICIAL_LOAD_READY",
            @"C:\local\db\papapersonas-v2.duckdb",
            MetadataRowCount: 1,
            OfficialRowCount: 123,
            SourceFileName: @"C:\private\workbooks\sergio-2026.xlsx");

        var view = SergioDuckDbFooterPresenter.FromStatus(status, isAppBusy: false, isStatusBusy: false);

        Assert.Equal("Base local lista con carga oficial de Sergio.", view.StatusText);
        Assert.Contains("Archivo oficial: sergio-2026.xlsx.", view.DetailText);
        Assert.DoesNotContain("private", view.DetailText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Filas oficiales: 123.", view.DetailText);
    }

    [Fact]
    public void InconsistentAndUnavailableAreActionable()
    {
        var inconsistent = new SergioDuckDbLifecycleStatus(
            SergioDuckDbLifecycleKind.Inconsistent,
            "PARTIAL_DYNAMIC_TABLES",
            @"C:\local\db\papapersonas-v2.duckdb",
            MetadataRowCount: 1);
        var unavailable = new SergioDuckDbLifecycleStatus(
            SergioDuckDbLifecycleKind.Unavailable,
            "DUCKDB_UNAVAILABLE_IOException",
            @"C:\local\db\papapersonas-v2.duckdb",
            MetadataRowCount: 0);

        var inconsistentView = SergioDuckDbFooterPresenter.FromStatus(inconsistent, isAppBusy: false, isStatusBusy: false);
        var unavailableView = SergioDuckDbFooterPresenter.FromStatus(unavailable, isAppBusy: false, isStatusBusy: false);

        Assert.Contains("Reiniciar base", inconsistentView.DetailText);
        Assert.Contains("vuelva a intentar", unavailableView.DetailText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarningSuffixIsAppendedWhenTransientWarningIsPresent()
    {
        var status = new SergioDuckDbLifecycleStatus(
            SergioDuckDbLifecycleKind.ReadyWithoutOfficialLoad,
            "NO_OFFICIAL_LOAD",
            @"C:\local\db\papapersonas-v2.duckdb",
            MetadataRowCount: 0,
            HasTransientSergioTablesWarning: true,
            Warning: "Transient Sergio tables were detected and may come from an interrupted operation.");

        var view = SergioDuckDbFooterPresenter.FromStatus(status, isAppBusy: false, isStatusBusy: false);

        Assert.Contains("Advertencia: se detectaron tablas transitorias de Sergio.", view.DetailText);
    }

    [Fact]
    public void CompactPathEllipsizesLongPathsAndKeepsTooltipFullPath()
    {
        var longPath = @"C:\Users\someone\AppData\Local\PapaPersonas\V2\nested\another\deep\folder\papapersonas-v2.duckdb";

        var compact = SergioDuckDbFooterPresenter.CompactPath(longPath, maxChars: 40);

        Assert.Contains('…', compact);
        Assert.True(compact.Length <= 40);
    }

    [Fact]
    public void ResetFailurePresentationDistinguishesPreservedDeletedAndMissingDatabase()
    {
        var preserved = SergioDuckDbResetFailurePresenter.Present(SergioDuckDbResetFailureKind.PreviousDatabasePreserved);
        var deleted = SergioDuckDbResetFailurePresenter.Present(SergioDuckDbResetFailureKind.PreviousDataIrreversiblyDeleted);
        var missing = SergioDuckDbResetFailurePresenter.Present(SergioDuckDbResetFailureKind.NoPreviousDatabase);

        Assert.Contains("se conservó", preserved.AlertText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no se pueden recuperar", preserved.AlertText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no se pueden recuperar", deleted.AlertText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No había una base anterior", missing.AlertText, StringComparison.Ordinal);
        Assert.Contains("No se eliminó ningún dato previo", missing.AlertText, StringComparison.Ordinal);
        Assert.DoesNotContain("se conservó", missing.AlertText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("La base anterior se eliminó", missing.AlertText, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetIsDisabledWhenStatusRefreshIsBusy()
    {
        var status = new SergioDuckDbLifecycleStatus(
            SergioDuckDbLifecycleKind.ReadyWithOfficialLoad,
            "OFFICIAL_LOAD_READY",
            @"C:\local\db\papapersonas-v2.duckdb",
            MetadataRowCount: 1,
            OfficialRowCount: 5,
            SourceFileName: "sergio.xlsx");

        var view = SergioDuckDbFooterPresenter.FromStatus(status, isAppBusy: false, isStatusBusy: true);

        Assert.False(view.CanReset);
    }
}
