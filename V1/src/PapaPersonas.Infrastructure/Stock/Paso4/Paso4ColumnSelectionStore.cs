using System.Text.Json;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Stock.Paso4;

namespace PapaPersonas.Infrastructure.Stock.Paso4;

public sealed class Paso4ColumnSelectionStore : IPaso4ColumnSelectionStore
{
    private const string RelativePath = "config\\paso4-columnas.json";

    private readonly string _baseDirectory;

    public Paso4ColumnSelectionStore(string? baseDirectory = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PapaPersonas")
            : baseDirectory;
    }

    public Paso4ColumnSelectionState Load()
    {
        var fullPath = Path.Combine(_baseDirectory, RelativePath);
        if (!File.Exists(fullPath))
        {
            return new Paso4ColumnSelectionState(Paso4ColumnCatalog.DefaultColumns, []);
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var payload = JsonSerializer.Deserialize<ColumnPayload>(json);
            if (payload?.SelectedColumns is null)
            {
                return new Paso4ColumnSelectionState(Paso4ColumnCatalog.DefaultColumns, ["La estructura de selección de columnas no es válida. Se aplicaron las predeterminadas."]);
            }

            var normalized = Paso4ColumnCatalog.NormalizeSelectedColumns(payload.SelectedColumns);
            return new Paso4ColumnSelectionState(normalized, []);
        }
        catch (Exception ex)
        {
            return new Paso4ColumnSelectionState(
                Paso4ColumnCatalog.DefaultColumns,
                [UserFacingExceptionMessage.WithTechnicalDetail(
                    "El JSON de selección de columnas no es válido. Se aplicaron las predeterminadas.",
                    ex)]);
        }
    }

    public void Save(IReadOnlyList<string> selectedColumns)
    {
        var normalized = Paso4ColumnCatalog.NormalizeSelectedColumns(selectedColumns);
        var payload = new ColumnPayload(normalized);
        var fullPath = Path.Combine(_baseDirectory, RelativePath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException("La ruta de selección de columnas no es válida.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(payload);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, fullPath, overwrite: true);
    }

    private sealed record ColumnPayload(IReadOnlyList<string> SelectedColumns);
}
