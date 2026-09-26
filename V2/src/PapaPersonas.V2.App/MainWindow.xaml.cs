using System.Windows;
using System.Windows.Controls;
using PapaPersonas.V2.Shell;

namespace PapaPersonas.V2.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyTabCatalog();
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
}
