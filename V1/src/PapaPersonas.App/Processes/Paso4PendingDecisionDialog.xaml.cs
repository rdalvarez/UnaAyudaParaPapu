using System.Windows;
using PapaPersonas.Core.Stock.Paso4;

namespace PapaPersonas.App.Processes;

public partial class Paso4PendingDecisionDialog : Window
{
    public Paso4PendingDecisionDialog(Paso4PendingExtractionState pending)
    {
        InitializeComponent();
        MessageTextBlock.Text =
            $"Hay una extracción pendiente iniciada el {pending.StartedUtc:yyyy-MM-dd HH:mm:ss}.\n" +
            $"Archivo destino: {pending.OutputPath}\n" +
            $"Filas esperadas: {pending.ExpectedRows}.";
    }

    public Paso4PendingDecision Decision { get; private set; } = Paso4PendingDecision.None;

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        Decision = Paso4PendingDecision.Retry;
        DialogResult = true;
        Close();
    }

    private void CancelPending_Click(object sender, RoutedEventArgs e)
    {
        Decision = Paso4PendingDecision.Cancel;
        DialogResult = true;
        Close();
    }
}

public enum Paso4PendingDecision
{
    None = 0,
    Retry = 1,
    Cancel = 2
}
