using System.IO;
using System.Windows;
using System.Windows.Controls;
using PapaPersonas.V2.Shell;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace PapaPersonas.V2.App;

public partial class MainWindow : Window
{
    private readonly IHeaderReader _headerReader = new ExcelDataReaderHeaderReader();
    private readonly ActivityLog _activityLog = new();
    private string? _rejectedFolderPath;

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
            SetBusy(true);
            var path = dialog.FileName;
            var rejectedFolder = RejectedFolderPolicy.GetDefault(path);
            var columns = await Task.Run(() =>
            {
                Directory.CreateDirectory(rejectedFolder);
                return HeaderPresentation.Normalize(_headerReader.ReadHeaders(path));
            });

            InputFileTextBox.Text = path;
            RejectedFolderTextBox.Text = rejectedFolder;
            _rejectedFolderPath = rejectedFolder;
            SetColumns(columns);
            AddActivity($"Archivo seleccionado: {Path.GetFileName(path)}");
            AddActivity($"Carpeta de rechazados: {rejectedFolder}");
            AddActivity($"Columnas detectadas: {columns.Count}");
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

    private void SetColumns(IReadOnlyList<DetectedColumn> columns)
    {
        DetectedColumnsListBox.ItemsSource = columns;
        DuplicateColumnComboBox.ItemsSource = columns;
        EmptyColumnComboBox.ItemsSource = columns;
        DuplicateColumnComboBox.IsEnabled = columns.Count > 0;
        EmptyColumnComboBox.IsEnabled = columns.Count > 0;
    }

    private void SetBusy(bool isBusy)
    {
        HeaderReadingProgressPanel.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BrowseFileButton.IsEnabled = !isBusy;
        BrowseFolderButton.IsEnabled = !isBusy;
        RejectedFolderLabel.IsEnabled = !isBusy;
        RejectedFolderTextBox.IsEnabled = !isBusy;
    }

    private void AddActivity(string message)
    {
        _activityLog.Add(message);
        ActivityTextBox.Text = string.Join(Environment.NewLine, _activityLog.Entries);
    }

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
