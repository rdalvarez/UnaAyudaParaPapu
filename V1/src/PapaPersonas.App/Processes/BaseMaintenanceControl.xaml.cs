using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PapaPersonas.App;
using PapaPersonas.App.Ui;
using PapaPersonas.Core.Activity;
using PapaPersonas.Core.App;
using PapaPersonas.Core.Database;
using PapaPersonas.Core.Import;
using PapaPersonas.Infrastructure.Database;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace PapaPersonas.App.Processes;

public partial class BaseMaintenanceControl : System.Windows.Controls.UserControl
{
    private const string BusyOwner = "Maintenance";

    private readonly IDatabaseMaintenanceService _maintenanceService;
    private readonly ActivityBuffer _activity = new(maxMessages: 500);

    private ProcessBusyCoordinator? _busyCoordinator;
    private Func<string?>? _databasePathProvider;
    private bool _isLocalBusy;

    public BaseMaintenanceControl()
        : this(new DuckDbMaintenanceService())
    {
    }

    internal BaseMaintenanceControl(IDatabaseMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));

        InitializeComponent();
        ActivityConsole.AttachBuffer(_activity);
        RefreshBoundPaths();
        UpdateControlState();
        LogInfo("Mantenimiento", "Consola iniciada para esta sesión de la app.");
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
        RefreshBoundPaths();
        UpdateControlState();
    }

    private void BusyCoordinatorOnBusyStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateControlState);
    }

    // TODO(database-maintenance-hardening): These UI handlers must let fatal exceptions escape the boundary; only non-fatal failures should become friendly status text.
    // Crea una copia consistente de la base activa usando un archivo temporal antes de publicarla.
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginWork())
        {
            return;
        }

        try
        {
            if (!TryResolveDatabasePath(out var databasePath))
            {
                return;
            }

            if (!ConfirmMaintenanceOperation(DatabaseMaintenanceConfirmation.BackupPrompt, "Confirmar creación de copia"))
            {
                return;
            }

            var latestImportDate = await Task.Run(() => _maintenanceService.GetLatestImportDate(databasePath));
            var suggestedFileName = DatabaseMaintenanceNaming.BuildSuggestedBackupFileName(latestImportDate);
            var initialDirectory = GetDefaultBackupDirectory();

            using var dialog = new Forms.SaveFileDialog
            {
                Title = "Guardar copia de la base",
                Filter = "DuckDB (*.duckdb)|*.duckdb|Todos los archivos (*.*)|*.*",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = initialDirectory,
                FileName = suggestedFileName
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
            {
                SetStatus("La copia se canceló antes de guardar.", isError: false);
                return;
            }

            SetStatus("Creando copia de seguridad...", isError: false);
            var result = await Task.Run(() => _maintenanceService.BackupCurrentDatabase(databasePath, dialog.FileName));
            if (!result.IsSuccess)
            {
                SetStatus(result.Message ?? "No se pudo crear la copia.", isError: true);
                return;
            }

            var dateText = result.LatestImportDate.HasValue
                ? $" Última importación: {result.LatestImportDate:yyyy-MM-dd}."
                : "";
            SetStatus($"Copia creada en {result.OutputPath}.{dateText}", isError: false);
            LogSuccess("Mantenimiento", $"Copia creada en {result.OutputPath}.");
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            SetStatus(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo crear la copia. Verificá que la base no esté abierta en otra herramienta y volvé a intentar.",
                    ex),
                isError: true);
            LogError(
                "Mantenimiento",
                UserFacingExceptionMessage.WithTechnicalDetail("Error al crear copia.", ex));
        }
        finally
        {
            EndWork();
        }
    }

    // Preserva la base activa, valida la copia en staging y reemplaza el archivo sólo al final.
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginWork())
        {
            return;
        }

        try
        {
            if (!TryResolveDatabasePath(out var databasePath))
            {
                return;
            }

            if (!ConfirmMaintenanceOperation(DatabaseMaintenanceConfirmation.RestorePrompt, "Confirmar restauración total"))
            {
                return;
            }

            using var dialog = new Forms.OpenFileDialog
            {
                Title = "Seleccionar copia para restaurar",
                Filter = "DuckDB (*.duckdb)|*.duckdb|Todos los archivos (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = GetDefaultBackupDirectory()
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
            {
                SetStatus("La restauración se canceló antes de seleccionar una copia.", isError: false);
                return;
            }

            SetStatus("Restaurando base seleccionada...", isError: false);
            var safetyRoot = GetSafetyBackupRoot();
            var result = await Task.Run(() => _maintenanceService.RestoreDatabase(databasePath, dialog.FileName, safetyRoot));
            if (!result.IsSuccess)
            {
                SetStatus(result.Message ?? "No se pudo restaurar la base.", isError: true);
                return;
            }

            // Libera Maintenance antes de que Proceso 4 solicite la decisión pendiente.
            EndWork();
            await RefreshMainWindowAfterMutationAsync(
                databasePath,
                DatabaseMaintenanceRefreshPolicy.RequestsPendingDialog(DatabaseMaintenanceOperation.Restore));
            SetStatus($"Base restaurada. Resguardo automático guardado en {result.SafetyBackupPath}.", isError: false);
            LogSuccess("Mantenimiento", $"Base restaurada desde {dialog.FileName}. Resguardo en {result.SafetyBackupPath}.");
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            SetStatus(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo restaurar la base. Verificá la copia seleccionada y volvé a intentar.",
                    ex),
                isError: true);
            LogError(
                "Mantenimiento",
                UserFacingExceptionMessage.WithTechnicalDetail("Error al restaurar.", ex));
        }
        finally
        {
            EndWork();
        }
    }

    // Preserva la base activa y la reemplaza por una base nueva cuyo esquema ya fue validado.
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginWork())
        {
            return;
        }

        try
        {
            if (!TryResolveDatabasePath(out var databasePath))
            {
                return;
            }

            SetStatus("Reiniciando base y recreando el esquema...", isError: false);
            var safetyRoot = GetSafetyBackupRoot();
            var result = await Task.Run(() => _maintenanceService.ResetDatabase(databasePath, safetyRoot));
            if (!result.IsSuccess)
            {
                SetStatus(result.Message ?? "No se pudo reiniciar la base.", isError: true);
                return;
            }

            await RefreshMainWindowAfterMutationAsync(
                databasePath,
                DatabaseMaintenanceRefreshPolicy.RequestsPendingDialog(DatabaseMaintenanceOperation.Reset));
            SetStatus($"Base reiniciada. Resguardo automático guardado en {result.SafetyBackupPath}.", isError: false);
            LogSuccess("Mantenimiento", $"Base reiniciada. Resguardo en {result.SafetyBackupPath}.");
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            SetStatus(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo reiniciar la base. Verificá que la base no esté abierta en otra herramienta y volvé a intentar.",
                    ex),
                isError: true);
            LogError(
                "Mantenimiento",
                UserFacingExceptionMessage.WithTechnicalDetail("Error al reiniciar.", ex));
        }
        finally
        {
            EndWork();
        }
    }

    private async Task RefreshMainWindowAfterMutationAsync(string databasePath, bool showPendingDialog)
    {
        var window = Window.GetWindow(this) as MainWindow ?? System.Windows.Application.Current.MainWindow as MainWindow;
        if (window is null)
        {
            return;
        }

        await window.RefreshDatabaseAfterMaintenanceAsync(databasePath, showPendingDialog);
    }

    private bool ConfirmMaintenanceOperation(string message, string title)
    {
        var owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow;
        return MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    private bool TryBeginWork()
    {
        if (_busyCoordinator is null)
        {
            return false;
        }

        if (!_busyCoordinator.TryBegin(BusyOwner))
        {
            ShowWarning("Hay otra operación en curso. Esperá a que termine para usar Mantenimiento de base.", "Mantenimiento de base");
            return false;
        }

        _isLocalBusy = true;
        UpdateControlState();
        return true;
    }

    private void EndWork()
    {
        if (!_isLocalBusy)
        {
            return;
        }

        _isLocalBusy = false;
        _busyCoordinator?.End(BusyOwner);
        UpdateControlState();
    }

    private bool TryResolveDatabasePath(out string databasePath)
    {
        databasePath = _databasePathProvider?.Invoke() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            SetStatus("La base todavía no está lista.", isError: true);
            ShowWarning("La base todavía no está lista.", "Mantenimiento de base");
            return false;
        }

        if (!File.Exists(databasePath))
        {
            SetStatus("No se encontró la base activa.", isError: true);
            ShowWarning("No se encontró la base activa.", "Mantenimiento de base");
            return false;
        }

        return true;
    }

    private void RefreshBoundPaths()
    {
        var databasePath = _databasePathProvider?.Invoke();
        DatabasePathText.Text = string.IsNullOrWhiteSpace(databasePath) ? "No disponible." : databasePath;
        SafetyBackupRootText.Text = GetSafetyBackupRoot();
    }

    private static string GetDefaultBackupDirectory()
    {
        var root = GetSafetyBackupRoot();
        Directory.CreateDirectory(root);
        return root;
    }

    private static string GetSafetyBackupRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return DatabaseMaintenanceNaming.BuildSafetyBackupRootDirectory(localAppData);
    }

    private void UpdateControlState()
    {
        var globalBusy = _busyCoordinator?.IsBusy ?? false;
        var canEdit = !globalBusy;

        BackupButton.IsEnabled = canEdit;
        RestoreButton.IsEnabled = canEdit;
        ResetButton.IsEnabled = canEdit;
        WorkingProgressBar.Visibility = _isLocalBusy ? Visibility.Visible : Visibility.Collapsed;

        if (!canEdit)
        {
            StatusText.Text = "Hay otra operación en curso.";
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Foreground = isError ? StatusBrushes.Error : StatusBrushes.Info;
        StatusText.Text = message;
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
