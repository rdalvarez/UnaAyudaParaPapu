using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PapaPersonas.App.Ui;
using PapaPersonas.Core.Activity;
using PapaPersonas.Core.App;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Query.Paso3;
using PapaPersonas.Infrastructure.Query.Paso3;
using Forms = System.Windows.Forms;

namespace PapaPersonas.App.Processes;

public partial class Paso3ProcessControl : System.Windows.Controls.UserControl
{
    private const string BusyOwner = "Paso3";

    private static readonly string[] AvailableColumns =
    [
        "cuil",
        "dni",
        "fecha_nacimiento",
        "sexo",
        "tipo_dni",
        "apellido",
        "nombre",
        "direccion",
        "codigo_postal",
        "localidad",
        "partido",
        "provincia",
        "nacionalidad",
        "telefono_fijo_1",
        "telefono_fijo_2",
        "telefono_fijo_3",
        "telefono_fijo_4",
        "telefono_fijo_5",
        "celular_1",
        "celular_2",
        "celular_3",
        "celular_4",
        "celular_5",
        "whatsapp_1",
        "whatsapp_2",
        "whatsapp_3",
        "whatsapp_4",
        "whatsapp_5",
        "email_1",
        "email_2",
        "email_3",
        "email_4",
        "email_5",
        "codigo_obra_social",
        "obra_social",
        "cuit_empleador",
        "edad",
        "anio",
        "fecha_importacion",
        "fecha_actualizacion"
    ];

    private static readonly string[] DefaultColumns =
    [
        "cuil",
        "apellido",
        "nombre",
        "obra_social",
        "codigo_postal",
        "fecha_importacion"
    ];

    private static readonly string[] FilterColumns =
    [
        "apellido",
        "nombre",
        "obra_social",
        "codigo_postal",
        "localidad",
        "provincia",
        "edad",
        "fecha_importacion",
        "fecha_actualizacion"
    ];

    private static readonly string[] FilterOperators =
    [
        "eq",
        "contains",
        "starts_with",
        "gte",
        "lte",
        "between"
    ];

    private readonly IPaso3QueryService _queryService;
    private readonly IPaso3ExportService _exportService;
    private readonly ActivityBuffer _activity = new(maxMessages: 500);
    private readonly List<Paso3Filter> _filters = [];

    private readonly Paso3ProcessUiState _uiState = new(AvailableColumns);

    private ProcessBusyCoordinator? _busyCoordinator;
    private Func<string?>? _databasePathProvider;
    private CancellationTokenSource? _exportCts;
    private bool _isLocalBusy;
    private Paso3PreviewResult? _lastPreviewResult;

    public Paso3ProcessControl()
        : this(new DuckDbPaso3QueryService(), new DuckDbPaso3ExportService())
    {
    }

    internal Paso3ProcessControl(IPaso3QueryService queryService, IPaso3ExportService exportService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));

        InitializeComponent();
        ActivityConsole.AttachBuffer(_activity);
        InitializeDefaults();
        LogInfo("Proceso 3", "Consola iniciada para esta sesión de la app.");
    }

    public void ConfigureCoordinator(ProcessBusyCoordinator coordinator)
    {
        _busyCoordinator = coordinator;
        _busyCoordinator.BusyStateChanged += BusyCoordinatorOnBusyStateChanged;
        UpdateControlState();
    }

    public void SetDatabasePathProvider(Func<string?> provider)
    {
        _databasePathProvider = provider;
    }

    public void ClearDatabaseDependentState()
    {
        _uiState.ResetPage();
        _lastPreviewResult = null;
        BindPreviewRows(Array.Empty<Paso3PreviewRow>());
        UpdateKpiAndPageLabels(0, 0, null);
        StatusText.Text = "La base cambió. Ejecutá la vista previa de nuevo.";
    }

    private void BusyCoordinatorOnBusyStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateControlState);
    }

    private void InitializeDefaults()
    {
        FilterColumnCombo.ItemsSource = FilterColumns;
        FilterColumnCombo.SelectedIndex = 0;
        FilterOperatorCombo.ItemsSource = FilterOperators;
        FilterOperatorCombo.SelectedItem = "contains";

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        ExportPathTextBox.Text = Path.Combine(documents, "PapaPersonas", "Proceso3", "consulta.csv");

        foreach (var column in _uiState.AvailableColumns)
        {
            var checkBox = new System.Windows.Controls.CheckBox
            {
                Content = column,
                Margin = new Thickness(0, 0, 12, 6),
                IsChecked = DefaultColumns.Contains(column, StringComparer.OrdinalIgnoreCase)
            };
            checkBox.Checked += ColumnSelectionChanged;
            checkBox.Unchecked += ColumnSelectionChanged;
            ColumnsWrapPanel.Children.Add(checkBox);
        }

        ApplyDefaultColumns();
        UpdateFilterListView();
        UpdateKpiAndPageLabels(0, 0, null);
        StatusText.Text = "Listo. Ejecutá la vista previa para consultar las filas paginadas.";
    }

    // Ejecuta la vista previa paginada con filtros validados y actualiza sus resultados agregados.
    private async void RunQuery_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginWork(isExport: false))
        {
            return;
        }

        try
        {
            if (!TryResolveDatabasePath(out var databasePath))
            {
                StatusText.Text = "La base no está lista.";
                LogError("Proceso 3", "Vista previa bloqueada: DuckDB no disponible.");
                return;
            }

            if (!_uiState.TryValidateDateRange(
                    ImportDateFromTextBox.Text,
                    ImportDateToTextBox.Text,
                    out var dateFrom,
                    out var dateTo,
                    out var dateError))
            {
                StatusText.Text = dateError ?? "El filtro de fecha no es válido.";
                LogError("Proceso 3", "Vista previa bloqueada: filtros de fecha inválidos (formato o rango).");
                return;
            }

            var request = BuildPreviewRequest(databasePath, dateFrom, dateTo);
            var result = await Task.Run(() => _queryService.QueryPreview(request));
            _lastPreviewResult = result;

            BindPreviewRows(result.Rows);
            UpdateKpiAndPageLabels(result.TotalCount, result.Rows.Count, result.LatestImportDate);
            StatusText.Text = $"Vista previa lista. Página {_uiState.CurrentPageNumber}.";

            LogInfo(
                "Proceso 3",
                Paso3ProcessUiState.BuildPreviewAggregateLog(
                    totalCount: result.TotalCount,
                    pageRowCount: result.Rows.Count,
                    pageNumber: _uiState.CurrentPageNumber,
                    selectedColumnsCount: _uiState.SelectedColumns.Count,
                    filtersCount: _filters.Count,
                    latestImportDate: result.LatestImportDate));
        }
        catch (ArgumentException)
        {
            StatusText.Text = "Vista previa bloqueada: verificá columna, operador y formato de fecha, y volvé a intentar.";
            LogError("Proceso 3", "Vista previa bloqueada por validación de filtros guiados.");
        }
        catch (Exception ex)
        {
            StatusText.Text = UserFacingExceptionMessage.WithTechnicalDetail(
                "La vista previa falló. Verificá los filtros y volvé a intentar.",
                ex);
            LogError(
                "Proceso 3",
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "Error de vista previa. Verificá los filtros y volvé a intentar.",
                    ex));
        }
        finally
        {
            EndWork();
        }
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPreviewResult is null)
        {
            return;
        }

        if (_uiState.MovePreviousPage())
        {
            _ = RunPreviewFromPagingAsync();
        }
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPreviewResult is null)
        {
            return;
        }

        if (_uiState.MoveNextPage(_lastPreviewResult.TotalCount))
        {
            _ = RunPreviewFromPagingAsync();
        }
    }

    // Recarga la página solicitada manteniendo los filtros y la paginación de la vista previa.
    private async Task RunPreviewFromPagingAsync()
    {
        if (!TryBeginWork(isExport: false))
        {
            return;
        }

        try
        {
            if (!TryResolveDatabasePath(out var databasePath))
            {
                StatusText.Text = "La base no está lista.";
                return;
            }

            if (!_uiState.TryValidateDateRange(
                    ImportDateFromTextBox.Text,
                    ImportDateToTextBox.Text,
                    out var dateFrom,
                    out var dateTo,
                    out var dateError))
            {
                StatusText.Text = dateError ?? "El filtro de fecha no es válido.";
                LogError("Proceso 3", "Cambio de página bloqueado por filtros de fecha inválidos.");
                return;
            }

            var request = BuildPreviewRequest(databasePath, dateFrom, dateTo);
            var result = await Task.Run(() => _queryService.QueryPreview(request));
            _lastPreviewResult = result;

            BindPreviewRows(result.Rows);
            UpdateKpiAndPageLabels(result.TotalCount, result.Rows.Count, result.LatestImportDate);
            StatusText.Text = $"Vista previa lista. Página {_uiState.CurrentPageNumber}.";
        }
        catch (ArgumentException)
        {
            StatusText.Text = "Cambio de página bloqueado: verificá columna, operador y formato de fecha, y volvé a intentar.";
            LogError("Proceso 3", "Cambio de página bloqueado por validación de filtros guiados.");
        }
        catch (Exception ex)
        {
            StatusText.Text = UserFacingExceptionMessage.WithTechnicalDetail(
                "La vista previa falló al cambiar de página.",
                ex);
            LogError(
                "Proceso 3",
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "Error al cambiar de página en la vista previa.",
                    ex));
        }
        finally
        {
            EndWork();
        }
    }

    private void AddFilter_Click(object sender, RoutedEventArgs e)
    {
        var column = FilterColumnCombo.SelectedItem?.ToString();
        var op = FilterOperatorCombo.SelectedItem?.ToString();
        var value = FilterValueTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(op) || string.IsNullOrWhiteSpace(value))
        {
            StatusText.Text = "El filtro requiere columna, operador y valor.";
            return;
        }

        _filters.Add(new Paso3Filter(column, op, value));
        FilterValueTextBox.Text = string.Empty;
        _uiState.ResetPage();
        _lastPreviewResult = null;
        UpdateFilterListView();
        StatusText.Text = "Filtro agregado. Ejecutá la vista previa para actualizar.";
    }

    private void RemoveFilter_Click(object sender, RoutedEventArgs e)
    {
        var selectedIndex = ActiveFiltersListBox.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _filters.Count)
        {
            return;
        }

        _filters.RemoveAt(selectedIndex);
        _uiState.ResetPage();
        _lastPreviewResult = null;
        UpdateFilterListView();
        StatusText.Text = "Filtro quitado. Ejecutá la vista previa para actualizar.";
    }

    private void SelectAllColumns_Click(object sender, RoutedEventArgs e)
    {
        _uiState.SelectAllColumns();
        RefreshColumnCheckboxesFromState();
        _lastPreviewResult = null;
        StatusText.Text = "Se seleccionaron todas las columnas.";
    }

    private void DefaultColumns_Click(object sender, RoutedEventArgs e)
    {
        ApplyDefaultColumns();
        _lastPreviewResult = null;
        StatusText.Text = "Se seleccionaron las columnas predeterminadas.";
    }

    // Exporta la consulta completa a un CSV temporalmente seguro e informa progreso y cancelación.
    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginWork(isExport: true))
        {
            return;
        }

        _exportCts = new CancellationTokenSource();

        try
        {
            if (!TryResolveDatabasePath(out var databasePath))
            {
                StatusText.Text = "La base no está lista.";
                LogError("Proceso 3", "Exportación bloqueada: DuckDB no disponible.");
                return;
            }

            var destinationPath = ExportPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                StatusText.Text = "Elegí una ruta de exportación.";
                return;
            }

            if (!_uiState.TryValidateDateRange(
                    ImportDateFromTextBox.Text,
                    ImportDateToTextBox.Text,
                    out var dateFrom,
                    out var dateTo,
                    out var dateError))
            {
                StatusText.Text = dateError ?? "El filtro de fecha para exportar no es válido.";
                return;
            }

            var exportRequest = new Paso3ExportRequest(
                BuildPreviewRequest(databasePath, dateFrom, dateTo, forceNoPagination: true),
                destinationPath);

            var progress = new Progress<Paso3ExportProgress>(value =>
            {
                StatusText.Text = value.Message;
            });

            var result = await _exportService.ExportCsvAsync(exportRequest, progress, _exportCts.Token);

            StatusText.Text = result.IsSuccess
                ? $"Exportación completada: {result.OutputPath}"
                : result.Status == Paso3ExportStatus.Canceled
                    ? "Exportación cancelada por el usuario."
                    : result.FailureMessage ?? "La exportación falló. Verificá la ruta de destino y volvé a intentar.";

            LogInfo(
                "Proceso 3",
                Paso3ProcessUiState.BuildExportAggregateLog(
                    rowsWritten: result.RowsWritten,
                    status: result.Status,
                    hasFailureMessage: !string.IsNullOrWhiteSpace(result.FailureMessage)));
        }
        catch (ArgumentException)
        {
            StatusText.Text = "Exportación bloqueada: verificá columna, operador y formato de fecha, y volvé a intentar.";
            LogError("Proceso 3", "Exportación bloqueada por validación de filtros guiados.");
        }
        catch (Exception ex)
        {
            StatusText.Text = UserFacingExceptionMessage.WithTechnicalDetail("La exportación falló inesperadamente.", ex);
            LogError(
                "Proceso 3",
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "Error de exportación. Verificá la ruta de destino y volvé a intentar.",
                    ex));
        }
        finally
        {
            _exportCts?.Dispose();
            _exportCts = null;
            EndWork();
        }
    }

    private void CancelExport_Click(object sender, RoutedEventArgs e)
    {
        _exportCts?.Cancel();
    }

    private void BrowseExport_Click(object sender, RoutedEventArgs e)
    {
        if (IsInteractionBlocked())
        {
            return;
        }

        using var dialog = new Forms.SaveFileDialog
        {
            Title = "Seleccionar CSV de exportación de Proceso 3",
            Filter = "Archivos CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.GetFileName(ExportPathTextBox.Text)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            ExportPathTextBox.Text = dialog.FileName;
        }
    }

    private void ColumnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox checkBox || checkBox.Content is not string column)
        {
            return;
        }

        _uiState.SetColumnSelection(column, checkBox.IsChecked == true);
        RefreshColumnCheckboxesFromState();
        _lastPreviewResult = null;
    }

    // Construye una solicitud estable con filtros, columnas y paginación actuales para consulta o exportación.
    private Paso3PreviewRequest BuildPreviewRequest(string databasePath, DateOnly? dateFrom, DateOnly? dateTo, bool forceNoPagination = false)
    {
        var pageNumber = forceNoPagination ? 1 : _uiState.CurrentPageNumber;
        var pageSize = forceNoPagination ? Paso3PreviewDefaults.MaxPageSize : _uiState.PageSize;

        return new Paso3PreviewRequest(
            DatabasePath: databasePath,
            ExactCuil: string.IsNullOrWhiteSpace(ExactCuilTextBox.Text) ? null : ExactCuilTextBox.Text.Trim(),
            Filters: _filters.ToArray(),
            ImportDateFrom: dateFrom,
            ImportDateTo: dateTo,
            SelectedColumns: _uiState.SelectedColumns,
            PageNumber: pageNumber,
            PageSize: pageSize);
    }

    private void BindPreviewRows(IReadOnlyList<Paso3PreviewRow> rows)
    {
        var table = new DataTable();
        var selectedColumns = _uiState.SelectedColumns;
        foreach (var column in selectedColumns)
        {
            table.Columns.Add(column, typeof(string));
        }

        foreach (var row in rows)
        {
            var data = table.NewRow();
            foreach (var column in selectedColumns)
            {
                data[column] = FormatValue(row.Values.TryGetValue(column, out var value) ? value : null);
            }

            table.Rows.Add(data);
        }

        PreviewDataGrid.ItemsSource = table.DefaultView;
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private void UpdateFilterListView()
    {
        ActiveFiltersListBox.ItemsSource = null;
        ActiveFiltersListBox.ItemsSource = _filters
            .Select(static filter => $"{filter.Column} {filter.Operator} {filter.Value}")
            .ToArray();
    }

    private void UpdateKpiAndPageLabels(int totalCount, int pageRows, DateOnly? latestImportDate)
    {
        TotalCountText.Text = $"Total: {totalCount}";
        PageCountText.Text = $"Filas de página: {pageRows}";
        LatestImportText.Text = $"Última fecha de importación: {(latestImportDate.HasValue ? latestImportDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "n/d")}";
    }

    private void ApplyDefaultColumns()
    {
        _uiState.ClearSelectedColumns();
        foreach (var column in DefaultColumns)
        {
            _uiState.SetColumnSelection(column, isSelected: true);
        }

        RefreshColumnCheckboxesFromState();
    }

    private void RefreshColumnCheckboxesFromState()
    {
        var selected = _uiState.SelectedColumns;
        foreach (var child in ColumnsWrapPanel.Children)
        {
            if (child is not System.Windows.Controls.CheckBox checkBox || checkBox.Content is not string column)
            {
                continue;
            }

            var shouldBeChecked = selected.Contains(column, StringComparer.OrdinalIgnoreCase);
            if (checkBox.IsChecked != shouldBeChecked)
            {
                checkBox.IsChecked = shouldBeChecked;
            }
        }
    }

    private bool TryResolveDatabasePath(out string databasePath)
    {
        databasePath = _databasePathProvider?.Invoke() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(databasePath) && File.Exists(databasePath);
    }

    private bool TryBeginWork(bool isExport)
    {
        if (_busyCoordinator is null)
        {
            return false;
        }

        if (!_busyCoordinator.TryBegin(BusyOwner))
        {
            return false;
        }

        _isLocalBusy = isExport
            ? _uiState.TryBeginExportBusy()
            : _uiState.TryBeginPreviewBusy();
        UpdateControlState();
        return _isLocalBusy;
    }

    private void EndWork()
    {
        _uiState.EndBusy();
        _isLocalBusy = false;
        _busyCoordinator?.End(BusyOwner);
        UpdateControlState();
    }

    private bool IsInteractionBlocked()
    {
        return _isLocalBusy || (_busyCoordinator?.IsBusy ?? false);
    }

    private void UpdateControlState()
    {
        var globalBusy = _busyCoordinator?.IsBusy ?? false;
        var localBusy = _isLocalBusy;
        var canEdit = !globalBusy;

        ExactCuilTextBox.IsEnabled = canEdit;
        ImportDateFromTextBox.IsEnabled = canEdit;
        ImportDateToTextBox.IsEnabled = canEdit;
        FilterColumnCombo.IsEnabled = canEdit;
        FilterOperatorCombo.IsEnabled = canEdit;
        FilterValueTextBox.IsEnabled = canEdit;
        AddFilterButton.IsEnabled = canEdit;
        RemoveFilterButton.IsEnabled = canEdit;
        SelectAllColumnsButton.IsEnabled = canEdit;
        DefaultColumnsButton.IsEnabled = canEdit;
        PrevPageButton.IsEnabled = canEdit && _uiState.CurrentPageNumber > 1;
        NextPageButton.IsEnabled = canEdit && _lastPreviewResult is not null && _uiState.CurrentPageNumber * _uiState.PageSize < _lastPreviewResult.TotalCount;

        foreach (var child in ColumnsWrapPanel.Children)
        {
            if (child is System.Windows.Controls.CheckBox checkBox)
            {
                checkBox.IsEnabled = canEdit;
            }
        }

        RunQueryButton.IsEnabled = canEdit;
        BrowseExportButton.IsEnabled = canEdit;
        ExportCsvButton.IsEnabled = canEdit;
        CancelExportButton.IsEnabled = _uiState.CanCancelExport;

        ExportProgressBar.Visibility = localBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LogInfo(string phase, string message) => ActivityConsole.Add(ActivitySeverity.Info, phase, message);
    private void LogError(string phase, string message) => ActivityConsole.Add(ActivitySeverity.Error, phase, message);
}
