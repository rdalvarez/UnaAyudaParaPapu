using PapaPersonas.Core.App;

namespace PapaPersonas.Core.Query.Paso3;

public sealed class Paso3ProcessUiState
{
    private readonly List<string> _availableColumns;
    private readonly HashSet<string> _selectedColumns;
    private bool _isExportBusy;

    public Paso3ProcessUiState(IReadOnlyList<string> availableColumns, int pageSize = Paso3PreviewDefaults.DefaultPageSize)
    {
        ArgumentNullException.ThrowIfNull(availableColumns);

        _availableColumns = availableColumns
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_availableColumns.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos una columna disponible.", nameof(availableColumns));
        }

        _selectedColumns = new HashSet<string>(_availableColumns, StringComparer.OrdinalIgnoreCase);

        PageSize = pageSize <= 0
            ? Paso3PreviewDefaults.DefaultPageSize
            : Math.Min(pageSize, Paso3PreviewDefaults.MaxPageSize);
    }

    public bool IsBusy { get; private set; }

    public bool IsExportBusy => IsBusy && _isExportBusy;

    public bool CanCancelExport => IsExportBusy;

    public int CurrentPageNumber { get; private set; } = 1;

    public int PageSize { get; }

    public IReadOnlyList<string> AvailableColumns => _availableColumns;

    public IReadOnlyList<string> SelectedColumns =>
        _availableColumns.Where(column => _selectedColumns.Contains(column)).ToArray();

    public bool TryBeginBusy()
    {
        return TryBegin(isExport: false);
    }

    public bool TryBeginPreviewBusy()
    {
        return TryBegin(isExport: false);
    }

    public bool TryBeginExportBusy()
    {
        return TryBegin(isExport: true);
    }

    private bool TryBegin(bool isExport)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        _isExportBusy = isExport;
        return true;
    }

    public void EndBusy()
    {
        IsBusy = false;
        _isExportBusy = false;
    }

    public bool TryValidateDateRange(
        string? fromRaw,
        string? toRaw,
        out DateOnly? fromDate,
        out DateOnly? toDate,
        out string? errorMessage)
    {
        if (!TryParseOptionalDate(fromRaw, out fromDate))
        {
            toDate = null;
            errorMessage = "Los filtros de fecha deben usar el formato yyyy-MM-dd.";
            return false;
        }

        if (!TryParseOptionalDate(toRaw, out toDate))
        {
            errorMessage = "Los filtros de fecha deben usar el formato yyyy-MM-dd.";
            return false;
        }

        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        {
            errorMessage = "El rango de fechas de importación no es válido: Desde debe ser <= Hasta.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    public bool MovePreviousPage()
    {
        if (CurrentPageNumber <= 1)
        {
            return false;
        }

        CurrentPageNumber--;
        return true;
    }

    public bool MoveNextPage(int totalCount)
    {
        var maxPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (CurrentPageNumber >= maxPage)
        {
            return false;
        }

        CurrentPageNumber++;
        return true;
    }

    public void ResetPage() => CurrentPageNumber = 1;

    public void SetColumnSelection(string column, bool isSelected)
    {
        if (!_availableColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (isSelected)
        {
            _selectedColumns.Add(column);
            return;
        }

        _selectedColumns.Remove(column);
        if (_selectedColumns.Count == 0)
        {
            _selectedColumns.Add(_availableColumns[0]);
        }
    }

    public void ClearSelectedColumns()
    {
        _selectedColumns.Clear();
        _selectedColumns.Add(_availableColumns[0]);
    }

    public void SelectAllColumns()
    {
        _selectedColumns.Clear();
        foreach (var column in _availableColumns)
        {
            _selectedColumns.Add(column);
        }
    }

    public static string BuildPreviewAggregateLog(
        int totalCount,
        int pageRowCount,
        int pageNumber,
        int selectedColumnsCount,
        int filtersCount,
        DateOnly? latestImportDate)
    {
        var latestImport = latestImportDate.HasValue
            ? latestImportDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : "n/a";

        return $"Vista previa completada (total={totalCount}, page_rows={pageRowCount}, page={pageNumber}, selected_columns={selectedColumnsCount}, filters={filtersCount}, latest_import={latestImport}).";
    }

    public static string BuildExportAggregateLog(int rowsWritten, Paso3ExportStatus status, bool hasFailureMessage)
    {
        return $"Exportación finalizada (status={StatusDisplayNames.For(status)}, rows_written={rowsWritten}, failure_message_present={hasFailureMessage}).";
    }

    private static bool TryParseOptionalDate(string? raw, out DateOnly? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(
                raw.Trim(),
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
