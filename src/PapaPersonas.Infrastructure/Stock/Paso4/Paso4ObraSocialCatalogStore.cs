using System.Text.Json;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Stock.Paso4;

namespace PapaPersonas.Infrastructure.Stock.Paso4;

public sealed class Paso4ObraSocialCatalogStore : IPaso4ObraSocialCatalogStore
{
    private const string RelativePath = "config\\paso4-obras-sociales.json";

    private readonly string _baseDirectory;

    public Paso4ObraSocialCatalogStore(string? baseDirectory = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PapaPersonas")
            : baseDirectory;
    }

    public Paso4ObraSocialCatalogState Load()
    {
        var fullPath = Path.Combine(_baseDirectory, RelativePath);
        if (!File.Exists(fullPath))
        {
            return new Paso4ObraSocialCatalogState(new Dictionary<string, string>(StringComparer.Ordinal), []);
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var payload = JsonSerializer.Deserialize<CatalogPayload>(json);
            if (payload?.NamesByNormalizedCode is null)
            {
                return new Paso4ObraSocialCatalogState(
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    ["La estructura del catálogo de obras sociales no es válida. Se usaron nombres sugeridos."]);
            }

            return new Paso4ObraSocialCatalogState(Normalize(payload.NamesByNormalizedCode), []);
        }
        catch (Exception ex)
        {
            return new Paso4ObraSocialCatalogState(
                new Dictionary<string, string>(StringComparer.Ordinal),
                [UserFacingExceptionMessage.WithTechnicalDetail(
                    "El JSON del catálogo de obras sociales no es válido. Se usaron nombres sugeridos.",
                    ex)]);
        }
    }

    public void Save(IReadOnlyDictionary<string, string> namesByNormalizedCode)
    {
        ArgumentNullException.ThrowIfNull(namesByNormalizedCode);

        var payload = new CatalogPayload(Normalize(namesByNormalizedCode));
        var fullPath = Path.Combine(_baseDirectory, RelativePath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException("La ruta del catálogo de obras sociales no es válida.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(payload);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, fullPath, overwrite: true);
    }

    private static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string> namesByNormalizedCode)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in namesByNormalizedCode)
        {
            var code = Paso4ObraSocialPresentation.NormalizeCode(pair.Key);
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            normalized[code] = pair.Value.Trim();
        }

        return normalized;
    }

    private sealed record CatalogPayload(Dictionary<string, string>? NamesByNormalizedCode);
}
