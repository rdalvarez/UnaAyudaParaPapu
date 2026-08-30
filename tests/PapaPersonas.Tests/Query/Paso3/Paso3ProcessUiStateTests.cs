using PapaPersonas.Core.Query.Paso3;

namespace PapaPersonas.Tests.Query.Paso3;

public sealed class Paso3ProcessUiStateTests
{
    [Fact]
    public void BusyState_BeginAndEnd_FollowsExpectedTransitions()
    {
        var state = new Paso3ProcessUiState(["cuil", "apellido", "nombre"]);

        Assert.False(state.IsBusy);
        Assert.True(state.TryBeginBusy());
        Assert.True(state.IsBusy);
        Assert.False(state.TryBeginBusy());

        state.EndBusy();
        Assert.False(state.IsBusy);
    }

    [Fact]
    public void Pagination_NextAndPrevious_RespectTotalCountBoundaries()
    {
        var state = new Paso3ProcessUiState(["cuil"], pageSize: 2);

        Assert.Equal(1, state.CurrentPageNumber);
        Assert.False(state.MovePreviousPage());

        Assert.True(state.MoveNextPage(totalCount: 5));
        Assert.Equal(2, state.CurrentPageNumber);
        Assert.True(state.MoveNextPage(totalCount: 5));
        Assert.Equal(3, state.CurrentPageNumber);
        Assert.False(state.MoveNextPage(totalCount: 5));
        Assert.Equal(3, state.CurrentPageNumber);

        Assert.True(state.MovePreviousPage());
        Assert.Equal(2, state.CurrentPageNumber);
    }

    [Fact]
    public void SelectedColumns_DefaultsToAllAndSupportsSelectAllBehavior()
    {
        var state = new Paso3ProcessUiState(["cuil", "apellido", "nombre"]);

        Assert.Equal(["cuil", "apellido", "nombre"], state.SelectedColumns);

        state.SetColumnSelection("apellido", isSelected: false);
        Assert.Equal(["cuil", "nombre"], state.SelectedColumns);

        state.ClearSelectedColumns();
        Assert.Equal(["cuil"], state.SelectedColumns);

        state.SelectAllColumns();
        Assert.Equal(["cuil", "apellido", "nombre"], state.SelectedColumns);
    }

    [Fact]
    public void AggregateLogs_NeverIncludeRowLevelPiiValues()
    {
        var previewLog = Paso3ProcessUiState.BuildPreviewAggregateLog(
            totalCount: 124,
            pageRowCount: 100,
            pageNumber: 2,
            selectedColumnsCount: 8,
            filtersCount: 3,
            latestImportDate: new DateOnly(2026, 8, 11));

        var exportLog = Paso3ProcessUiState.BuildExportAggregateLog(
            rowsWritten: 124,
            status: Paso3ExportStatus.Completed,
            hasFailureMessage: false);

        Assert.Contains("total=124", previewLog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("page_rows=100", previewLog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("20123456789", previewLog, StringComparison.Ordinal);
        Assert.DoesNotContain("LOPEZ", previewLog, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("rows_written=124", exportLog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("20123456789", exportLog, StringComparison.Ordinal);
        Assert.DoesNotContain("LOPEZ", exportLog, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2026/08/10", "2026-08-11")]
    [InlineData("2026-08-10", "11-08-2026")]
    public void ValidateDateRange_InvalidFormat_IsRejected(string fromRaw, string toRaw)
    {
        var state = new Paso3ProcessUiState(["cuil"]);

        var ok = state.TryValidateDateRange(fromRaw, toRaw, out var fromDate, out var toDate, out var error);

        Assert.False(ok);
        Assert.Null(toDate);
        Assert.True(fromDate is null || fromDate == new DateOnly(2026, 8, 10));
        Assert.Equal("Los filtros de fecha deben usar el formato yyyy-MM-dd.", error);
    }

    [Fact]
    public void ValidateDateRange_FromGreaterThanTo_IsRejected()
    {
        var state = new Paso3ProcessUiState(["cuil"]);

        var ok = state.TryValidateDateRange("2026-08-12", "2026-08-10", out _, out _, out var error);

        Assert.False(ok);
        Assert.Equal("El rango de fechas de importación no es válido: Desde debe ser <= Hasta.", error);
    }

    [Fact]
    public void ValidateDateRange_ValidValues_AreParsed()
    {
        var state = new Paso3ProcessUiState(["cuil"]);

        var ok = state.TryValidateDateRange("2026-08-10", "2026-08-11", out var fromDate, out var toDate, out var error);

        Assert.True(ok);
        Assert.Equal(new DateOnly(2026, 8, 10), fromDate);
        Assert.Equal(new DateOnly(2026, 8, 11), toDate);
        Assert.Null(error);
    }

    [Fact]
    public void ExportCancelState_IsEnabledOnlyWhenExportBusy()
    {
        var state = new Paso3ProcessUiState(["cuil"]);

        Assert.False(state.IsExportBusy);
        Assert.False(state.CanCancelExport);

        Assert.True(state.TryBeginPreviewBusy());
        Assert.False(state.IsExportBusy);
        Assert.False(state.CanCancelExport);
        state.EndBusy();

        Assert.True(state.TryBeginExportBusy());
        Assert.True(state.IsExportBusy);
        Assert.True(state.CanCancelExport);
        state.EndBusy();

        Assert.False(state.IsExportBusy);
        Assert.False(state.CanCancelExport);
    }
}
