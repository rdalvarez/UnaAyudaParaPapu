using System.Windows;
using PapaPersonas.V2.Shell;

namespace PapaPersonas.V2.App;

public partial class SergioRejectionDecisionDialog : Window
{
    public SergioRejectionDecisionDialog(SergioStagingPresentation presentation)
    {
        InitializeComponent();
        MessageTextBlock.Text = $"Se excluirán las {presentation.RejectedRows} filas rechazadas. Se podrán cargar únicamente las {presentation.ValidRows} filas válidas. ¿Desea continuar?";
    }

    public bool Accepted { get; private set; }
    private void ContinueButton_OnClick(object sender, RoutedEventArgs e) { Accepted = true; DialogResult = true; }
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) { Accepted = false; DialogResult = false; }
}
