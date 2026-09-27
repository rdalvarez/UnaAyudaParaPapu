using System.Text;
using ExcelDataReader;

namespace PapaPersonas.V2.Shell;

public sealed record DetectedColumn(int Position, string DisplayName);

public enum CleaningAction
{
    RemoveDuplicates,
    RemoveEmptyValues
}

public sealed record CleaningPlanStep(
    CleaningAction Action,
    int ColumnPosition,
    string ColumnName,
    int StepNumber)
{
    public string VisibleDescription =>
        $"{StepNumber}. {Action switch
        {
            CleaningAction.RemoveDuplicates => "Quitar duplicados",
            CleaningAction.RemoveEmptyValues => "Quitar valores vacíos",
            _ => throw new ArgumentOutOfRangeException()
        }} por {ColumnName}";
}

public sealed class CleaningPlan
{
    private readonly List<CleaningPlanStep> _steps = [];

    public IReadOnlyList<CleaningPlanStep> Steps => _steps;

    public CleaningPlanStep Add(CleaningAction action, DetectedColumn column)
    {
        var step = new CleaningPlanStep(action, column.Position, column.DisplayName, _steps.Count + 1);
        _steps.Add(step);
        return step;
    }

    public int Clear()
    {
        var removed = _steps.Count;
        _steps.Clear();
        return removed;
    }
}

public static class HeaderPresentation
{
    public static IReadOnlyList<DetectedColumn> Normalize(IReadOnlyList<string?> headers)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DetectedColumn>(headers.Count);
        var unnamed = 0;

        for (var position = 0; position < headers.Count; position++)
        {
            var baseName = headers[position]?.Trim() ?? string.Empty;
            if (baseName.Length == 0)
            {
                unnamed++;
                baseName = $"Columna sin nombre {unnamed}";
            }

            counts.TryGetValue(baseName, out var occurrence);
            occurrence++;
            counts[baseName] = occurrence;
            result.Add(new DetectedColumn(position, occurrence == 1 ? baseName : $"{baseName} ({occurrence})"));
        }

        return result;
    }
}

public static class RejectedFolderPolicy
{
    public static string GetDefault(string inputFilePath) =>
        Path.Combine(Path.GetDirectoryName(inputFilePath) ?? string.Empty, "rech");
}

public interface IHeaderReader
{
    IReadOnlyList<string?> ReadHeaders(string filePath);
}

public static class HeaderReaderConfiguration
{
    public static ExcelReaderConfiguration Create()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return new ExcelReaderConfiguration
        {
            SinglePassMode = true
        };
    }
}

public sealed class ExcelDataReaderHeaderReader : IHeaderReader
{
    private static bool _encodingProviderRegistered;

    public IReadOnlyList<string?> ReadHeaders(string filePath)
    {
        EnsureEncodingProviderRegistered();
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream, HeaderReaderConfiguration.Create());
        if (!reader.Read())
        {
            return [];
        }

        var headers = new List<string?>(reader.FieldCount);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            headers.Add(reader.GetValue(index)?.ToString());
        }

        return headers;
    }

    private static void EnsureEncodingProviderRegistered()
    {
        if (_encodingProviderRegistered)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encodingProviderRegistered = true;
    }
}

public sealed class ActivityLog
{
    private readonly Func<DateTime> _now;
    private readonly List<string> _entries = [];

    public ActivityLog(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.Now);
    }

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string section, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentNullException.ThrowIfNull(message);

        _entries.Insert(0, $"[{section}][{_now():dd/MM/yyyy HH:mm:ss}]{message}");
    }
}
