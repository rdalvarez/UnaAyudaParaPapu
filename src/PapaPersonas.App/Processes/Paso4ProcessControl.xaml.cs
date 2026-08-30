using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using PapaPersonas.App.Ui;
using PapaPersonas.Core.Activity;
using PapaPersonas.Core.App;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Stock.Paso4;
using PapaPersonas.Infrastructure.Database;
using PapaPersonas.Infrastructure.Stock.Paso4;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace PapaPersonas.App.Processes;

public partial class Paso4ProcessControl : System.Windows.Controls.UserControl
{
    private const string BusyOwner = "Paso4";

    private readonly IPaso4StockService _stockService;
    private readonly IPaso4ExtractionService _extractionService;
    private readonly IPaso4ColumnSelectionStore _columnSelectionStore;
    private readonly ActivityBuffer _activity = new(maxMessages: 500);

    private readonly ObservableCollection<Paso4SummaryRowViewModel> _summaryRows = [];

    private ProcessBusyCoordinator? _busyCoordinator;
    private Func<string?>? _databasePathProvider;
    private bool _isLocalBusy;
    private bool _isRefreshing;
    private bool _pendingDecisionInProgress;
    private Paso4StockOverview? _overview;
    private IReadOnlyList<string> _selectedColumns = Paso4ColumnCatalog.DefaultColumns;

    public Paso4ProcessControl()
        : this(new DuckDbPaso4StockService(), new DuckDbPaso4ExtractionService(), new Paso4ColumnSelectionStore())
    {
    }

    internal Paso4ProcessControl(
        IPaso4StockService stockService,
        IPaso4ExtractionService extractionService,
        IPaso4ColumnSelectionStore columnSelectionStore)
    {
        _stockService = stockService;
        _extractionService = extractionService;
        _columnSelectionStore = columnSelectionStore;

        InitializeComponent();

        ActivityConsole.AttachBuffer(_activity);
        SummaryGrid.ItemsSource = _summaryRows;
        CompletedExtractionsComboBox.ItemsSource = new List<Paso4CompletedExtractionDisplayItem>();

        LoadColumnSelection();
        RefreshFooterTotals();
        Loaded += async (_, _) => await RefreshAllAsync(showPendingDialog: true);
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

    public Task RefreshDatabaseDependentStateAsync(bool showPendingDialog = false) => RefreshAllAsync(showPendingDialog);

    private void BusyCoordinatorOnBusyStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateControlState);
    }

    // Recarga stock, resumen y extracciones pendientes, evitando refrescos concurrentes.
    private async Task RefreshAllAsync(bool showPendingDialog)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            if (!TryResolveDatabasePath(out var dbPath))
            {
                SetNoDatabaseState();
                return;
            }

            _overview = await Task.Run(() => _stockService.GetOverview(dbPath));
            BindDates(_overview.AvailableDates);
            BindStockStatus(_overview);
            await LoadSummaryAsync(dbPath);
            BindCompletedExtractions(dbPath);
            UpdateControlState();

            if (showPendingDialog)
            {
                await ResolvePendingIfNeededAsync(dbPath);
            }
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            StockBadgeText.Text = UserFacingExceptionMessage.WithTechnicalDetail("STOCK no disponible.", ex);
            StockBadgeText.Foreground = StatusBrushes.Error;
            LogError(
                "Proceso 4",
                UserFacingExceptionMessage.WithTechnicalDetail("Error al refrescar estado.", ex));
        }
        finally
        {
            _isRefreshing = false;
            UpdateControlState();
        }
    }

    private async Task LoadSummaryAsync(string dbPath)
    {
        _summaryRows.Clear();

        if (_overview?.CurrentStock is null)
        {
            MetricTotalText.Text = "Total: 0";
            MetricSoldText.Text = "Vendidos: 0";
            MetricAvailableText.Text = "Disponibles: 0";
            MetricGroupsText.Text = "Grupos OS: 0";
            RefreshFooterTotals();
            return;
        }

        var summary = await Task.Run(() => _stockService.GetCurrentStockSummary(dbPath));
        foreach (var row in summary.Groups)
        {
            _summaryRows.Add(new Paso4SummaryRowViewModel(row, OnQuantityChanged));
        }

        MetricTotalText.Text = $"Total: {summary.Header.TotalMembers}";
        MetricSoldText.Text = $"Vendidos: {summary.Header.SoldMembers}";
        MetricAvailableText.Text = $"Disponibles: {summary.Header.AvailableMembers}";
        MetricGroupsText.Text = $"Grupos OS: {summary.Header.GroupCount}";

        RefreshFooterTotals();
    }

    // Obliga a resolver una extracción pendiente mediante reintento o cancelación antes de continuar.
    private async Task ResolvePendingIfNeededAsync(string dbPath)
    {
        if (_pendingDecisionInProgress)
        {
            return;
        }

        var pending = _extractionService.GetPendingState(dbPath);
        if (pending is null)
        {
            return;
        }

        _pendingDecisionInProgress = true;
        try
        {
            var owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow;
            var dialog = new Paso4PendingDecisionDialog(pending)
            {
                Owner = owner
            };

            var result = dialog.ShowDialog();
            if (result != true || dialog.Decision == Paso4PendingDecision.None)
            {
                ShowWarning("Existe una extracción pendiente. Debés resolverla para continuar con acciones de cambio.", "Extracción pendiente");
                return;
            }

            if (dialog.Decision == Paso4PendingDecision.Retry)
            {
                if (!TryBeginWork())
                {
                    return;
                }

                try
                {
                    var retry = await _extractionService.RetryPendingExtractionAsync(dbPath, CancellationToken.None);
                    if (retry.IsSuccess)
                    {
                        LogSuccess("Proceso 4", $"Reintento completado. Filas exportadas={retry.ExportedRows}.");
                        ShowInfo($"Reintento completado. Archivo: {retry.OutputPath}", "Extracción pendiente");
                    }
                    else
                    {
                        LogError("Proceso 4", "Reintento de extracción pendiente falló.");
                        ShowWarning(
                            retry.FailureMessage ?? Paso4ProcessUiLogic.BuildRetryPendingErrorMessage(retry.Status),
                            "Extracción pendiente");
                    }
                }
                finally
                {
                    EndWork();
                }
            }
            else
            {
                if (!TryBeginWork())
                {
                    return;
                }

                try
                {
                    var cancel = _extractionService.CancelPendingExtraction(dbPath);
                    if (cancel.IsSuccess)
                    {
                        LogInfo("Proceso 4", $"Pendiente cancelado. Reservas liberadas={cancel.ReleasedRows}.");
                        ShowInfo("La extracción pendiente se canceló y se liberaron las reservas.", "Extracción pendiente");
                    }
                    else
                    {
                        LogError("Proceso 4", "Cancelación de extracción pendiente bloqueada o fallida.");
                        ShowWarning(
                            cancel.FailureMessage ?? Paso4ProcessUiLogic.BuildCancelPendingErrorMessage(cancel.Status),
                            "Extracción pendiente");
                    }
                }
                finally
                {
                    EndWork();
                }
            }

            await RefreshAllAsync(showPendingDialog: false);
        }
        finally
        {
            _pendingDecisionInProgress = false;
        }
    }

    // Genera o regenera el stock de la fecha seleccionada tras confirmar el reemplazo de estado.
    private async void GenerateStock_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveDatabasePath(out var dbPath) || !TryGetSelectedDate(out var selectedDate) || !TryBeginWork())
        {
            return;
        }

        try
        {
            var hasExistingStock = _overview?.CurrentStock is not null;
            var message = hasExistingStock
                ? "Esta acción eliminará el stock actual, ventas, pendientes y opciones de reexportación. ¿Querés continuar?"
                : "Se generará el stock para la fecha seleccionada. ¿Querés continuar?";

            var confirm = MessageBox.Show(
                Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
                message,
                hasExistingStock ? "Regenerar stock" : "Generar stock",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            ActivityConsole.ClearActivity();
            LogInfo("Proceso 4", "Inicio de generación/regeneración de stock.");

            var result = await Task.Run(() => _stockService.GenerateOrRegenerateStock(new Paso4GenerateStockRequest(dbPath, selectedDate)));
            if (!result.IsSuccess)
            {
                ShowWarning(
                    result.FailureMessage ?? Paso4ProcessUiLogic.BuildGenerateStockErrorMessage(result.Status),
                    "Stock");
                LogError("Proceso 4", "Generación/regeneración bloqueada o fallida.");
                return;
            }

            LogSuccess("Proceso 4", $"Stock generado. miembros={result.MembersSnapshotted}, fallback_legacy={result.LegacyFallbackAssignedCount}.");
            ShowInfo($"Stock generado correctamente. Personas incluidas: {result.MembersSnapshotted}.", "Stock");

            await RefreshAllAsync(showPendingDialog: true);
        }
        finally
        {
            EndWork();
        }
    }

    private void SelectColumns_Click(object sender, RoutedEventArgs e)
    {
        if (IsInteractionBlocked())
        {
            return;
        }

        var dialog = new Paso4ColumnSelectionDialog(_selectedColumns)
        {
            Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _columnSelectionStore.Save(dialog.SelectedColumns);
        LoadColumnSelection();
        LogInfo("Proceso 4", $"Columnas de exportación actualizadas. seleccionadas={_selectedColumns.Count}.");
    }

    private async void ExportSummary_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveDatabasePath(out var dbPath) || !TryBeginWork())
        {
            return;
        }

        try
        {
            if (!TryGetSourceDateForFileName(out var sourceDate))
            {
                ShowWarning("No hay stock generado para exportar.", "Exportar resumen");
                return;
            }

            var path = PickCsvPath("Exportar resumen de stock", Paso4ProcessUiLogic.BuildSuggestedFileName("resumen_stock", sourceDate, DateTime.Now));
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var result = await _stockService.ExportSummaryCsvAsync(new Paso4SummaryExportRequest(dbPath, path), CancellationToken.None);
            if (!result.IsSuccess)
            {
                ShowWarning(
                    result.FailureMessage ?? Paso4ProcessUiLogic.BuildExportSummaryErrorMessage(),
                    "Exportar resumen");
                return;
            }

            ShowInfo($"Resumen exportado. Filas: {result.RowsWritten}.\nRuta: {result.OutputPath}", "Exportar resumen");
            LogInfo("Proceso 4", $"Resumen exportado. filas={result.RowsWritten}.");
        }
        finally
        {
            EndWork();
        }
    }

    private async void ExportFull_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveDatabasePath(out var dbPath) || !TryBeginWork())
        {
            return;
        }

        try
        {
            if (!TryGetSourceDateForFileName(out var sourceDate))
            {
                ShowWarning("No hay stock generado para exportar.", "Exportar stock completo");
                return;
            }

            var path = PickCsvPath("Exportar stock completo", Paso4ProcessUiLogic.BuildSuggestedFileName("stock_completo", sourceDate, DateTime.Now, includeTimestamp: true));
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var result = await _stockService.ExportFullCsvAsync(
                new Paso4FullExportRequest(dbPath, path, OnlyAvailableCheckBox.IsChecked == true, _selectedColumns),
                CancellationToken.None);

            if (!result.IsSuccess)
            {
                ShowWarning(
                    result.FailureMessage ?? Paso4ProcessUiLogic.BuildExportFullErrorMessage(),
                    "Exportar stock completo");
                return;
            }

            ShowInfo($"Stock completo exportado. Filas: {result.RowsWritten}.\nRuta: {result.OutputPath}", "Exportar stock completo");
            LogInfo("Proceso 4", $"Exportación completa finalizada. filas={result.RowsWritten}, solo_disponibles={(OnlyAvailableCheckBox.IsChecked == true)}.");
        }
        finally
        {
            EndWork();
        }
    }

    // Valida, reserva y exporta la selección de personas respetando el estado pendiente del stock.
    private async void ExtractSelection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveDatabasePath(out var dbPath) || !TryBeginWork())
        {
            return;
        }

        try
        {
            var rows = _summaryRows
                .Select(r => new Paso4QuantityInput(r.NormalizedCodigoObraSocial, r.NormalizedObraSocial, r.QuantityText))
                .ToArray();

            var groups = Paso4ProcessUiLogic.BuildGroupRequests(rows, out var validationMessage);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                ShowWarning(validationMessage, "Extraer selección");
                return;
            }

            if (groups.Count == 0)
            {
                ShowWarning("Ingresá al menos una cantidad mayor a cero para extraer.", "Extraer selección");
                return;
            }

            var preflight = _extractionService.PreflightExtraction(dbPath, groups);
            if (!preflight.IsReady)
            {
                ShowWarning(Paso4ProcessUiLogic.BuildPreflightValidationMessage(preflight), "Extraer selección");
                return;
            }

            var totals = Paso4ProcessUiLogic.ComputeRequestTotals(rows);
            var confirm = MessageBox.Show(
                Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
                Paso4ProcessUiLogic.BuildExtractionConfirmationMessage(totals),
                "Confirmar extracción",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            if (!TryGetSourceDateForFileName(out var sourceDate))
            {
                ShowWarning("No hay stock generado para extraer.", "Extraer selección");
                return;
            }

            var path = PickCsvPath("Guardar extracción", Paso4ProcessUiLogic.BuildSuggestedFileName("extraccion_stock", sourceDate, DateTime.Now, includeTimestamp: true));
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            ActivityConsole.ClearActivity();
            LogInfo("Proceso 4", "Inicio de extracción de selección por grupos.");

            var result = await _extractionService.BeginExtractionAsync(
                new Paso4BeginExtractionRequest(dbPath, path, _selectedColumns, groups),
                CancellationToken.None);

            if (!result.IsSuccess)
            {
                ShowWarning(
                    result.FailureMessage ?? Paso4ProcessUiLogic.BuildBeginExtractionErrorMessage(result.Status),
                    "Extraer selección");
                LogError("Proceso 4", "Extracción fallida o bloqueada. No se reporta éxito parcial.");
                await RefreshAllAsync(showPendingDialog: true);
                return;
            }

            ShowInfo($"Extracción completada. Filas: {result.ExportedRows}.\nRuta: {result.OutputPath}", "Extraer selección");
            LogSuccess("Proceso 4", $"Extracción completada. filas={result.ExportedRows}.");

            ClearQuantitiesInternal();
            await RefreshAllAsync(showPendingDialog: true);
        }
        finally
        {
            EndWork();
        }
    }

    private void ClearQuantities_Click(object sender, RoutedEventArgs e)
    {
        ClearQuantitiesInternal();
    }

    private async void ReExportExtraction_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveDatabasePath(out var dbPath) || !TryBeginWork())
        {
            return;
        }

        try
        {
            if (CompletedExtractionsComboBox.SelectedItem is not Paso4CompletedExtractionDisplayItem selected)
            {
                ShowWarning("Seleccioná una extracción completada para reexportar.", "Reexportar extracción");
                return;
            }

            if (!TryGetSourceDateForFileName(out var sourceDate))
            {
                ShowWarning("No hay stock generado para reexportar.", "Reexportar extracción");
                return;
            }

            var path = PickCsvPath("Reexportar extracción", Paso4ProcessUiLogic.BuildSuggestedFileName("reexport_extraccion", sourceDate, DateTime.Now, includeTimestamp: true));
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var result = await _extractionService.ReExportCompletedAsync(
                new Paso4ReExportExtractionRequest(dbPath, selected.Token, path, _selectedColumns),
                CancellationToken.None);

            if (!result.IsSuccess)
            {
                ShowWarning(
                    result.FailureMessage ?? Paso4ProcessUiLogic.BuildReExportErrorMessage(result.Status),
                    "Reexportar extracción");
                return;
            }

            ShowInfo($"Reexportación completada. Filas: {result.RowsWritten}.\nRuta: {result.OutputPath}", "Reexportar extracción");
            LogInfo("Proceso 4", $"Reexportación completada. filas={result.RowsWritten}.");
        }
        finally
        {
            EndWork();
        }
    }

    private async void DateComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isRefreshing || !TryResolveDatabasePath(out _))
        {
            return;
        }

        await RefreshAllAsync(showPendingDialog: false);
    }

    private void SetNoDatabaseState()
    {
        DateComboBox.ItemsSource = null;
        _summaryRows.Clear();
        StockBadgeText.Text = "STOCK no disponible";
        StockBadgeText.Foreground = StatusBrushes.Error;
        StockMetaText.Text = "DuckDB no inicializada.";
        MetricTotalText.Text = "Total: 0";
        MetricSoldText.Text = "Vendidos: 0";
        MetricAvailableText.Text = "Disponibles: 0";
        MetricGroupsText.Text = "Grupos OS: 0";
        DateWarningText.Visibility = Visibility.Collapsed;
        UpdateControlState();
    }

    private void BindDates(IReadOnlyList<Paso4DateOption> availableDates)
    {
        var selectedBefore = DateComboBox.SelectedItem as DateOnly?;
        var dates = availableDates.Select(x => x.FechaImportacion).ToList();
        DateComboBox.ItemsSource = dates;
        DateComboBox.ItemStringFormat = "yyyy-MM-dd";
        DateComboBox.SelectedItem = selectedBefore.HasValue && dates.Contains(selectedBefore.Value)
            ? selectedBefore.Value
            : dates.FirstOrDefault();
    }

    private void BindStockStatus(Paso4StockOverview overview)
    {
        if (overview.CurrentStock is null)
        {
            StockBadgeText.Text = "STOCK no generado";
            StockBadgeText.Foreground = StatusBrushes.Info;
            StockMetaText.Text = "Generá stock para empezar a operar.";
            DateWarningText.Visibility = Visibility.Collapsed;
            return;
        }

        var current = overview.CurrentStock;
        StockBadgeText.Text = current.IsStale ? "STOCK desactualizado" : "STOCK actualizado";
        StockBadgeText.Foreground = current.IsStale ? StatusBrushes.Error : StatusBrushes.Success;
        StockMetaText.Text = $"Fecha stock: {Paso4ProcessUiLogic.FormatDateIso(current.SourceFechaImportacion)} | Generado: {current.GeneratedUtc:yyyy-MM-dd HH:mm:ss}";

        var selected = DateComboBox.SelectedItem is DateOnly selectedDate ? selectedDate : (DateOnly?)null;
        if (selected.HasValue && overview.LatestSuccessfulImportDate.HasValue && selected.Value < overview.LatestSuccessfulImportDate.Value)
        {
            DateWarningText.Text = $"Advertencia: la fecha seleccionada ({Paso4ProcessUiLogic.FormatDateIso(selected.Value)}) no es la más reciente y puede generar un stock reducido o desactualizado.";
            DateWarningText.Visibility = Visibility.Visible;
        }
        else
        {
            DateWarningText.Visibility = Visibility.Collapsed;
        }
    }

    private void BindCompletedExtractions(string dbPath)
    {
        var options = _extractionService.ListCompletedExtractions(dbPath)
            .Select(x => new Paso4CompletedExtractionDisplayItem(
                x.Token,
                $"{x.FechaVentaUtc:yyyy-MM-dd HH:mm:ss} - {Paso4ProcessUiLogic.FormatRowCount(x.RowCount)}"))
            .ToArray();

        CompletedExtractionsComboBox.ItemsSource = options;
        CompletedExtractionsComboBox.SelectedIndex = options.Length > 0 ? 0 : -1;
    }

    private void OnQuantityChanged()
    {
        RefreshFooterTotals();
    }

    private void RefreshFooterTotals()
    {
        var totals = Paso4ProcessUiLogic.ComputeRequestTotals(
            _summaryRows.Select(r => new Paso4QuantityInput(r.NormalizedCodigoObraSocial, r.NormalizedObraSocial, r.QuantityText)).ToArray());

        SelectionTotalsText.Text = $"Grupos seleccionados: {totals.SelectedGroups} | Personas solicitadas: {totals.TotalPeopleRequested}";
    }

    private void ClearQuantitiesInternal()
    {
        var cleared = Paso4ProcessUiLogic.ClearQuantities(
            _summaryRows.Select(r => new Paso4QuantityInput(r.NormalizedCodigoObraSocial, r.NormalizedObraSocial, r.QuantityText)).ToArray());

        for (var i = 0; i < _summaryRows.Count && i < cleared.Count; i++)
        {
            _summaryRows[i].QuantityText = cleared[i].QuantityText;
        }

        RefreshFooterTotals();
    }

    private void LoadColumnSelection()
    {
        var loaded = _columnSelectionStore.Load();
        _selectedColumns = loaded.SelectedColumns;
        if (loaded.Warnings.Count > 0)
        {
            LogInfo("Proceso 4", "Se aplicó configuración de columnas por defecto debido a configuración inválida.");
        }
    }

    private bool TryResolveDatabasePath(out string databasePath)
    {
        databasePath = _databasePathProvider?.Invoke() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(databasePath) && File.Exists(databasePath);
    }

    private bool TryGetSelectedDate(out DateOnly selectedDate)
    {
        if (DateComboBox.SelectedItem is DateOnly value)
        {
            selectedDate = value;
            return true;
        }

        selectedDate = default;
        return false;
    }

    private bool TryGetSourceDateForFileName(out DateOnly sourceDate)
    {
        if (_overview?.CurrentStock is not null)
        {
            sourceDate = _overview.CurrentStock.SourceFechaImportacion;
            return true;
        }

        sourceDate = default;
        return false;
    }

    private string? PickCsvPath(string title, string suggestedFileName)
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = title,
            Filter = "Archivos CSV (*.csv)|*.csv",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = suggestedFileName
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private bool TryBeginWork()
    {
        if (_busyCoordinator is null)
        {
            return false;
        }

        if (!_busyCoordinator.TryBegin(BusyOwner))
        {
            return false;
        }

        _isLocalBusy = true;
        UpdateControlState();
        return true;
    }

    private void EndWork()
    {
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
        var busy = IsInteractionBlocked();
        var hasPending = false;
        var hasStock = _overview?.CurrentStock is not null;

        if (TryResolveDatabasePath(out var dbPath))
        {
            hasPending = _extractionService.GetPendingState(dbPath) is not null;
        }

        var ui = Paso4ProcessUiLogic.EvaluateControlState(busy, hasPending, hasStock);

        DateComboBox.IsEnabled = !busy;
        GenerateStockButton.Content = hasStock ? "Regenerar stock" : "Generar stock";
        GenerateStockButton.IsEnabled = ui.CanGenerateOrRegenerate;

        ExtractSelectionButton.IsEnabled = ui.CanExtract;
        ClearQuantitiesButton.IsEnabled = !busy;
        SelectColumnsButton.IsEnabled = !busy;

        ExportSummaryButton.IsEnabled = ui.CanReadOnlyExport;
        ExportFullButton.IsEnabled = ui.CanReadOnlyExport;
        OnlyAvailableCheckBox.IsEnabled = ui.CanReadOnlyExport;

        CompletedExtractionsComboBox.IsEnabled = !busy;
        ReExportExtractionButton.IsEnabled = !busy && CompletedExtractionsComboBox.SelectedItem is Paso4CompletedExtractionDisplayItem;
    }

    private void ShowWarning(string message, string title)
    {
        MessageBox.Show(Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowInfo(string message, string title)
    {
        MessageBox.Show(Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LogInfo(string phase, string message) => ActivityConsole.Add(ActivitySeverity.Info, phase, message);
    private void LogSuccess(string phase, string message) => ActivityConsole.Add(ActivitySeverity.Success, phase, message);
    private void LogError(string phase, string message) => ActivityConsole.Add(ActivitySeverity.Error, phase, message);
}

internal sealed class Paso4SummaryRowViewModel : INotifyPropertyChanged
{
    private readonly Action _onQuantityChanged;
    private string? _quantityText;

    public Paso4SummaryRowViewModel(Paso4StockGroupSummaryRow row, Action onQuantityChanged)
    {
        _onQuantityChanged = onQuantityChanged;
        Orden = row.Orden;
        NormalizedCodigoObraSocial = row.NormalizedCodigoObraSocial;
        NormalizedObraSocial = row.NormalizedObraSocial;
        CodigoObraSocialDisplay = Paso4ProcessUiLogic.ToDisplayValue(row.NormalizedCodigoObraSocial);
        ObraSocialDisplay = Paso4ProcessUiLogic.ToDisplayValue(row.NormalizedObraSocial);
        Total = row.Total;
        Sold = row.Sold;
        Available = row.Available;
        _quantityText = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Orden { get; }
    public string NormalizedCodigoObraSocial { get; }
    public string NormalizedObraSocial { get; }
    public string CodigoObraSocialDisplay { get; }
    public string ObraSocialDisplay { get; }
    public int Total { get; }
    public int Sold { get; }
    public int Available { get; }

    public string? QuantityText
    {
        get => _quantityText;
        set
        {
            if (string.Equals(_quantityText, value, StringComparison.Ordinal))
            {
                return;
            }

            _quantityText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QuantityText)));
            _onQuantityChanged();
        }
    }
}

internal sealed record Paso4CompletedExtractionDisplayItem(Guid Token, string Display);
