using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PapaPersonas.App.Ui;
using PapaPersonas.Core.Activity;
using PapaPersonas.Core.App;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso1;
using PapaPersonas.Infrastructure.Import.Paso1;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace PapaPersonas.App.Processes;

public partial class Paso1ProcessControl : System.Windows.Controls.UserControl
{
    private const string BusyOwner = "Paso1";

    private readonly HernanPaso1Processor _processor = new();
    private readonly ActivityBuffer _activity = new(maxMessages: 500);
    private ProcessBusyCoordinator? _busyCoordinator;
    private bool _isLocalBusy;
    private string? _lastOutputDirectory;

    public Paso1ProcessControl()
    {
        InitializeComponent();
        ActivityConsole.AttachBuffer(_activity);
        InitializeDefaults();
        LogInfo("Paso 1", "Consola iniciada para esta sesión de la app.");
    }

    public void ConfigureCoordinator(ProcessBusyCoordinator coordinator)
    {
        _busyCoordinator = coordinator;
        _busyCoordinator.BusyStateChanged += BusyCoordinator_BusyStateChanged;
        UpdateControlState();
    }

    private void BusyCoordinator_BusyStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateControlState);
    }

    private void InitializeDefaults()
    {
        var appBaseDirectory = AppContext.BaseDirectory;
        var documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var localAppDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var defaults = Paso1RuntimePathResolver.Resolve(appBaseDirectory, documentsDirectory, localAppDataDirectory);
        ConfigPathTextBox.Text = defaults.DefaultConfigPath;
        OutputPathTextBox.Text = defaults.DefaultOutputDirectory;
        SummaryText.Text = "Todavía no se ejecutó el proceso.";
    }

    // Ejecuta Paso 1 después de validar entradas y coordina su progreso y resultado en la interfaz.
    private async void RunPaso1_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginWork())
        {
            return;
        }

        ActivityConsole.ClearActivity();
        LogInfo("Paso 1", "Inicio de ejecución. Se limpia la consola de este proceso para la nueva corrida.");

        var inputPath = InputPathTextBox.Text.Trim();
        var outputDirectory = OutputPathTextBox.Text.Trim();
        var configPath = ConfigPathTextBox.Text.Trim();

        if (!ValidateInputs(inputPath, outputDirectory, configPath))
        {
            EndWork();
            return;
        }

        var configLoad = HernanPaso1JsonConfigLoader.LoadFromFile(configPath);
        if (!configLoad.IsSuccess || configLoad.Config is null)
        {
            RunStatusText.Foreground = StatusBrushes.Error;
            RunStatusText.Text = "La configuración no es válida.";
            SummaryText.Text = configLoad.Error ?? "No se pudo cargar el archivo de configuración.";
            LogError("Paso 1", "Configuración inválida. Se bloquea la ejecución hasta corregir JSON/mapeos.");
            EndWork();
            return;
        }

        RunStatusText.Foreground = StatusBrushes.Info;
        RunStatusText.Text = "Procesando Paso 1...";
        SummaryText.Text = "En ejecución. Esperá...";
        LogInfo("Paso 1", "Validaciones locales OK. Inicia procesamiento asíncrono.");

        var canOpenOutputFolder = false;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!TryEnsureOutputDirectory(outputDirectory))
            {
                return;
            }

            LogInfo("Paso 1", "Carpeta de salida preparada. Ejecutando procesamiento principal.");

            var request = new HernanPreparationRequest(
                InputFilePath: inputPath,
                OutputDirectory: outputDirectory,
                Config: configLoad.Config);

            var result = await Task.Run(() => _processor.Process(request));

            if (result.IsSuccess)
            {
                _lastOutputDirectory = outputDirectory;
                canOpenOutputFolder = true;
                RunStatusText.Foreground = StatusBrushes.Success;
                RunStatusText.Text = "Paso 1 completado.";
                SummaryText.Text = BuildSuccessSummary(result, stopwatch.Elapsed);
                LogSuccess("Paso 1", $"Completado. Filas: {result.Summary.TotalRows}, válidas: {result.Summary.ValidRows}, rechazadas: {result.Summary.RejectedRows}.");
                return;
            }

            RunStatusText.Foreground = StatusBrushes.Error;
            RunStatusText.Text = result.IsValidationFailure ? "Paso 1 bloqueado por validación." : "Paso 1 falló.";
            SummaryText.Text = BuildFailureSummary(result, stopwatch.Elapsed);
            LogError("Paso 1", $"Finalizó con errores. Errores: {result.Errors.Count}, avisos: {result.Notices.Count}.");
        }
        catch (Exception ex)
        {
            RunStatusText.Foreground = StatusBrushes.Error;
            RunStatusText.Text = UserFacingExceptionMessage.WithTechnicalDetail("Paso 1 falló.", ex);
            SummaryText.Text = UserFacingExceptionMessage.WithTechnicalDetail(
                "Ocurrió un error al procesar. Verificá el archivo de entrada y la configuración, y volvé a intentar.",
                ex);
            LogError(
                "Paso 1",
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "Error inesperado durante el procesamiento. No se muestran valores de filas.",
                    ex));
        }
        finally
        {
            stopwatch.Stop();
            OpenOutputFolderButton.IsEnabled = canOpenOutputFolder;
            EndWork();
        }
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        if (IsInteractionBlocked())
        {
            return;
        }

        using var dialog = new Forms.OpenFileDialog
        {
            Title = "Seleccionar archivo de entrada de Hernán",
            Filter = "Archivos Excel (*.xlsx)|*.xlsx|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            InputPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        if (IsInteractionBlocked())
        {
            return;
        }

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Seleccionar carpeta de salida",
            UseDescriptionForTitle = true,
            InitialDirectory = OutputPathTextBox.Text
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void BrowseConfig_Click(object sender, RoutedEventArgs e)
    {
        if (IsInteractionBlocked())
        {
            return;
        }

        using var dialog = new Forms.OpenFileDialog
        {
            Title = "Seleccionar archivo de configuración",
            Filter = "Archivos JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            ConfigPathTextBox.Text = dialog.FileName;
        }
    }

    private void EditConfig_Click(object sender, RoutedEventArgs e)
    {
        var configPath = ConfigPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(configPath))
        {
            ShowWarning("La ruta de configuración está vacía.", "Configuración");
            return;
        }

        if (!File.Exists(configPath))
        {
            ShowWarning("No se encontró el archivo de configuración.", "Configuración");
            return;
        }

        OpenPath(configPath, "archivo de configuración");
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var outputDirectory = _lastOutputDirectory ?? OutputPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            ShowWarning("No se encontró la carpeta de salida.", "Salida");
            return;
        }

        OpenPath(outputDirectory, "carpeta de salida");
    }

    // Comprueba que existan las rutas de entrada, salida y configuración necesarias para ejecutar Paso 1.
    private bool ValidateInputs(string inputPath, string outputDirectory, string configPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            RunStatusText.Foreground = StatusBrushes.Error;
            RunStatusText.Text = "Se requiere la ruta de entrada.";
            SummaryText.Text = "Seleccioná un archivo XLSX de entrada antes de ejecutar Paso 1.";
            LogError("Paso 1", "Falta archivo XLSX de entrada.");
            return false;
        }

        if (!File.Exists(inputPath))
        {
            RunStatusText.Foreground = StatusBrushes.Error;
            RunStatusText.Text = "No se encontró el archivo de entrada.";
            SummaryText.Text = "El archivo XLSX seleccionado no existe.";
            LogError("Paso 1", "No se encontró el XLSX de entrada seleccionado.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            RunStatusText.Foreground = StatusBrushes.Error;
            RunStatusText.Text = "Se requiere la carpeta de salida.";
            SummaryText.Text = "Seleccioná o ingresá una carpeta de salida antes de ejecutar Paso 1.";
            LogError("Paso 1", "Falta carpeta de salida.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            RunStatusText.Foreground = StatusBrushes.Error;
            RunStatusText.Text = "Se requiere la ruta de configuración.";
            SummaryText.Text = "Seleccioná PARA_HERNAN.json antes de ejecutar Paso 1.";
            LogError("Paso 1", "Falta ruta de configuración JSON.");
            return false;
        }

        if (!File.Exists(configPath))
        {
            RunStatusText.Foreground = StatusBrushes.Error;
            RunStatusText.Text = "No se encontró el archivo de configuración.";
            SummaryText.Text = "El archivo JSON seleccionado no existe.";
            LogError("Paso 1", "No se encontró el archivo de configuración JSON.");
            return false;
        }

        return true;
    }

    private bool TryEnsureOutputDirectory(string outputDirectory)
    {
        var preparation = Paso1OutputDirectoryPreparer.TryPrepare(outputDirectory);
        if (preparation.IsSuccess)
        {
            return true;
        }

        RunStatusText.Foreground = StatusBrushes.Error;
        RunStatusText.Text = preparation.StatusMessage ?? "La carpeta de salida no está disponible.";
        SummaryText.Text = preparation.SummaryMessage ?? "Elegí otra carpeta de salida y ejecutá Paso 1 nuevamente.";
        LogError("Paso 1", "La carpeta de salida no está disponible o no tiene permisos de escritura.");
        return false;
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
        var globalBusy = _busyCoordinator?.IsBusy ?? false;
        var localBusy = _isLocalBusy;
        var canEdit = !globalBusy;

        InputPathTextBox.IsEnabled = canEdit;
        OutputPathTextBox.IsEnabled = canEdit;
        ConfigPathTextBox.IsEnabled = canEdit;
        BrowseInputButton.IsEnabled = canEdit;
        BrowseOutputButton.IsEnabled = canEdit;
        BrowseConfigButton.IsEnabled = canEdit;
        EditConfigButton.IsEnabled = canEdit;
        RunPaso1Button.IsEnabled = canEdit;

        RunProgressBar.Visibility = localBusy ? Visibility.Visible : Visibility.Collapsed;

        if (localBusy || globalBusy)
        {
            OpenOutputFolderButton.IsEnabled = false;
        }
        else
        {
            OpenOutputFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastOutputDirectory);
        }
    }

    private static string BuildSuccessSummary(HernanPreparationResult result, TimeSpan elapsed)
    {
        var summary = result.Summary;
        return
            $"Paso 1 finalizó correctamente en {elapsed.TotalSeconds:F1}s.{Environment.NewLine}" +
            $"Filas totales: {summary.TotalRows}{Environment.NewLine}" +
            $"Filas válidas: {summary.ValidRows}{Environment.NewLine}" +
            $"Filas rechazadas: {summary.RejectedRows}{Environment.NewLine}" +
            $"CUIL faltantes: {summary.MissingCuilRows}{Environment.NewLine}" +
            $"CUIL con formato inválido: {summary.MalformedCuilRows}{Environment.NewLine}" +
            $"Filas con CUIL duplicado: {summary.DuplicateCuilRows}{Environment.NewLine}" +
            $"Avisos: {result.Notices.Count}{Environment.NewLine}" +
            $"CSV de Sergio: {result.SergioCsvPath}{Environment.NewLine}" +
            $"CSV rechazado: {result.RejectedCsvPath}";
    }

    private static string BuildFailureSummary(HernanPreparationResult result, TimeSpan elapsed)
    {
        var errorMessages = result.Errors.Count == 0
            ? "No se informaron detalles de validación."
            : string.Join(Environment.NewLine, result.Errors.Select(e => $"- {e.Code}: {e.Message}"));

        return
            $"Paso 1 se detuvo después de {elapsed.TotalSeconds:F1}s.{Environment.NewLine}" +
            $"Errores: {result.Errors.Count}, avisos: {result.Notices.Count}{Environment.NewLine}" +
            errorMessages;
    }

    private void OpenPath(string path, string targetName)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowWarning(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    $"No se pudo abrir {targetName}. Verificá los permisos de la aplicación y la configuración predeterminada.",
                    ex),
                "No se pudo abrir");
        }
    }

    private void ShowWarning(string message, string title)
    {
        var owner = Window.GetWindow(this);
        if (owner is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void LogInfo(string phase, string message) => ActivityConsole.Add(ActivitySeverity.Info, phase, message);
    private void LogSuccess(string phase, string message) => ActivityConsole.Add(ActivitySeverity.Success, phase, message);
    private void LogError(string phase, string message) => ActivityConsole.Add(ActivitySeverity.Error, phase, message);
}
