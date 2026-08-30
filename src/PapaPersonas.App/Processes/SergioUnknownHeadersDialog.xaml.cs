using System.Windows;
using PapaPersonas.Core.Import.Paso2;

namespace PapaPersonas.App.Processes;

public partial class SergioUnknownHeadersDialog : Window
{
    public SergioUnknownHeadersDialog(IReadOnlyList<string> unknownHeaders)
    {
        ArgumentNullException.ThrowIfNull(unknownHeaders);

        InitializeComponent();
        UnknownHeadersList.ItemsSource = unknownHeaders;
    }

    public bool ShouldContinue { get; private set; }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        ShouldContinue = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ShouldContinue = false;
        DialogResult = false;
    }
}
