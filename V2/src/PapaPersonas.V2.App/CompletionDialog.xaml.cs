using System.Diagnostics;
using System.IO;
using System.Windows;
using PapaPersonas.V2.Shell;

namespace PapaPersonas.V2.App;

public partial class CompletionDialog : Window
{
    private readonly string sourceDirectory;

    public CompletionDialog(CleaningExecutionResult result, string inputWorkbookPath)
    {
        InitializeComponent();
        DataContext = result;
        sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(inputWorkbookPath))!;
    }

    private void OpenFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = sourceDirectory,
                UseShellExecute = true
            });
            DialogResult = true;
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"No se pudo abrir la carpeta de origen.\n\n{exception.Message}", "No se pudo abrir la carpeta", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AcceptButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
