using System.IO;
using System.Windows;
using System.Windows.Controls;
using PapaPersonas.V2.Shell;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace PapaPersonas.V2.App;

public partial class MainWindow : Window
{
    private sealed class OutputColumnSelection(DetectedColumn column)
    {
        public DetectedColumn Column { get; } = column;
        public bool IsSelected { get; set; } = true;
    }

    private readonly IHeaderReader _headerReader = new ExcelDataReaderHeaderReader();
    private readonly ActivityLog _activityLog = new();
    private readonly CleaningPlan _cleaningPlan = new();
    private readonly CleaningExecutionCoordinator _executionCoordinator = new();
    private readonly ColumnSelectionSettingsStore _columnSelectionSettings = new();
    private string? _rejectedFolderPath;
    private IReadOnlyList<string?> _rawHeaders = [];
    private IReadOnlyList<OutputColumnSelection> _outputColumns = [];
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTabCatalog();
        RejectedFolderTextBox.Cursor = System.Windows.Input.Cursors.Hand;
        RejectedFolderTextBox.MouseLeftButtonUp += (_, _) => OpenRejectedFolder();
    }

    private void ApplyTabCatalog()
    {
        TabItem[] shellTabs = [
            PrepararTab,
            SergioTab,
            StockTab,
            ConsultasTab
        ];

        if (shellTabs.Length != TabCatalog.All.Count)
        {
            throw new InvalidOperationException("The shell tab count must match the tab catalog.");
        }

        for (var index = 0; index < shellTabs.Length; index++)
        {
            var definition = TabCatalog.All[index];
            shellTabs[index].Header = definition.Title;
            shellTabs[index].IsEnabled = definition.IsEnabled;
        }
    }

    private async void BrowseFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "Archivos de Excel (*.xlsx;*.xls)|*.xlsx;*.xls",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SetBusy(true, "Leyendo encabezados...");
            var path = dialog.FileName;
            var rejectedFolder = RejectedFolderPolicy.GetDefault(path);
            var loadedSettings = await Task.Run(() =>
            {
                Directory.CreateDirectory(rejectedFolder);
                return (_headerReader.ReadHeaders(path).ToArray(), _columnSelectionSettings.Load());
            });
            var rawHeaders = loadedSettings.Item1;
            var columns = HeaderPresentation.Normalize(rawHeaders);

            InputFileTextBox.Text = path;
            RejectedFolderTextBox.Text = rejectedFolder;
            _rejectedFolderPath = rejectedFolder;
            ResetCleaningPlan();
            _rawHeaders = rawHeaders;
            SetColumns(columns, loadedSettings.Item2.Document.ConfiguracionPaso1.DeselectedColumnNames);
            AddActivity($"Archivo seleccionado: {path}");
            AddActivity($"Carpeta de rechazados: {rejectedFolder}");
            AddActivity($"Columnas detectadas: {columns.Count}");
            AddActivity($"Configuración de columnas cargada: {_columnSelectionSettings.Path}");
            if (loadedSettings.Item2.WasReset)
            {
                AddActivity($"Advertencia: configuración corrupta reemplazada y restablecida: {_columnSelectionSettings.Path}");
            }
        }
        catch (Exception exception)
        {
            AddActivity("Error amigable: no se pudo preparar el archivo seleccionado.");
            System.Windows.MessageBox.Show(this, $"No se pudo leer el archivo o crear la carpeta de rechazados.\n\n{exception.Message}", "No se pudo cargar el archivo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BrowseFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = _rejectedFolderPath ?? string.Empty,
            Description = "Seleccione la carpeta de rechazados"
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(dialog.SelectedPath);
            _rejectedFolderPath = dialog.SelectedPath;
            RejectedFolderTextBox.Text = dialog.SelectedPath;
            AddActivity($"Carpeta de rechazados actualizada: {dialog.SelectedPath}");
        }
        catch (Exception exception)
        {
            AddActivity("Error amigable: no se pudo crear la carpeta de rechazados.");
            System.Windows.MessageBox.Show(this, $"No se pudo crear la carpeta seleccionada.\n\n{exception.Message}", "No se pudo seleccionar la carpeta", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetColumns(IReadOnlyList<DetectedColumn> columns, IReadOnlyList<string> deselectedNames)
    {
        _outputColumns = columns.Select(column => new OutputColumnSelection(column)).ToArray();
        var applied = ColumnSelectionMemory.ApplyDeselected(
            columns,
            deselectedNames,
            (column, isSelected) => _outputColumns.First(item => item.Column == column).IsSelected = isSelected);
        DetectedColumnsListBox.ItemsSource = _outputColumns;
        DuplicateColumnComboBox.ItemsSource = columns;
        EmptyColumnComboBox.ItemsSource = columns;
        DuplicateColumnComboBox.IsEnabled = columns.Count > 0;
        EmptyColumnComboBox.IsEnabled = columns.Count > 0;
        UpdateCleaningControls();
        AddActivity($"Preferencias de columnas aplicadas: {applied}.");
    }

    private void OutputColumnCheckBox_OnChanged(object sender, RoutedEventArgs e) => UpdateCleaningControls();

    private void SetBusy(bool isBusy, string? status = null)
    {
        _isBusy = isBusy;
        OperationProgressBar.IsIndeterminate = isBusy;
        OperationProgressBar.Value = 0;
        OperationStatusTextBlock.Text = isBusy ? status ?? "Procesando..." : "Sin operaciones en curso.";
        BrowseFileButton.IsEnabled = !isBusy;
        BrowseFolderButton.IsEnabled = !isBusy;
        RejectedFolderLabel.IsEnabled = !isBusy;
        RejectedFolderTextBox.IsEnabled = !isBusy;
        DetectedColumnsListBox.IsEnabled = !isBusy;
        UpdateCleaningControls();
    }

    private void CleaningColumnComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCleaningControls();

    private void RemoveDuplicatesButton_OnClick(object sender, RoutedEventArgs e) =>
        AddCleaningStep(CleaningAction.RemoveDuplicates, DuplicateColumnComboBox.SelectedItem as DetectedColumn);

    private void RemoveEmptyValuesButton_OnClick(object sender, RoutedEventArgs e) =>
        AddCleaningStep(CleaningAction.RemoveEmptyValues, EmptyColumnComboBox.SelectedItem as DetectedColumn);

    private void AddCleaningStep(CleaningAction action, DetectedColumn? column)
    {
        if (column is null)
        {
            return;
        }

        var step = _cleaningPlan.Add(action, column);
        RefreshCleaningPlanPresentation();
        AddActivity($"Paso de limpieza agregado: {step.VisibleDescription}");
    }

    private void ClearCleaningPlanButton_OnClick(object sender, RoutedEventArgs e)
    {
        var removed = _cleaningPlan.Clear();
        RefreshCleaningPlanPresentation();
        AddActivity($"Plan de limpieza limpiado: {removed} pasos eliminados.");
    }

    private void ResetCleaningPlan()
    {
        var removed = _cleaningPlan.Clear();
        DuplicateColumnComboBox.SelectedItem = null;
        EmptyColumnComboBox.SelectedItem = null;
        _rawHeaders = [];
        RefreshCleaningPlanPresentation();
        if (removed > 0)
        {
            AddActivity($"Plan de limpieza reiniciado: {removed} pasos eliminados.");
        }
    }

    private void RefreshCleaningPlanPresentation()
    {
        CleaningPlanListBox.ItemsSource = null;
        CleaningPlanListBox.ItemsSource = _cleaningPlan.Steps;
        CleaningPlanSummaryTextBlock.Text = _cleaningPlan.Steps.Count == 0
            ? "Plan vacío."
            : $"Plan de limpieza: {_cleaningPlan.Steps.Count} paso(s).";
        UpdateCleaningControls();
    }

    private void UpdateCleaningControls()
    {
        var isBusy = _isBusy;
        DuplicateColumnComboBox.IsEnabled = !isBusy && DuplicateColumnComboBox.Items.Count > 0;
        EmptyColumnComboBox.IsEnabled = !isBusy && EmptyColumnComboBox.Items.Count > 0;
        RemoveDuplicatesButton.IsEnabled = !isBusy && DuplicateColumnComboBox.SelectedItem is DetectedColumn;
        RemoveEmptyValuesButton.IsEnabled = !isBusy && EmptyColumnComboBox.SelectedItem is DetectedColumn;
        ClearCleaningPlanButton.IsEnabled = !isBusy && _cleaningPlan.Steps.Count > 0;
        GenerateOutputButton.IsEnabled = !isBusy &&
            !string.IsNullOrWhiteSpace(InputFileTextBox.Text) && File.Exists(InputFileTextBox.Text) &&
            !string.IsNullOrWhiteSpace(_rejectedFolderPath) && _rawHeaders.Count > 0 && _cleaningPlan.Steps.Count > 0 &&
            _outputColumns.Any(column => column.IsSelected);
    }

    private async void GenerateOutputButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || string.IsNullOrWhiteSpace(InputFileTextBox.Text) ||
            string.IsNullOrWhiteSpace(_rejectedFolderPath) || _rawHeaders.Count == 0 || _cleaningPlan.Steps.Count == 0)
            return;

        var inputPath = InputFileTextBox.Text;
        var rejectedFolderPath = _rejectedFolderPath;
        var rawHeaders = _rawHeaders.ToArray();
        var visibleSteps = _cleaningPlan.Steps.ToArray();
        var selectedPositions = _outputColumns.Where(column => column.IsSelected)
            .Select(column => column.Column.Position).OrderBy(position => position).ToArray();
        AddActivity($"Columnas incluidas en la salida limpia: {selectedPositions.Length} de {_rawHeaders.Count}.");
        AddActivity($"Validando plan: {visibleSteps.Length} pasos recibidos.");
        SetBusy(true, "Procesando archivo...");
        CycleSummaryTextBlock.Text = "Procesando archivo...";
        try
        {
            var result = await Task.Run(() => _executionCoordinator.Execute(
                new CleaningExecutionRequest(inputPath, rejectedFolderPath, rawHeaders, visibleSteps, selectedPositions)));
            foreach (var omitted in result.OmittedSteps)
                AddActivity($"Paso omitido: {omitted.Step.VisibleDescription}. Razón: {omitted.Reason}.");
            foreach (var cycle in result.Cycles)
                AddActivity($"Ciclo {cycle.StepNumber}: {GetCleaningActionName(cycle.Action)} por {cycle.ColumnName}; entrada {cycle.InputRows}, rechazadas {cycle.RejectedRows}, restantes {cycle.RemainingRows}.");
            AddActivity($"Total rechazado: {result.TotalRejectedRows}. Total restante: {result.RemainingRows}.");
            AddActivity($"Salida limpia: {result.CleanOutputPath}");
            AddActivity($"Salida de rechazados: {result.RejectedOutputPath}");
            if (result.CleanupWarning is not null) AddActivity($"Advertencia de limpieza temporal: {result.CleanupWarning}");
            CycleSummaryTextBlock.Text = $"Proceso completado. Rechazadas: {result.TotalRejectedRows}. Restantes: {result.RemainingRows}.";
            var deselectedNames = ColumnSelectionMemory.CaptureDeselected(
                _outputColumns.Select(column => column.Column).ToArray(),
                column => _outputColumns.First(item => item.Column == column).IsSelected);
            try
            {
                _columnSelectionSettings.Save(new ColumnSelectionSettingsDocument
                {
                    ConfiguracionPaso1 = new Paso1ColumnSelectionSettings
                    {
                        DeselectedColumnNames = deselectedNames.ToList()
                    }
                });
                AddActivity($"Configuración de columnas guardada: {_columnSelectionSettings.Path}");
            }
            catch (Exception settingsException)
            {
                AddActivity($"Advertencia: no se pudo guardar la configuración de columnas: {settingsException.Message}");
                System.Windows.MessageBox.Show(this, $"Los archivos de salida se generaron correctamente, pero no se pudo guardar la selección de columnas.\n\nDetalle técnico: {settingsException.Message}", "No se pudo guardar la configuración", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            new CompletionDialog(result, inputPath) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            AddActivity($"Error al procesar el archivo: {exception.Message}");
            CycleSummaryTextBlock.Text = $"Falló el procesamiento. {exception.Message}";
            System.Windows.MessageBox.Show(this, $"No se pudo procesar el archivo.\n\nDetalle técnico: {exception.Message}\n\nSi el libro cambió, vuelva a seleccionarlo y recargue sus encabezados. No se reintentará automáticamente.", "No se pudo generar el archivo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AddActivity(string message)
    {
        _activityLog.Add("Paso 1", message);
        ActivityTextBox.Text = string.Join(Environment.NewLine, _activityLog.Entries);
    }

    private static string GetCleaningActionName(CleaningAction action) => action switch
    {
        CleaningAction.RemoveDuplicates => "Quitar duplicados",
        CleaningAction.RemoveEmptyValues => "Quitar valores vacíos",
        _ => action.ToString()
    };

    private void OpenRejectedFolder()
    {
        if (string.IsNullOrWhiteSpace(_rejectedFolderPath) || !Directory.Exists(_rejectedFolderPath))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _rejectedFolderPath,
                UseShellExecute = true
            });
        }
        catch
        {
            AddActivity("Error amigable: no se pudo abrir la carpeta de rechazados.");
            System.Windows.MessageBox.Show(this, "No se pudo abrir la carpeta de rechazados.", "No se pudo abrir la carpeta", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RejectedFolderLabel_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenRejectedFolder();
    }
}
