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
    private ColumnSelectionSettingsDocument _columnSelectionDocument = new();
    private string? _rejectedFolderPath;
    private IReadOnlyList<string?> _rawHeaders = [];
    private IReadOnlyList<OutputColumnSelection> _outputColumns = [];
    private string? _acceptedSergioFilePath;
    private SergioHeaderPreflightResult? _acceptedSergioHeaders;
    private bool _isBusy;
    private readonly SergioStagingAnalyzer _sergioAnalyzer = new();
    private readonly SergioLoadPromoter _sergioLoadPromoter;
    private readonly SergioRejectedExporter _sergioRejectedExporter;
    private string? _sergioCandidateTable;
    private string? _sergioExportSourceTable;
    private SergioStagingResult? _sergioStagingResult;
    private bool _sergioRejectionContinuationAccepted;

    public MainWindow()
    {
        InitializeComponent();
        _sergioLoadPromoter = new SergioLoadPromoter(_sergioAnalyzer);
        _sergioRejectedExporter = new SergioRejectedExporter(_sergioAnalyzer);
        ApplyTabCatalog();
        RejectedFolderTextBox.Cursor = System.Windows.Input.Cursors.Hand;
        RejectedFolderTextBox.MouseLeftButtonUp += (_, _) => OpenRejectedFolder();
        Closing += (_, _) =>
        {
            try { _sergioAnalyzer.CleanupAll(); }
            catch (Exception exception) { AddActivity("Paso 2", $"Advertencia: no se pudo limpiar candidato Sergio al cerrar: {exception.Message}"); }
        };
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
            _columnSelectionDocument = loadedSettings.Item2.Document;

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

    private async void SergioBrowseFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog { Filter = "Archivos de Excel (*.xlsx;*.xls)|*.xlsx;*.xls", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;

        var path = dialog.FileName;
        try
        {
            CleanupSergioCandidate();
            InvalidateSergioInteractiveState();
            SetBusy(true, "Leyendo encabezados de Sergio...");
            var rawHeaders = await Task.Run(() => _headerReader.ReadHeaders(path).ToArray());
            var result = SergioHeaderPreflight.Analyze(rawHeaders);
            if (result.HasIssues)
            {
                var issueDialog = new SergioHeaderIssueDialog(result) { Owner = this };
                if (issueDialog.ShowDialog() != true || !issueDialog.Accepted)
                {
                    ClearAcceptedSergioState();
                    AddActivity("Paso 2", "Lectura de Sergio cancelada por el usuario.");
                    return;
                }
            }

            AcceptSergioState(path, result);
            AddActivity("Paso 2", $"Archivo de Sergio seleccionado: {Path.GetFileName(path)}");
            AddActivity("Paso 2", $"Encabezados de Sergio detectados: {result.Columns.Count}.");
            if (SergioPrimaryKeyComboBox.SelectedItem is null)
                AddActivity("Paso 2", "Advertencia: no se encontró el encabezado exacto CUIL; la clave primaria quedó sin seleccionar.");
        }
        catch (Exception exception)
        {
            ClearAcceptedSergioState();
            AddActivity("Paso 2", $"Error al leer encabezados de Sergio: {exception.Message}");
            System.Windows.MessageBox.Show(this, $"No se pudieron leer los encabezados del archivo de Sergio.\n\n{exception.Message}", "No se pudo cargar el archivo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AcceptSergioState(string path, SergioHeaderPreflightResult result)
    {
        _acceptedSergioFilePath = path;
        _acceptedSergioHeaders = result;
        SergioFileTextBox.Text = path;
        SergioHeadersListBox.ItemsSource = result.Columns.Select(column => $"{column.Position + 1}. {DisplayHeader(column.RawName)} → {column.ProposedAlias}").ToArray();
        SergioPrimaryKeyComboBox.ItemsSource = SergioPrimaryKeySelection.Options(result.Columns);
        SergioPrimaryKeyComboBox.SelectedItem = SergioPrimaryKeySelection.DefaultAlias(result.Columns);
        SergioPrimaryKeyComboBox.IsEnabled = true;
        SergioSummaryTextBlock.Text = result.HasReservedTechnicalAlias
            ? $"Advertencia: la columna técnica {SergioSchemaNames.RejectionReason} del archivo será ignorada y reutilizada en staging; el archivo original no se modifica. Seleccione la clave primaria y valide."
            : "Estado: encabezados aceptados; seleccione la clave primaria y valide.";
        UpdateSergioControls();
    }

    private void ClearAcceptedSergioState()
    {
        CleanupSergioCandidate();
        InvalidateSergioInteractiveState();
        _acceptedSergioFilePath = null;
        _acceptedSergioHeaders = null;
        SergioFileTextBox.Clear();
        SergioHeadersListBox.ItemsSource = null;
        SergioPrimaryKeyComboBox.ItemsSource = null;
        SergioPrimaryKeyComboBox.SelectedItem = null;
        SergioPrimaryKeyComboBox.IsEnabled = false;
        SergioSummaryTextBlock.Text = "Estado: falta aceptar archivo, encabezados y clave primaria.";
        UpdateSergioControls();
    }

    private static string DisplayHeader(string? value) => string.IsNullOrEmpty(value) ? "<vacío>" : value.Replace("\r", "\\r").Replace("\n", "\\n");

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
        SergioBrowseFileButton.IsEnabled = !isBusy;
        SergioPrimaryKeyComboBox.IsEnabled = !isBusy && _acceptedSergioHeaders is not null;
        SergioValidateButton.IsEnabled = !isBusy && _acceptedSergioHeaders is not null && SergioPrimaryKeyComboBox.SelectedItem is string;
        SergioPreviewButton.IsEnabled = !isBusy && _sergioCandidateTable is not null && (_sergioStagingResult?.ValidRows ?? 0) > 0;
        SergioExportRejectedButton.IsEnabled = !isBusy && HasCurrentSergioExportSource();
        BrowseFolderButton.IsEnabled = !isBusy;
        RejectedFolderLabel.IsEnabled = !isBusy;
        RejectedFolderTextBox.IsEnabled = !isBusy;
        DetectedColumnsListBox.IsEnabled = !isBusy;
        UpdateCleaningControls();
    }

    private void CleaningColumnComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCleaningControls();

    private void SergioPrimaryKeyComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CleanupSergioCandidate();
        InvalidateSergioInteractiveState();
        SergioSummaryTextBlock.Text = "Estado: clave modificada; vuelva a validar la carga.";
        UpdateSergioControls();
    }

    private async void SergioValidateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _acceptedSergioHeaders is null || _acceptedSergioFilePath is null || SergioPrimaryKeyComboBox.SelectedItem is not string selectedKey) return;
        var request = new SergioStagingRequest(_acceptedSergioFilePath, _acceptedSergioHeaders, selectedKey);
        CleanupSergioCandidate();
        InvalidateSergioInteractiveState();
        SetBusy(true, "Validando y preparando carga de Sergio...");
        try
        {
            var result = await Task.Run(() => _sergioAnalyzer.Analyze(request));
            _sergioCandidateTable = result.CandidateTableName;
            _sergioStagingResult = result;
            _sergioExportSourceTable = result.CandidateTableName;
            _sergioRejectionContinuationAccepted = !result.HasRejections;
            var presentation = SergioInteractionPolicy.For(result);
            SergioSummaryTextBlock.Text = $"PK: {presentation.SelectedPrimaryKey}. Importadas: {presentation.ImportedRows}. Válidas: {presentation.ValidRows}. Filas vacías: {presentation.EmptyRows}. Grupos duplicados: {presentation.DuplicateGroups}. Filas duplicadas: {presentation.DuplicateRows}. Rechazadas: {presentation.RejectedRows}.";
            AddActivity("Paso 2", $"Validación Sergio: filas {result.TotalRows}, válidas {result.ValidRows}, vacías {result.EmptyPrimaryKeyRows}, grupos duplicados {result.DuplicateGroups}, filas duplicadas {result.DuplicateRows}, candidato retenido.");
            if (presentation.DecisionRequired)
                _sergioRejectionContinuationAccepted = AskToContinue(presentation);
            else if (presentation.ValidRows == 0)
                System.Windows.MessageBox.Show(this, SergioInteractionPolicy.ZeroValidStatusMessage(presentation), "Carga de Sergio sin filas válidas", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            AddActivity("Paso 2", $"Error al validar Sergio: {exception.Message}");
            SergioSummaryTextBlock.Text = "Estado: no se pudo preparar la carga; no se retuvo candidato.";
            System.Windows.MessageBox.Show(this, $"No se pudo validar y preparar la carga de Sergio.\n\n{exception.Message}", "No se pudo validar Sergio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async void SergioPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _sergioCandidateTable is null || _acceptedSergioFilePath is null || _sergioStagingResult is null || _sergioStagingResult.ValidRows == 0 || SergioPrimaryKeyComboBox.SelectedItem is not string selectedKey) return;
        var presentation = SergioInteractionPolicy.For(_sergioStagingResult);
        if (presentation.DecisionRequired && !_sergioRejectionContinuationAccepted)
        {
            _sergioRejectionContinuationAccepted = AskToContinue(presentation);
            if (!_sergioRejectionContinuationAccepted) return;
        }
        var candidate = _sergioCandidateTable;
        var sourcePath = _acceptedSergioFilePath;
        SetBusy(true, "Paso 2: preparando vista previa...");
        try
        {
            var preview = await Task.Run(() => _sergioLoadPromoter.Preview(candidate, selectedKey, sourcePath));
            var dialog = new SergioLoadPreviewDialog(preview) { Owner = this };
            if (dialog.ShowDialog() != true || !dialog.Confirmed) return;
            SetBusy(true, "Paso 2: publicando carga oficial...");
            await Task.Run(() => _sergioLoadPromoter.Promote(candidate, selectedKey, sourcePath));
            _sergioCandidateTable = null;
            _sergioExportSourceTable = SergioLoadPromoter.LastImportTableName;
            SergioSummaryTextBlock.Text = $"Carga oficial actualizada: {preview.ValidRows} filas válidas de {preview.SourceFileName}. Rechazadas retenidas: {preview.RejectedRows}.";
            AddActivity("Paso 2", $"Carga oficial de Sergio actualizada: archivo {preview.SourceFileName}, filas válidas {preview.ValidRows}, rechazadas retenidas {preview.RejectedRows}, fecha {DateTime.Now:g}.");
        }
        catch (Exception exception)
        {
            SergioSummaryTextBlock.Text = "No se actualizó la carga oficial; el candidato queda disponible para reintentar.";
            AddActivity("Paso 2", $"Error al publicar carga Sergio: {exception.Message}");
            System.Windows.MessageBox.Show(this, $"No se pudo actualizar la carga oficial de Sergio. El candidato queda disponible para reintentar.\n\nDetalle técnico: {exception.Message}", "No se pudo cargar Sergio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void CleanupSergioCandidate()
    {
        if (_sergioCandidateTable is null) return;
        try { _sergioAnalyzer.Cleanup(_sergioCandidateTable); }
        catch (Exception exception) { AddActivity("Paso 2", $"Advertencia: no se pudo limpiar candidato Sergio: {exception.Message}"); }
        finally { _sergioCandidateTable = null; }
    }

    private void InvalidateSergioInteractiveState()
    {
        _sergioStagingResult = null;
        _sergioRejectionContinuationAccepted = false;
        _sergioExportSourceTable = null;
    }

    private bool HasCurrentSergioExportSource() =>
        _sergioExportSourceTable is not null &&
        _sergioStagingResult is not null &&
        _sergioStagingResult.HasRejections &&
        _acceptedSergioFilePath is not null;

    private bool AskToContinue(SergioStagingPresentation presentation) =>
        new SergioRejectionDecisionDialog(presentation) { Owner = this }.ShowDialog() == true;

    private async void SergioExportRejectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || !HasCurrentSergioExportSource()) return;
        var source = _sergioExportSourceTable!;
        var workbookPath = _acceptedSergioFilePath!;
        SetBusy(true, "Paso 2: exportando filas rechazadas...");
        try
        {
            var exported = await Task.Run(() => _sergioRejectedExporter.Export(source, workbookPath));
            if (!exported.HasOutput || string.IsNullOrWhiteSpace(exported.OutputPath))
            {
                AddActivity("Paso 2", "No había filas rechazadas para exportar.");
                SergioSummaryTextBlock.Text = "No había filas rechazadas para exportar.";
                return;
            }
            AddActivity("Paso 2", $"Rechazados exportados: {exported.RejectedCount}. Salida completa: {exported.OutputPath}");
            SergioSummaryTextBlock.Text = $"Se exportaron {exported.RejectedCount} filas rechazadas. Salida: {exported.OutputPath}";
        }
        catch (Exception exception)
        {
            AddActivity("Paso 2", $"Error al exportar rechazados: {exception.Message}");
            System.Windows.MessageBox.Show(this, $"No se pudieron exportar los rechazados. Verifique que el archivo de origen siga disponible y vuelva a intentar.\n\nDetalle técnico: {exception.Message}", "No se pudo exportar rechazados", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void UpdateSergioControls()
    {
        SergioValidateButton.IsEnabled = !_isBusy && File.Exists(_acceptedSergioFilePath) && _acceptedSergioHeaders?.Columns.Count > 0 && SergioPrimaryKeyComboBox.SelectedItem is string;
        SergioPreviewButton.IsEnabled = !_isBusy && _sergioCandidateTable is not null && (_sergioStagingResult?.ValidRows ?? 0) > 0;
        SergioExportRejectedButton.IsEnabled = !_isBusy && HasCurrentSergioExportSource();
    }

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
                _columnSelectionDocument.ConfiguracionPaso1.DeselectedColumnNames = deselectedNames.ToList();
                _columnSelectionSettings.Save(_columnSelectionDocument);
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

    private void AddActivity(string message) => AddActivity("Paso 1", message);

    private void AddActivity(string section, string message)
    {
        _activityLog.Add(section, message);
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
