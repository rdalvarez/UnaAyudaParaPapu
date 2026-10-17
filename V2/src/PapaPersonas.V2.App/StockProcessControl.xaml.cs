using System.Windows;
using System.Windows.Controls;
using PapaPersonas.V2.Shell;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace PapaPersonas.V2.App;

public partial class StockProcessControl : System.Windows.Controls.UserControl
{
    private readonly record struct CompletedItem(string Id, string Text)
    {
        public override string ToString() => Text;
    }

    private StockSnapshotService? service;
    private Action<string>? report;
    private Func<bool>? isBusy;
    private Action<bool, string?>? setBusy;
    private int localBusy;
    private string? primaryKeyAlias;
    private StockHeader? currentHeader;
    private StockSourceIdentity? currentOfficialIdentity;
    private bool hasStock;
    private bool hasPending;
    private bool hasCompleted;
    private long refreshGeneration;

    public StockProcessControl()
    {
        InitializeComponent();
    }

    public void Attach(StockSnapshotService stockService, Action<string> reportActivity, Func<bool> operationIsBusy, Action<bool, string?> setGlobalBusy)
    {
        service = stockService;
        report = reportActivity;
        isBusy = operationIsBusy;
        setBusy = setGlobalBusy;
    }

    public void ApplyBusy() => ApplyState(StockPresentation.Evaluate(isBusy?.Invoke() == true, primaryKeyAlias is not null, hasStock, hasPending, hasCompleted));

    public async Task RefreshAsync()
    {
        if (service is null || localBusy > 0)
            return;

        var generation = Interlocked.Increment(ref refreshGeneration);
        try
        {
            var snapshot = await Task.Run(() =>
            {
                StockOfficialSource? official = null;
                var officialFailed = false;
                try
                {
                    official = service.GetOfficialSource();
                }
                catch
                {
                    officialFailed = true;
                }

                var summary = service.GetSummary();
                var pending = service.GetPending();
                var completed = service.ListCompleted();
                var columns = service.GetSnapshotColumns();
                return (official, officialFailed, summary, pending, completed, columns);
            });

            if (generation != refreshGeneration)
                return;

            primaryKeyAlias = snapshot.Item1?.CanGenerate == true ? snapshot.Item1.PrimaryKeyAlias : null;
            BindColumns(snapshot.Item1?.Columns ?? []);
            hasStock = snapshot.Item3 is not null;
            hasPending = snapshot.Item4 is not null;
            hasCompleted = snapshot.Item5.Count > 0;
            BindOrigin(snapshot.Item3?.Header, snapshot.Item1?.Identity, snapshot.Item2);
            BindSummary(snapshot.Item3, snapshot.Item4, snapshot.Item5, snapshot.Item6);
            ApplyBusy();
        }
        catch (Exception exception)
        {
            if (generation == refreshGeneration)
            {
                primaryKeyAlias = null;
                BindOriginUnavailable();
                ApplyBusy();
                report?.Invoke($"No se pudo actualizar el stock. {exception.Message}");
            }
        }
    }

    private async void GenerateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var validation = StockPresentation.ValidateGeneration(Selected(CodeComboBox), Selected(DescriptionComboBox));
        if (validation is not null || service is null || primaryKeyAlias is null)
        {
            Report(validation ?? "No hay una carga oficial lista para generar stock.");
            return;
        }

        var confirmation = hasStock ? StockPresentation.DestructiveConfirmation : StockPresentation.CreateConfirmation;
        if (WpfMessageBox.Show(confirmation, "Confirmar generación", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var request = new StockGenerateRequest(primaryKeyAlias, Selected(CodeComboBox)!, Selected(DescriptionComboBox)!, hasStock);
        var result = await RunAsync("Generando stock...", () => service.Generate(request));
        Report(result?.Message ?? (result?.Status == StockGenerateStatus.Completed ? "Stock generado." : "No se pudo generar el stock."));
        ClearGenerationSelection();
        await RefreshAsync();
    }

    private async void SaveNamesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CommitGrid() || service is null || GroupsGrid.ItemsSource is not IEnumerable<StockGroupRow> rows)
            return;

        var names = rows.ToDictionary(row => row.Code, row => row.Description, StringComparer.Ordinal);
        var saved = await RunAsync("Guardando nombres...", () =>
        {
            service.SaveCatalog(names);
            return true;
        });
        Report(saved == true ? "Nombres de stock guardados." : "No se pudieron guardar los nombres. Las ediciones siguen en pantalla.");
    }

    private async void SummaryButton_OnClick(object sender, RoutedEventArgs e) =>
        await ExportAsync(path => service!.ExportSummary(path));

    private async void DetailButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedExportColumns();
        var onlyAvailable = OnlyAvailableCheckBox.IsChecked == true;
        await ExportAsync(path => service!.ExportDetail(new StockDetailExportRequest(path, selected, onlyAvailable)));
    }

    private async void ExtractButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CommitGrid() || service is null || GroupsGrid.ItemsSource is not IEnumerable<StockGroupRow> rows)
            return;

        IReadOnlyList<StockGroupQuantity> quantities;
        try
        {
            quantities = StockPresentation.ParseQuantities(rows.Select(row => (row.Code, row.Quantity)).ToArray());
        }
        catch (Exception exception)
        {
            Report(exception.Message);
            return;
        }

        if (quantities.Count == 0)
        {
            Report("Seleccione al menos una cantidad positiva.");
            return;
        }

        var reference = ReferenceTextBox.Text.Trim();
        var total = quantities.Sum(item => item.Quantity);
        if (WpfMessageBox.Show($"Se extraerán {total} filas. Referencia: {reference}.\n\n¿Desea continuar?", "Confirmar extracción", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var path = ChooseSavePath();
        if (path is null)
            return;

        var selected = SelectedExportColumns();
        var result = await RunAsync("Extrayendo stock...", () => service.BeginExtraction(new StockExtractionRequest(path, selected, quantities, reference)));
        Report(StockPresentation.ResultMessage(result, "Extracción completada.", "No se pudo extraer el stock."));
        if (result?.Status == StockGenerateStatus.Completed)
            ReferenceTextBox.Clear();
        await RefreshAsync();
    }

    private async void RetryButton_OnClick(object sender, RoutedEventArgs e) => await RetryAsync(null);

    private async void RetryOtherButton_OnClick(object sender, RoutedEventArgs e)
    {
        var path = ChooseSavePath();
        if (path is not null)
            await RetryAsync(path);
    }

    private async void CancelPendingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (service is null || WpfMessageBox.Show("Se liberarán las reservas pendientes. No se eliminará un archivo ya publicado.\n\n¿Desea continuar?", "Cancelar pendiente", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var result = await RunAsync("Cancelando pendiente...", service.CancelPending);
        Report(StockPresentation.ResultMessage(result, "Pendiente cancelada.", "No se pudo cancelar la extracción pendiente."));
        await RefreshAsync();
    }

    private async void ReexportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (service is null || CompletedListBox.SelectedItem is not CompletedItem item)
            return;

        var path = ChooseSavePath();
        if (path is null)
            return;

        var result = await RunAsync("Reexportando...", () => service.ReexportCompleted(item.Id, path));
        Report(StockPresentation.ResultMessage(result, "Reexportación completada.", "No se pudo reexportar la extracción."));
        await RefreshAsync();
    }

    private async Task RetryAsync(string? alternatePath)
    {
        if (service is null)
            return;

        var result = await RunAsync("Reintentando extracción...", () => service.RetryPending(alternatePath));
        Report(StockPresentation.ResultMessage(result, "Extracción pendiente completada.", "No se pudo reintentar la extracción."));
        await RefreshAsync();
    }

    private async Task ExportAsync(Func<string, StockFileResult> export)
    {
        var path = ChooseSavePath();
        if (path is null || service is null)
            return;

        var result = await RunAsync("Exportando stock...", () => export(path));
        Report(StockPresentation.ResultMessage(result, "Exportación completada.", "No se pudo exportar el stock."));
    }

    private async Task<T?> RunAsync<T>(string status, Func<T> action)
    {
        if (Interlocked.Increment(ref localBusy) != 1 || isBusy?.Invoke() == true)
        {
            Interlocked.Decrement(ref localBusy);
            Report("Hay una operación en curso.");
            return default;
        }

        setBusy?.Invoke(true, status);
        report?.Invoke(status);
        try
        {
            return await Task.Run(action);
        }
        catch (Exception exception)
        {
            Report(exception.Message);
            return default;
        }
        finally
        {
            Interlocked.Decrement(ref localBusy);
            setBusy?.Invoke(false, null);
        }
    }

    private void BindColumns(IReadOnlyList<string> columns)
    {
        if (!columns.SequenceEqual(CodeComboBox.Items.Cast<string>()))
        {
            CodeComboBox.ItemsSource = columns;
            DescriptionComboBox.ItemsSource = columns;
            CodeComboBox.SelectedIndex = -1;
            DescriptionComboBox.SelectedIndex = -1;
        }

        PrimaryKeyTextBlock.Text = primaryKeyAlias is null
            ? "No hay una clave oficial lista."
            : $"Clave oficial: {primaryKeyAlias}";
    }

    private void BindOrigin(StockHeader? header, StockSourceIdentity? officialIdentity, bool officialUnavailable)
    {
        currentHeader = header;
        currentOfficialIdentity = officialIdentity;
        var model = StockPresentation.EvaluateOrigin(header, officialIdentity, officialUnavailable);

        StockOriginStatusTextBlock.Text = model.StatusText;
        StockFileTextBlock.Text = model.StockSourceText;
        OfficialFileTextBlock.Text = model.OfficialSourceText;

        if (string.IsNullOrWhiteSpace(model.GeneratedText))
        {
            StockGeneratedTextBlock.Visibility = Visibility.Collapsed;
            StockGeneratedTextBlock.Text = string.Empty;
        }
        else
        {
            StockGeneratedTextBlock.Visibility = Visibility.Visible;
            StockGeneratedTextBlock.Text = model.GeneratedText;
        }

        if (string.IsNullOrWhiteSpace(model.ExplanatoryText))
        {
            OriginWarningTextBlock.Visibility = Visibility.Collapsed;
            OriginWarningTextBlock.Text = string.Empty;
        }
        else
        {
            OriginWarningTextBlock.Visibility = Visibility.Visible;
            OriginWarningTextBlock.Text = model.ExplanatoryText;
        }
    }

    private void BindOriginUnavailable()
    {
        var model = StockPresentation.EvaluateOrigin(currentHeader, null, officialUnavailable: true);
        StockOriginStatusTextBlock.Text = currentHeader is not null ? model.StatusText : "Estado del stock no disponible";
        StockFileTextBlock.Text = currentHeader is not null ? model.StockSourceText : "Archivo del stock: Consulta no disponible";
        OfficialFileTextBlock.Text = model.OfficialSourceText;

        if (currentHeader is not null && !string.IsNullOrWhiteSpace(model.GeneratedText))
        {
            StockGeneratedTextBlock.Visibility = Visibility.Visible;
            StockGeneratedTextBlock.Text = model.GeneratedText;
        }
        else
        {
            StockGeneratedTextBlock.Visibility = Visibility.Collapsed;
            StockGeneratedTextBlock.Text = string.Empty;
        }

        OriginWarningTextBlock.Visibility = Visibility.Collapsed;
        OriginWarningTextBlock.Text = string.Empty;
    }

    private void BindSummary(StockSummary? summary, StockPendingExtraction? pending, IReadOnlyList<StockCompletedExtraction> completed, IReadOnlyList<string> columns)
    {
        var catalogWarnings = summary is null ? string.Empty : string.Join(" ", summary.CatalogWarnings);
        CatalogWarningTextBlock.Text = catalogWarnings;
        CatalogWarningTextBlock.Visibility = string.IsNullOrWhiteSpace(catalogWarnings) ? Visibility.Collapsed : Visibility.Visible;

        CountsTextBlock.Text = summary is null ? string.Empty : $"Total: {summary.Total}. Vendido: {summary.Sold}. Disponible: {summary.Available}.";
        GroupsGrid.ItemsSource = summary?.Groups.Select(group => new StockGroupRow(group.NormalizedCode, group.DisplayName, group.Total, group.Sold, group.Reserved, group.Available)).ToArray();
        PendingTextBlock.Text = pending is null
            ? "No hay una extracción pendiente."
            : $"Pendiente: {pending.ExpectedRows} filas. Referencia: {pending.SaleReference}. Ruta: {pending.OutputPath}";
        CompletedListBox.ItemsSource = completed.Select(item => new CompletedItem(item.ExtractionId, $"{item.CompletedUtc:g} — {item.ExpectedRows} — {item.SaleReference}")).ToArray();
        var previous = ExportColumnsListBox.Items.Cast<WpfCheckBox>().Select(item => ((string)item.Content, item.IsChecked == true)).ToArray();
        var mandatory = new[] { summary?.Header.PrimaryKeyAlias, summary?.Header.CodeAlias, summary?.Header.DescriptionAlias }.Where(alias => alias is not null).Cast<string>().ToArray();
        var next = StockPresentation.BuildExportColumns(columns, mandatory, previous);
        if (!previous.Select(item => item.Item1).SequenceEqual(next.Select(item => item.Name)))
        {
            ExportColumnsListBox.Items.Clear();
            foreach (var column in next)
            {
                var checkBox = new WpfCheckBox { Content = column.Name, IsChecked = column.IsSelected, IsEnabled = !column.IsMandatory };
                if (column.IsMandatory)
                    checkBox.Checked += (_, _) => checkBox.IsChecked = true;
                ExportColumnsListBox.Items.Add(checkBox);
            }
        }
    }

    private void ApplyState(StockUiState state)
    {
        StatusTextBlock.Text = state.StatusText ?? string.Empty;
        StatusTextBlock.Visibility = string.IsNullOrWhiteSpace(StatusTextBlock.Text) ? Visibility.Collapsed : Visibility.Visible;
        GenerateButton.IsEnabled = state.CanGenerate;
        SummaryButton.IsEnabled = state.CanExport;
        DetailButton.IsEnabled = state.CanExport;
        ExtractButton.IsEnabled = state.CanExtract;
        RetryButton.IsEnabled = state.CanRetryPending;
        RetryOtherButton.IsEnabled = state.CanRetryPending;
        CancelPendingButton.IsEnabled = state.CanCancelPending;
        ReexportButton.IsEnabled = state.CanReexport;
        SaveNamesButton.IsEnabled = state.CanSaveNames;
    }

    private void ClearGenerationSelection()
    {
        CodeComboBox.SelectedIndex = -1;
        DescriptionComboBox.SelectedIndex = -1;
    }

    private bool CommitGrid() =>
        GroupsGrid.CommitEdit(DataGridEditingUnit.Cell, true) && GroupsGrid.CommitEdit(DataGridEditingUnit.Row, true);

    private string[] SelectedExportColumns() =>
        ExportColumnsListBox.Items.Cast<WpfCheckBox>().Where(item => item.IsChecked == true).Select(item => (string)item.Content).ToArray();

    private void Report(string message) => report?.Invoke(message);

    private static string? Selected(System.Windows.Controls.ComboBox comboBox) => comboBox.SelectedItem as string;

    private string? ChooseSavePath()
    {
        var dialog = new WpfSaveFileDialog { Filter = "CSV (*.csv)|*.csv", OverwritePrompt = false, CheckFileExists = false };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private sealed class StockGroupRow(string code, string description, long total, long sold, long reserved, long available)
    {
        public string Code { get; } = code;
        public string Description { get; set; } = description;
        public long Total { get; } = total;
        public long Sold { get; } = sold;
        public long Reserved { get; } = reserved;
        public long Available { get; } = available;
        public string? Quantity { get; set; }
    }
}
