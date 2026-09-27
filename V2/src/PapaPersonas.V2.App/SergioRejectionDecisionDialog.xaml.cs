using System.Windows;
using PapaPersonas.V2.Shell;

namespace PapaPersonas.V2.App;

public partial class SergioRejectionDecisionDialog : Window
{
    public SergioRejectionDecisionDialog(SergioStagingPresentation presentation)
    {
        InitializeComponent();
        MessageTextBlock.Text = SergioInteractionPolicy.RejectionDecisionMessage(presentation);
    }

    private void ContinueButton_OnClick(object sender, RoutedEventArgs e) { DialogResult = true; }
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) { DialogResult = false; }
}
