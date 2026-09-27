using System.Windows;
using System.Windows.Controls;
using PapaPersonas.V2.Shell;

namespace PapaPersonas.V2.App;

public partial class SergioHeaderIssueDialog : Window
{
    public SergioHeaderIssueDialog(SergioHeaderPreflightResult result)
    {
        InitializeComponent();
        SummaryTextBlock.Text = $"Vacíos: {result.EmptyHeaderCount}. Duplicados: {result.DuplicateGroups.Count} grupo(s), {result.DuplicateOccurrenceCount} ocurrencia(s) repetida(s).";
        MappingsItemsControl.ItemsSource = result.Columns
            .Where(column => column.IssueKind != SergioHeaderIssueKind.None)
            .Select(column => new TextBlock
            {
                Text = $"Posición {column.Position + 1}: {Display(column.RawName)} → {column.ProposedAlias} ({Describe(column.IssueKind)})",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            }).ToArray();
    }

    public bool Accepted { get; private set; }

    private void ContinueButton_OnClick(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
    }

    private static string Display(string? value) => string.IsNullOrEmpty(value) ? "<vacío>" : value.Replace("\r", "\\r").Replace("\n", "\\n");

    private static string Describe(SergioHeaderIssueKind issueKind) => issueKind switch
    {
        SergioHeaderIssueKind.Empty => "vacío",
        SergioHeaderIssueKind.Duplicate => "duplicado exacto",
        _ when issueKind.HasFlag(SergioHeaderIssueKind.Empty) && issueKind.HasFlag(SergioHeaderIssueKind.Duplicate) => "vacío y duplicado",
        _ => issueKind.ToString()
    };
}
