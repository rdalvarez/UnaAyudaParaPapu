using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PapaPersonas.V2.Shell;

namespace PapaPersonas.V2.App;

public partial class SergioLoadPreviewDialog : Window
{
    public SergioLoadPreviewDialog(SergioPreview preview)
    {
        InitializeComponent();
        SummaryTextBlock.Text = SergioLoadPreviewText.Summary(preview);
        WarningTextBlock.Text = SergioLoadPreviewText.Warning(preview);
        PromoteButton.Content = SergioLoadPreviewText.Action(preview);
        for (var index = 0; index < preview.Aliases.Count; index++)
            PreviewDataGrid.Columns.Add(new DataGridTextColumn { Header = preview.Aliases[index], Binding = new System.Windows.Data.Binding($"[{index}]") { TargetNullValue = "<nulo>" } });
        PreviewDataGrid.ItemsSource = preview.Rows;
    }

    public bool Confirmed { get; private set; }
    private void PromoteButton_OnClick(object sender, RoutedEventArgs e) { Confirmed = true; DialogResult = true; }
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) { Confirmed = false; DialogResult = false; }
}
