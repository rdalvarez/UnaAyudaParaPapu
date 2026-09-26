using System.Windows;
using PapaPersonas.Infrastructure.Stock.Paso4;

namespace PapaPersonas.App.Processes;

public partial class Paso4ColumnSelectionDialog : Window
{
    private readonly List<System.Windows.Controls.CheckBox> _checkBoxes = [];

    public Paso4ColumnSelectionDialog(IReadOnlyList<string> selectedColumns)
    {
        InitializeComponent();
        BuildChecklist(selectedColumns);
    }

    public IReadOnlyList<string> SelectedColumns { get; private set; } = [];

    private void BuildChecklist(IReadOnlyList<string> selectedColumns)
    {
        var mandatory = new HashSet<string>(Paso4ColumnCatalog.MandatoryColumns, StringComparer.OrdinalIgnoreCase);
        var current = new HashSet<string>(selectedColumns, StringComparer.OrdinalIgnoreCase);

        foreach (var column in Paso4ColumnCatalog.AvailableColumns)
        {
            var isMandatory = mandatory.Contains(column);
            var checkBox = new System.Windows.Controls.CheckBox
            {
                Content = column,
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = isMandatory || current.Contains(column),
                IsEnabled = !isMandatory
            };

            ColumnsStackPanel.Children.Add(checkBox);
            _checkBoxes.Add(checkBox);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SelectedColumns = _checkBoxes
            .Where(cb => cb.IsChecked == true)
            .Select(cb => cb.Content?.ToString() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
