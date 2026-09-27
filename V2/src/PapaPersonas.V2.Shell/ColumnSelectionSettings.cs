using System.Text.Json;
using System.Text.Json.Serialization;

namespace PapaPersonas.V2.Shell;

public sealed class ColumnSelectionSettingsDocument
{
    [JsonPropertyName("configuracionPaso1")]
    public Paso1ColumnSelectionSettings ConfiguracionPaso1 { get; set; } = new();
}

public sealed class Paso1ColumnSelectionSettings
{
    [JsonPropertyName("columnasDeseleccionadas")]
    public List<string> DeselectedColumnNames { get; set; } = [];
}

public sealed record ColumnSelectionSettingsLoadResult(
    ColumnSelectionSettingsDocument Document,
    bool WasReset);

public sealed class ColumnSelectionSettingsStore
{
    public static string ProductionPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PapaPersonas", "V2", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public ColumnSelectionSettingsStore(string? pathOverride = null)
    {
        _path = string.IsNullOrWhiteSpace(pathOverride) ? ProductionPath : pathOverride;
    }

    public string Path => _path;

    public ColumnSelectionSettingsLoadResult Load()
    {
        if (!File.Exists(_path))
        {
            return new(new ColumnSelectionSettingsDocument(), false);
        }

        try
        {
            var document = JsonSerializer.Deserialize<ColumnSelectionSettingsDocument>(File.ReadAllText(_path), JsonOptions)
                ?? throw new JsonException("The settings document is null.");
            document.ConfiguracionPaso1 ??= new Paso1ColumnSelectionSettings();
            document.ConfiguracionPaso1.DeselectedColumnNames ??= [];
            document.ConfiguracionPaso1.DeselectedColumnNames = NormalizeNames(document.ConfiguracionPaso1.DeselectedColumnNames);
            return new(document, false);
        }
        catch (JsonException)
        {
            var defaults = new ColumnSelectionSettingsDocument();
            Save(defaults);
            return new(defaults, true);
        }
    }

    public void Save(ColumnSelectionSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException($"The settings path has no directory: {_path}");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var normalized = new ColumnSelectionSettingsDocument
            {
                ConfiguracionPaso1 = new Paso1ColumnSelectionSettings
                {
                    DeselectedColumnNames = NormalizeNames(document.ConfiguracionPaso1?.DeselectedColumnNames ?? [])
                }
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static List<string> NormalizeNames(IEnumerable<string> names) => names
        .Where(name => name is not null)
        .Distinct(StringComparer.Ordinal)
        .ToList();
}

public static class ColumnSelectionMemory
{
    public static IReadOnlyList<string> CaptureDeselected(
        IReadOnlyList<DetectedColumn> columns,
        Func<DetectedColumn, bool> isSelected)
    {
        ArgumentNullException.ThrowIfNull(isSelected);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (!isSelected(column) && seen.Add(column.DisplayName))
            {
                result.Add(column.DisplayName);
            }
        }

        return result;
    }

    public static int ApplyDeselected(
        IReadOnlyList<DetectedColumn> columns,
        IEnumerable<string> deselectedNames,
        Action<DetectedColumn, bool> setSelected)
    {
        ArgumentNullException.ThrowIfNull(setSelected);
        var names = new HashSet<string>(deselectedNames, StringComparer.Ordinal);
        var applied = 0;
        foreach (var column in columns)
        {
            var isDeselected = names.Contains(column.DisplayName);
            setSelected(column, !isDeselected);
            if (isDeselected)
            {
                applied++;
            }
        }

        return applied;
    }
}
