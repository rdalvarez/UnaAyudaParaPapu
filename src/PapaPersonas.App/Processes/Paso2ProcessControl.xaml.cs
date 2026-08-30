using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PapaPersonas.App.Ui;
using PapaPersonas.Core.Activity;
using PapaPersonas.Core.App;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso2;
using PapaPersonas.Infrastructure.Import.Paso2;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace PapaPersonas.App.Processes;

public partial class Paso2ProcessControl : System.Windows.Controls.UserControl
{
    private const string BusyOwner = "Paso2";
    private static readonly DateOnly LegacyBackfillDate = new(2026, 8, 10);

    private readonly SergioPaso2PreviewProcessor _previewProcessor = new();
    private readonly SergioPaso2ApplyProcessor _applyProcessor = new();
    private readonly Process2ImportSessionState _state = new();
    private readonly ActivityBuffer _activity = new(maxMessages: 500);

    private ProcessBusyCoordinator? _busyCoordinator;
    private Func<string?>? _databasePathProvider;
    private bool _isLocalBusy;
    private string? _lastOutputDirectory;
    private bool _isPrefillingImportDate;

    public Paso2ProcessControl()
    {
        InitializeComponent();
        ActivityConsole.AttachBuffer(_activity);
        InitializeDefaults();
        LogInfo("Proceso 2", "Consola iniciada para esta sesión de la app.");
    }

    public void ConfigureCoordinator(ProcessBusyCoordinator coordinator)
    {
        _busyCoordinator = coordinator;
        _busyCoordinator.BusyStateChanged += BusyCoordinator_BusyStateChanged;
        UpdateControlState();
    }

    public void SetDatabasePathProvider(Func<string?> provider)
    {
        _databasePathProvider = provider;
    }

    public void ClearDatabaseDependentState()
    {
        _state.Reset();
        Paso2StatusText.Foreground = StatusBrushes.Info;
        Paso2StatusText.Text = "La base cambió. Ejecutá un nuevo análisis.";
        Paso2SummaryText.Text = "El análisis anterior se limpió para evitar aplicar una vista vieja.";
        Paso2ImportRefText.Text = "Referencia de importación: no disponible.";
    }

    private void BusyCoordinator_BusyStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateControlState);
    }

    private void InitializeDefaults()
    {
        var documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var localAppDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var defaults = Paso2RuntimePathResolver.Resolve(documentsDirectory, localAppDataDirectory);
        Paso2RejectedFolderTextBox.Text = defaults.DefaultRejectedOutputDirectory;
        Paso2SummaryText.Text = "Todavía no se realizó un análisis.";
        Paso2ImportRefText.Text = "Referencia de importación: no analizada.";
    }

    // Analiza el archivo de Sergio y deja una importación validada en staging sin modificar personas.
    private async void AnalyzePaso2_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginWork())
        {
            return;
        }

        ActivityConsole.ClearActivity();
        LogInfo("Proceso 2", "Inicio del análisis. Se limpia la consola de este proceso para la nueva corrida.");

        if (!TryValidateDbPath(out var databasePath))
        {
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = "La base no está lista.";
            Paso2SummaryText.Text = "La ruta de DuckDB no está disponible. Reiniciá la aplicación y volvé a intentar.";
            LogError("Proceso 2", "Análisis bloqueado: DuckDB no disponible.");
            EndWork();
            return;
        }

        var inputPath = Paso2InputPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = "No se encontró el archivo de entrada.";
            Paso2SummaryText.Text = "Seleccioná un XLSX válido de Sergio antes de analizarlo.";
            LogError("Proceso 2", "Análisis bloqueado: no se encontró XLSX de Sergio.");
            EndWork();
            return;
        }

        if (!TryResolveImportDateForAnalyze(databasePath, out var importDate, out var dateValidationMessage, out var shouldWarnOldDate, out var oldDateWarningMessage))
        {
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = "La fecha de importación no es válida.";
            Paso2SummaryText.Text = dateValidationMessage;
            LogError("Proceso 2", "Análisis bloqueado: fecha de importación inválida, incompleta o futura.");
            EndWork();
            return;
        }

        if (shouldWarnOldDate)
        {
            LogInfo("Proceso 2", "Advertencia: se detectó una fecha de importación antigua. El análisis continúa según la política.");
        }

        _state.BeginWork();
        Paso2StatusText.Foreground = StatusBrushes.Info;
        Paso2StatusText.Text = "Analizando archivo de Sergio...";
        Paso2SummaryText.Text = shouldWarnOldDate
            ? oldDateWarningMessage ?? "Ejecutando vista previa con una fecha de importación anterior."
            : "Ejecutando vista previa. En este paso no se modifican personas.";
        Paso2ImportRefText.Text = "Referencia de importación: análisis pendiente...";
        LogInfo("Proceso 2", "Vista previa iniciada. Esta fase no modifica personas.");

        try
        {
            var request = new SergioStagePreviewRequest(inputPath, databasePath, importDate);
            var result = await Task.Run(() => _previewProcessor.Analyze(request));

            if (result.Status == SergioStagePreviewStatus.ConfirmationRequired)
            {
                var dialog = new SergioUnknownHeadersDialog(result.UnknownSourceHeaders)
                {
                    Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
                };

                if (dialog.ShowDialog() != true || !dialog.ShouldContinue)
                {
                    SergioPaso2PreviewProcessor.CleanupSnapshot(result.SourceSnapshotPath);
                    _state.ClearOwnership();
                    Paso2StatusText.Foreground = StatusBrushes.Info;
                    Paso2StatusText.Text = "Análisis cancelado.";
                    Paso2SummaryText.Text = "No se modificó la base ni se creó estado de importación.";
                    Paso2ImportRefText.Text = "Referencia de importación: no disponible.";
                    LogInfo("Proceso 2", "Análisis cancelado: el usuario no autorizó ignorar columnas desconocidas.");
                    return;
                }

                request = request with
                {
                    AllowUnknownHeaders = true,
                    ExpectedSourceShapeFingerprint = result.SourceShapeFingerprint,
                    ExpectedSourceIdentity = result.SourceIdentity,
                    SourceSnapshotPath = result.SourceSnapshotPath
                };
                LogInfo("Proceso 2", "Columnas desconocidas confirmadas para ignorarlas; se procesarán sólo campos reconocidos.");
                result = await Task.Run(() => _previewProcessor.Analyze(request));
            }

            _state.SetPreviewResult(result, inputPath, importDate);

            if (result.IsSuccess && result.ImportId.HasValue)
            {
                Paso2StatusText.Foreground = StatusBrushes.Success;
                Paso2StatusText.Text = "Vista previa lista para confirmar.";
                Paso2SummaryText.Text = BuildPreviewSuccessSummary(result);
                Paso2ImportRefText.Text = $"Referencia de importación: {result.ImportId:D}";
                LogSuccess("Proceso 2", $"Análisis completado. import_id={result.ImportId:D}, válidas={result.Summary.ValidRows}, rechazadas={result.Summary.RejectedRows}.");
                return;
            }

            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = result.Status == SergioStagePreviewStatus.ValidationFailed
                ? "Vista previa bloqueada por validación."
                : "La vista previa falló.";
            Paso2SummaryText.Text = BuildPreviewFailureSummary(result);
            Paso2ImportRefText.Text = "Referencia de importación: no disponible.";
            LogError("Proceso 2", $"El análisis falló. Estado={StatusDisplayNames.For(result.Status)}, errores={result.Errors.Count}, avisos={result.Notices.Count}.");
        }
        catch (Exception ex)
        {
            _state.Reset();
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = UserFacingExceptionMessage.WithTechnicalDetail("La vista previa falló.", ex);
            Paso2SummaryText.Text = UserFacingExceptionMessage.WithTechnicalDetail(
                "Ocurrió un error al analizar. Verificá el archivo y la base, y volvé a intentar.",
                ex);
            Paso2ImportRefText.Text = "Referencia de importación: no disponible.";
            LogError(
                "Proceso 2",
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "Error inesperado en el análisis. No se exponen valores de filas.",
                    ex));
        }
        finally
        {
            _state.EndWork();
            EndWork();
        }
    }

    // Confirma y aplica sólo la importación analizada que aún coincide con su archivo y fecha de origen.
    private async void ApplyPaso2_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginWork())
        {
            return;
        }

        if (!_state.PendingImportId.HasValue)
        {
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = "No hay una vista previa para aplicar.";
            Paso2SummaryText.Text = "Ejecutá Analizar primero. Sólo se puede aplicar la importación analizada actualmente.";
            LogError("Proceso 2", "Aplicación bloqueada: no existe una vista previa pendiente.");
            EndWork();
            return;
        }

        if (!TryValidateDbPath(out var databasePath))
        {
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = "La base no está lista.";
            Paso2SummaryText.Text = "La ruta de DuckDB no está disponible. Reiniciá la aplicación y volvé a intentar.";
            LogError("Proceso 2", "Aplicación bloqueada: DuckDB no disponible.");
            EndWork();
            return;
        }

        var importId = _state.PendingImportId.Value;

        if (!TryGetCurrentImportDate(out var currentImportDate))
        {
            _state.ClearOwnership();
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = "La fecha de importación no es válida.";
            Paso2SummaryText.Text = "Analizá nuevamente con una fecha válida antes de aplicar.";
            Paso2ImportRefText.Text = "Referencia de importación: invalidada (cambió la fecha).";
            LogError("Proceso 2", "Aplicación bloqueada: fecha inválida después del análisis.");
            EndWork();
            return;
        }

        if (!_state.IsOwnedImportForPathAndDate(importId, Paso2InputPathTextBox.Text, currentImportDate))
        {
            _state.ClearOwnership();
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = "Cambió la entrada o la fecha desde el análisis.";
            Paso2SummaryText.Text = "Analizá nuevamente antes de aplicar. Se invalidó la autorización de aplicación.";
            Paso2ImportRefText.Text = "Referencia de importación: invalidada (cambió el origen o la fecha).";
            LogError("Proceso 2", "Aplicación bloqueada: cambió la ruta o la fecha desde el análisis.");
            EndWork();
            return;
        }

        var warning = MessageBox.Show(
            Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
            "Esta acción actualizará la base definitiva de Sergio (personas).\n\n" +
            "Se recomienda crear un resguardo manual antes de aplicar. ¿Querés continuar sin crearlo ahora?",
            "Confirmar aplicación",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (warning != MessageBoxResult.OK)
        {
            Paso2StatusText.Foreground = StatusBrushes.Info;
            Paso2StatusText.Text = "Aplicación cancelada por el usuario.";
            LogInfo("Proceso 2", "Aplicación cancelada por el usuario después de la advertencia sobre el resguardo.");
            EndWork();
            return;
        }

        if (!_state.IsOwnedImportForPathAndDate(importId, Paso2InputPathTextBox.Text, currentImportDate))
        {
            _state.ClearOwnership();
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = "La referencia de importación está desactualizada.";
            Paso2SummaryText.Text = "Analizá nuevamente antes de aplicar. La ruta actual ya no coincide con la analizada.";
            LogError("Proceso 2", "Aplicación bloqueada: import_id desactualizado después de la confirmación.");
            EndWork();
            return;
        }

        var rejectedFolder = Paso2RejectedFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rejectedFolder))
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            rejectedFolder = Paso2RuntimePathResolver.Resolve(docs, local).DefaultRejectedOutputDirectory;
            Paso2RejectedFolderTextBox.Text = rejectedFolder;
        }

        var preflight = Paso2OutputDirectoryPreflight.ValidateWritable(rejectedFolder);
        if (!preflight.IsSuccess)
        {
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = "La carpeta de salida no está disponible.";
            Paso2SummaryText.Text = preflight.ErrorMessage ?? "La carpeta de salida seleccionada no permite escritura.";
            LogError("Proceso 2", "Aplicación bloqueada: la carpeta de rechazados no permite escritura.");
            EndWork();
            return;
        }

        _state.BeginWork();
        Paso2StatusText.Foreground = StatusBrushes.Info;
        Paso2StatusText.Text = "Aplicando importación...";
        Paso2SummaryText.Text = "Se aplican sólo las filas válidas agregadas en el área de preparación para la referencia actual.";
        LogInfo("Proceso 2", $"Aplicación iniciada para import_id={importId:D}.");

        try
        {
            if (!_state.IsOwnedImportForPathAndDate(importId, Paso2InputPathTextBox.Text, currentImportDate))
            {
                _state.ClearOwnership();
                Paso2StatusText.Foreground = StatusBrushes.Error;
                Paso2StatusText.Text = "Cambió la entrada o la fecha desde el análisis.";
                Paso2SummaryText.Text = "Analizá nuevamente antes de aplicar. Se invalidó la autorización de aplicación.";
                Paso2ImportRefText.Text = "Referencia de importación: invalidada (cambió el origen o la fecha).";
                LogError("Proceso 2", "Aplicación cancelada: cambió la ruta o la fecha antes de ejecutar la base.");
                return;
            }

            var request = new SergioApplyRequest(
                DatabasePath: databasePath,
                ImportId: importId,
                RejectedCsvOutputDirectory: rejectedFolder);

            var result = await Task.Run(() => _applyProcessor.Apply(request));

            if (result.IsSuccess)
            {
                _state.MarkApplied(importId);
                _lastOutputDirectory = result.RejectedCsvPath is null
                    ? rejectedFolder
                    : Path.GetDirectoryName(result.RejectedCsvPath) ?? rejectedFolder;

                Paso2StatusText.Foreground = StatusBrushes.Success;
                Paso2StatusText.Text = "Aplicación completada.";
                Paso2SummaryText.Text = BuildApplySuccessSummary(result);
                Paso2ImportRefText.Text = $"Referencia de importación: {importId:D} (completada)";
                LogSuccess("Proceso 2", $"Aplicación completada. aplicadas={result.Summary.AppliedRows}, insertadas={result.Summary.InsertedRows}, actualizadas={result.Summary.UpdatedRows}.");
                return;
            }

            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = result.Status == SergioApplyStatus.ValidationFailed
                ? "Aplicación bloqueada por validación."
                : "La aplicación falló.";
            _state.MarkFailedApply(importId);
            Paso2ImportRefText.Text = $"Referencia de importación: {importId:D} (aplicación fallida; se requiere analizar nuevamente)";
            Paso2SummaryText.Text = BuildApplyFailureSummary(result);
            LogError("Proceso 2", $"La aplicación falló. Estado={StatusDisplayNames.For(result.Status)}, errores={result.Errors.Count}, avisos={result.Notices.Count}.");
        }
        catch (Exception ex)
        {
            _state.MarkFailedApply(importId);
            Paso2StatusText.Foreground = StatusBrushes.Error;
            Paso2StatusText.Text = UserFacingExceptionMessage.WithTechnicalDetail("La aplicación falló.", ex);
            Paso2ImportRefText.Text = $"Referencia de importación: {importId:D} (aplicación fallida; se requiere analizar nuevamente)";
            Paso2SummaryText.Text = UserFacingExceptionMessage.WithTechnicalDetail(
                "Ocurrió un error al aplicar. No se muestran valores de filas. Verificá el estado y volvé a intentar.",
                ex);
            LogError(
                "Proceso 2",
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "Error inesperado en la aplicación. Se requiere un nuevo análisis.",
                    ex));
        }
        finally
        {
            _state.EndWork();
            EndWork();
        }
    }

    private void BrowsePaso2Input_Click(object sender, RoutedEventArgs e)
    {
        if (IsInteractionBlocked())
        {
            return;
        }

        using var dialog = new Forms.OpenFileDialog
        {
            Title = "Seleccionar archivo de devolución de Sergio",
            Filter = "Archivos Excel (*.xlsx)|*.xlsx|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            Paso2InputPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowsePaso2RejectedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (IsInteractionBlocked())
        {
            return;
        }

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Seleccionar carpeta de salida de rechazados",
            UseDescriptionForTitle = true,
            InitialDirectory = Paso2RejectedFolderTextBox.Text
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            Paso2RejectedFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Paso2InputPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        TryPrefillDateFromFileName(Paso2InputPathTextBox.Text);

        if (_state.InvalidateIfSourcePathOrImportDateChanged(Paso2InputPathTextBox.Text, GetCurrentImportDateOrNull()))
        {
            Paso2StatusText.Foreground = StatusBrushes.Info;
            Paso2StatusText.Text = "Cambió la entrada o la fecha. Se requiere volver a analizar.";
            Paso2ImportRefText.Text = "Referencia de importación: invalidada (cambió el origen o la fecha).";
            LogInfo("Proceso 2", "Cambió la ruta o la fecha. Se invalida la autorización de aplicación por seguridad.");
        }

        UpdateControlState();
    }

    private void Paso2ImportDatePart_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isPrefillingImportDate)
        {
            return;
        }

        if (_state.InvalidateIfSourcePathOrImportDateChanged(Paso2InputPathTextBox.Text, GetCurrentImportDateOrNull()))
        {
            Paso2StatusText.Foreground = StatusBrushes.Info;
            Paso2StatusText.Text = "Cambió la entrada o la fecha. Se requiere volver a analizar.";
            Paso2ImportRefText.Text = "Referencia de importación: invalidada (cambió el origen o la fecha).";
            LogInfo("Proceso 2", "Cambió la fecha. Se invalida la autorización de aplicación por seguridad.");
        }

        UpdateControlState();
    }

    private void OpenPaso2OutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var outputDirectory = _lastOutputDirectory ?? Paso2RejectedFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            ShowWarning("No se encontró la carpeta de salida de Paso 2.", "Salida");
            return;
        }

        OpenPath(outputDirectory, "carpeta de salida de Paso 2");
    }

    private bool TryValidateDbPath(out string databasePath)
    {
        databasePath = _databasePathProvider?.Invoke() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(databasePath) && File.Exists(databasePath);
    }

    private static DateOnly GetTodayDate()
    {
        return DateOnly.FromDateTime(DateTime.Today);
    }

    private bool TryGetCurrentImportDate(out DateOnly importDate)
    {
        return Process2ImportDateSemantics.TryParseImportDateFromParts(
            Paso2ImportDayTextBox.Text,
            Paso2ImportMonthTextBox.Text,
            Paso2ImportYearTextBox.Text,
            out importDate);
    }

    private DateOnly? GetCurrentImportDateOrNull()
    {
        return TryGetCurrentImportDate(out var importDate)
            ? importDate
            : null;
    }

    // Resuelve la fecha de importación y advierte si es anterior a la última fecha aplicada.
    private bool TryResolveImportDateForAnalyze(
        string databasePath,
        out DateOnly importDate,
        out string message,
        out bool shouldWarnOldDate,
        out string? oldDateWarningMessage)
    {
        shouldWarnOldDate = false;
        oldDateWarningMessage = null;

        if (!TryGetCurrentImportDate(out importDate))
        {
            message = "Ingresá una fecha de importación completa y válida (dd/MM/yyyy) antes de analizar.";
            return false;
        }

        if (Process2ImportDateSemantics.IsFutureDate(importDate, GetTodayDate()))
        {
            message = "La fecha de importación no puede ser futura.";
            return false;
        }

        var latestAppliedDate = TryGetLatestAppliedImportDate(databasePath);
        if (latestAppliedDate.HasValue && Process2ImportDateSemantics.IsOlderThanLatestApplied(importDate, latestAppliedDate.Value))
        {
            shouldWarnOldDate = true;
            oldDateWarningMessage = Process2ImportDateSemantics.OldDateWarningMessage;
        }

        message = string.Empty;
        return true;
    }

    private static DateOnly? TryGetLatestAppliedImportDate(string databasePath)
    {
        try
        {
            using var connection = new DuckDB.NET.Data.DuckDBConnection(new DuckDB.NET.Data.DuckDBConnectionStringBuilder { DataSource = databasePath }.ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(fecha_importacion) FROM personas WHERE fecha_importacion IS NOT NULL;";

            var value = command.ExecuteScalar();
            if (value is null || value == DBNull.Value)
            {
                return null;
            }

            var timestamp = (DateTime)Convert.ChangeType(value, typeof(DateTime), System.Globalization.CultureInfo.InvariantCulture);
            return DateOnly.FromDateTime(timestamp);
        }
        catch
        {
            return null;
        }
    }

    private void TryPrefillDateFromFileName(string inputPath)
    {
        if (_isPrefillingImportDate)
        {
            return;
        }

        if (!Process2ImportDateSemantics.TryExtractSingleBoundedFilenameDateToken(inputPath, out var tokenDate))
        {
            return;
        }

        _isPrefillingImportDate = true;
        try
        {
            Paso2ImportDayTextBox.Text = tokenDate.Day.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            Paso2ImportMonthTextBox.Text = tokenDate.Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            Paso2ImportYearTextBox.Text = tokenDate.Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            _isPrefillingImportDate = false;
        }
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

        Paso2InputPathTextBox.IsEnabled = canEdit;
        Paso2RejectedFolderTextBox.IsEnabled = canEdit;
        Paso2BrowseInputButton.IsEnabled = canEdit;
        Paso2BrowseRejectedFolderButton.IsEnabled = canEdit;
        AnalyzePaso2Button.IsEnabled = canEdit;
        ApplyPaso2Button.IsEnabled = canEdit && _state.CanApply;

        Paso2ProgressBar.Visibility = localBusy ? Visibility.Visible : Visibility.Collapsed;

        if (globalBusy || localBusy)
        {
            OpenPaso2OutputFolderButton.IsEnabled = false;
        }
        else
        {
            OpenPaso2OutputFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastOutputDirectory);
        }
    }

    private static string BuildPreviewSuccessSummary(SergioStagePreviewResult result)
    {
        var s = result.Summary;
        return
            $"Vista previa completada en {result.Elapsed.TotalSeconds:F1}s.{Environment.NewLine}" +
            $"Estado: {StatusDisplayNames.For(result.Status)}{Environment.NewLine}" +
            $"Filas totales: {s.TotalRows}{Environment.NewLine}" +
            $"Filas válidas: {s.ValidRows}{Environment.NewLine}" +
            $"Filas rechazadas: {s.RejectedRows}{Environment.NewLine}" +
            $"CUIL faltantes: {s.MissingCuilRows}{Environment.NewLine}" +
            $"CUIL con formato inválido: {s.MalformedCuilRows}{Environment.NewLine}" +
            $"Filas con CUIL duplicado: {s.DuplicateCuilRows}{Environment.NewLine}" +
            $"Filas con tipos inválidos: {s.InvalidTypedValueRows}{Environment.NewLine}" +
            $"Filas a insertar: {s.RowsToInsert}{Environment.NewLine}" +
            $"Filas a actualizar: {s.RowsToUpdate}{Environment.NewLine}" +
            $"Problemas: {result.Errors.Count}, avisos: {result.Notices.Count}";
    }

    private static string BuildPreviewFailureSummary(SergioStagePreviewResult result)
    {
        var details = result.Errors.Count == 0
            ? (result.FailureMessage ?? "No se informaron detalles de validación.")
            : string.Join(Environment.NewLine, result.Errors.Select(e => $"- {e.Code}: {e.Message}"));

        return
            $"La vista previa se detuvo después de {result.Elapsed.TotalSeconds:F1}s.{Environment.NewLine}" +
            $"Estado: {StatusDisplayNames.For(result.Status)}{Environment.NewLine}" +
            $"Problemas: {result.Errors.Count}, avisos: {result.Notices.Count}{Environment.NewLine}" +
            details;
    }

    private static string BuildApplySuccessSummary(SergioApplyResult result)
    {
        var s = result.Summary;
        var rejectedPath = string.IsNullOrWhiteSpace(result.RejectedCsvPath)
            ? "(no se generó)"
            : result.RejectedCsvPath;

        return
            $"Aplicación completada en {result.Elapsed.TotalSeconds:F1}s.{Environment.NewLine}" +
            $"Estado: {StatusDisplayNames.For(result.Status)}{Environment.NewLine}" +
            $"Filas aplicadas: {s.AppliedRows}{Environment.NewLine}" +
            $"Filas insertadas: {s.InsertedRows}{Environment.NewLine}" +
            $"Filas actualizadas: {s.UpdatedRows}{Environment.NewLine}" +
            $"Filas rechazadas exportadas: {s.RejectedRowsExported}{Environment.NewLine}" +
            $"Ruta del CSV rechazado: {rejectedPath}{Environment.NewLine}" +
            $"Problemas: {result.Errors.Count}, avisos: {result.Notices.Count}";
    }

    private static string BuildApplyFailureSummary(SergioApplyResult result)
    {
        var details = result.Errors.Count == 0
            ? (result.FailureMessage ?? "No se informaron detalles de validación.")
            : string.Join(Environment.NewLine, result.Errors.Select(e => $"- {e.Code}: {e.Message}"));

        return
            $"La aplicación se detuvo después de {result.Elapsed.TotalSeconds:F1}s.{Environment.NewLine}" +
            $"Estado: {StatusDisplayNames.For(result.Status)}{Environment.NewLine}" +
            $"Problemas: {result.Errors.Count}, avisos: {result.Notices.Count}{Environment.NewLine}" +
            details;
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
