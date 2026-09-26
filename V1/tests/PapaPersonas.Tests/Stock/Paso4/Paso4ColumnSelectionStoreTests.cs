using System.Text;
using System.Text.Json;
using PapaPersonas.Infrastructure.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class Paso4ColumnSelectionStoreTests
{
    [Fact]
    public void Load_WhenMissing_ReturnsDefault17Columns()
    {
        using var ctx = CreateContext();
        var store = new Paso4ColumnSelectionStore(ctx.BaseDirectory);

        var loaded = store.Load();

        Assert.Equal(17, loaded.SelectedColumns.Count);
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public void Load_WhenCorrupt_ReturnsDefaultAndWarning()
    {
        using var ctx = CreateContext();
        Directory.CreateDirectory(Path.Combine(ctx.BaseDirectory, "config"));
        File.WriteAllText(Path.Combine(ctx.BaseDirectory, "config", "paso4-columnas.json"), "{bad json", Encoding.UTF8);

        var store = new Paso4ColumnSelectionStore(ctx.BaseDirectory);
        var loaded = store.Load();

        Assert.Equal(17, loaded.SelectedColumns.Count);
        Assert.NotEmpty(loaded.Warnings);
    }

    [Fact]
    public void Load_WhenCorrupt_PreservesExactTechnicalDetailInWarning()
    {
        using var ctx = CreateContext();
        Directory.CreateDirectory(Path.Combine(ctx.BaseDirectory, "config"));
        const string invalidJson = "{bad json";
        var configPath = Path.Combine(ctx.BaseDirectory, "config", "paso4-columnas.json");
        File.WriteAllText(configPath, invalidJson, Encoding.UTF8);

        var expectedException = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<object>(invalidJson));
        var loaded = new Paso4ColumnSelectionStore(ctx.BaseDirectory).Load();

        Assert.Single(loaded.Warnings);
        Assert.Equal(
            $"El JSON de selección de columnas no es válido. Se aplicaron las predeterminadas. Detalle técnico: {expectedException.Message}",
            loaded.Warnings[0]);
    }

    [Fact]
    public void Load_IgnoresUnknown_AndForcesMandatoryFields()
    {
        using var ctx = CreateContext();
        Directory.CreateDirectory(Path.Combine(ctx.BaseDirectory, "config"));
        File.WriteAllText(
            Path.Combine(ctx.BaseDirectory, "config", "paso4-columnas.json"),
            "{\"selectedColumns\":[\"APELLIDO\",\"UNKNOWN\"]}",
            Encoding.UTF8);

        var store = new Paso4ColumnSelectionStore(ctx.BaseDirectory);
        var loaded = store.Load();

        Assert.Contains("CUIL", loaded.SelectedColumns);
        Assert.Contains("CODIGOOS", loaded.SelectedColumns);
        Assert.Contains("OBRASOCIAL", loaded.SelectedColumns);
        Assert.Contains("APELLIDO", loaded.SelectedColumns);
        Assert.DoesNotContain("UNKNOWN", loaded.SelectedColumns);
    }

    [Fact]
    public void Save_RoundTrip_AndAtomicReplacement()
    {
        using var ctx = CreateContext();
        var store = new Paso4ColumnSelectionStore(ctx.BaseDirectory);

        store.Save(["APELLIDO", "NOMBRE"]);
        var loaded = store.Load();

        Assert.Contains("CUIL", loaded.SelectedColumns);
        Assert.Contains("CODIGOOS", loaded.SelectedColumns);
        Assert.Contains("OBRASOCIAL", loaded.SelectedColumns);
        Assert.Contains("APELLIDO", loaded.SelectedColumns);
        Assert.Contains("NOMBRE", loaded.SelectedColumns);
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
