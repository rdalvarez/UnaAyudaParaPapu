using System.Text;
using System.Windows;
using System.Windows.Controls;
using PapaPersonas.Core.Activity;
using PapaPersonas.Core.Import;
using PapaPersonas.Infrastructure.Database;

namespace PapaPersonas.App.Controls;

public partial class ActivityConsoleControl : System.Windows.Controls.UserControl
{
    private ActivityBuffer? _buffer;

    public ActivityConsoleControl()
    {
        InitializeComponent();
    }

    public void AttachBuffer(ActivityBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Render();
    }

    public void Add(ActivitySeverity severity, string phase, string message)
    {
        if (_buffer is null)
        {
            return;
        }

        _buffer.Add(severity, phase, message);
        Render(autoScrollToBottom: true);
    }

    public void ClearActivity()
    {
        _buffer?.Clear();
        Render();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ConsoleTextBox.Text))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(ConsoleTextBox.Text);
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            System.Windows.MessageBox.Show(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo copiar la actividad al portapapeles. Volvé a intentar.",
                    ex),
                "Error al copiar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearActivity();
    }

    private void Render(bool autoScrollToBottom = false)
    {
        if (_buffer is null)
        {
            ConsoleTextBox.Text = string.Empty;
            return;
        }

        var entries = _buffer.SnapshotChronological();
        if (entries.Count == 0)
        {
            ConsoleTextBox.Text = string.Empty;
            return;
        }

        var builder = new StringBuilder(capacity: entries.Count * 96);
        foreach (var entry in entries)
        {
            builder
                .Append('[')
                .Append(entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
                .Append("] [")
                .Append(entry.Severity.ToString().ToUpperInvariant())
                .Append("] [")
                .Append(entry.Phase)
                .Append("] ")
                .Append(entry.Message)
                .AppendLine();
        }

        ConsoleTextBox.Text = builder.ToString();
        if (autoScrollToBottom)
        {
            ConsoleTextBox.CaretIndex = ConsoleTextBox.Text.Length;
            ConsoleTextBox.ScrollToEnd();
        }
    }
}
