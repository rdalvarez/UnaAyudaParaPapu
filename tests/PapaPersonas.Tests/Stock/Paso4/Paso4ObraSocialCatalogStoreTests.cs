using System.Text;
using System.Text.Json;
using PapaPersonas.Infrastructure.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class Paso4ObraSocialCatalogStoreTests
{
    [Fact]
    public void Load_WhenMissing_ReturnsEmptyCatalogWithoutWarnings()
    {
        using var ctx = CreateContext();
        var store = new Paso4ObraSocialCatalogStore(ctx.BaseDirectory);

        var loaded = store.Load();

        Assert.Empty(loaded.NamesByNormalizedCode);
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public void Load_WhenCorrupt_ReturnsEmptyCatalogAndWarning()
    {
        using var ctx = CreateContext();
        Directory.CreateDirectory(Path.Combine(ctx.BaseDirectory, "config"));
        File.WriteAllText(Path.Combine(ctx.BaseDirectory, "config", "paso4-obras-sociales.json"), "{bad json", Encoding.UTF8);

        var store = new Paso4ObraSocialCatalogStore(ctx.BaseDirectory);
        var loaded = store.Load();

        Assert.Empty(loaded.NamesByNormalizedCode);
        Assert.NotEmpty(loaded.Warnings);
    }

    [Fact]
    public void Load_WhenCorrupt_PreservesExactTechnicalDetailInWarning()
    {
        using var ctx = CreateContext();
        Directory.CreateDirectory(Path.Combine(ctx.BaseDirectory, "config"));
        const string invalidJson = "{bad json";
        var configPath = Path.Combine(ctx.BaseDirectory, "config", "paso4-obras-sociales.json");
        File.WriteAllText(configPath, invalidJson, Encoding.UTF8);

        var expectedException = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<object>(invalidJson));
        var loaded = new Paso4ObraSocialCatalogStore(ctx.BaseDirectory).Load();

        Assert.Single(loaded.Warnings);
        Assert.Equal(
            $"El JSON del catálogo de obras sociales no es válido. Se usaron nombres sugeridos. Detalle técnico: {expectedException.Message}",
            loaded.Warnings[0]);
    }

    [Fact]
    public void Load_WhenStructureInvalid_ReturnsEmptyCatalogAndWarning()
    {
        using var ctx = CreateContext();
        Directory.CreateDirectory(Path.Combine(ctx.BaseDirectory, "config"));
        File.WriteAllText(
            Path.Combine(ctx.BaseDirectory, "config", "paso4-obras-sociales.json"),
            "{\"other\":[]}",
            Encoding.UTF8);

        var loaded = new Paso4ObraSocialCatalogStore(ctx.BaseDirectory).Load();

        Assert.Empty(loaded.NamesByNormalizedCode);
        Assert.Equal(
            ["La estructura del catálogo de obras sociales no es válida. Se usaron nombres sugeridos."],
            loaded.Warnings);
    }

    [Fact]
    public void Save_RoundTrip_NormalizesCodes_AndUsesAtomicReplacement()
    {
        using var ctx = CreateContext();
        var store = new Paso4ObraSocialCatalogStore(ctx.BaseDirectory);

        store.Save(new Dictionary<string, string>
        {
            [" os1 "] = "  Obra Unificada  ",
            ["OS2"] = "   ",
            [""] = "Sin código"
        });
        var loaded = store.Load();

        Assert.Equal("Obra Unificada", loaded.NamesByNormalizedCode["OS1"]);
        Assert.Equal("Sin código", loaded.NamesByNormalizedCode[""]);
        Assert.False(loaded.NamesByNormalizedCode.ContainsKey("OS2"));
        Assert.Empty(loaded.Warnings);
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(ctx.BaseDirectory, "config")), x => x.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    private static TestContext CreateContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TestContext(root);
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(string baseDirectory)
        {
            BaseDirectory = baseDirectory;
        }

        public string BaseDirectory { get; }

        public void Dispose()
        {
            if (Directory.Exists(BaseDirectory))
            {
                Directory.Delete(BaseDirectory, recursive: true);
            }
        }
    }
}
