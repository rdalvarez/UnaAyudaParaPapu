using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class StockCatalogTests
{
    [Fact]
    public void EmptyCodeRemainsVisibleAndExtractableAsEmptyKey()
    {
        using var workspace = new CatalogWorkspace();
        Seed(workspace, ("20", "osde", "OSDE"), ("21", "  ", "   "));

        var summary = Service(workspace).Generate(new StockGenerateRequest("CUIL", "CODIGO", "NOMBRE", false)).Summary!;

        var empty = Assert.Single(summary.Groups, group => group.NormalizedCode == "");
        Assert.Equal(StockNames.EmptyCodeDisplayName, empty.DisplayName);
        Assert.Equal(1, empty.Available);
    }

    [Fact]
    public void AnyNonemptyDescriptionSeedsMissingNameWithoutOverridingSavedName()
    {
        using var workspace = new CatalogWorkspace();
        Seed(workspace, ("20", "osde", "Zulu"), ("22", "osde", "Alfa"));
        var service = Service(workspace);
        service.SaveCatalog(new Dictionary<string, string> { ["OSDE"] = "Guardado" });

        var summary = service.Generate(new StockGenerateRequest("CUIL", "CODIGO", "NOMBRE", false)).Summary!;

        Assert.Equal("Guardado", summary.Groups.Single().DisplayName);
        Assert.Equal("Guardado", new StockCatalogStore(workspace.CatalogDirectory).Load().NamesByNormalizedCode["OSDE"]);
    }

    [Fact]
    public void InvalidCatalogJsonIsNotOverwritten()
    {
        using var workspace = new CatalogWorkspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.CatalogPath)!);
        File.WriteAllText(workspace.CatalogPath, "{ invalid");
        Seed(workspace, ("20", "osde", "OSDE"));

        var result = Service(workspace).Generate(new StockGenerateRequest("CUIL", "CODIGO", "NOMBRE", false));

        Assert.Equal(StockGenerateStatus.Completed, result.Status);
        Assert.NotEmpty(result.Summary!.CatalogWarnings);
        Assert.Equal("{ invalid", File.ReadAllText(workspace.CatalogPath));
        Assert.Equal("OSDE", result.Summary.Groups.Single().DisplayName);
    }

    [Fact]
    public void MissingCodeSeedsAnyNonemptyGroupedDescription()
    {
        using var workspace = new CatalogWorkspace();
        Seed(workspace, ("20", "osde", "Zulu"), ("22", "osde", "Alfa"));

        var summary = Service(workspace).Generate(new StockGenerateRequest("CUIL", "CODIGO", "NOMBRE", false)).Summary!;

        Assert.False(string.IsNullOrWhiteSpace(summary.Groups.Single().DisplayName));
        Assert.Contains(summary.Groups.Single().DisplayName, new[] { "Zulu", "Alfa" });
    }

    [Fact]
    public void ExplicitSaveWritesOnlyNonemptyNormalizedNames()
    {
        using var workspace = new CatalogWorkspace();
        var store = new StockCatalogStore(workspace.CatalogDirectory);

        store.Save(new Dictionary<string, string> { [" osde "] = "  Nombre  ", ["vacio"] = "   " });
        var loaded = store.Load();

        Assert.Equal("Nombre", loaded.NamesByNormalizedCode["OSDE"]);
        Assert.False(loaded.NamesByNormalizedCode.ContainsKey("VACIO"));
    }

    private static StockSnapshotService Service(CatalogWorkspace workspace) =>
        new(new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)), new StockCatalogStore(workspace.CatalogDirectory));

    private static void Seed(CatalogWorkspace workspace, params (string Key, string Code, string Name)[] rows)
    {
        using var connection = new DuckDB.NET.Data.DuckDBConnection(new DuckDB.NET.Data.DuckDBConnectionStringBuilder { DataSource = workspace.DatabasePath }.ConnectionString);
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE sergio_current(CUIL VARCHAR PRIMARY KEY, CODIGO VARCHAR, NOMBRE VARCHAR)";
        create.ExecuteNonQuery();
        foreach (var row in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO sergio_current VALUES (?, ?, ?)";
            insert.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter { Value = row.Key });
            insert.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter { Value = row.Code });
            insert.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter { Value = row.Name });
            insert.ExecuteNonQuery();
        }

        using var metadata = connection.CreateCommand();
        metadata.CommandText = "CREATE TABLE sergio_load_metadata(source_file_name VARCHAR, loaded_at_utc TIMESTAMP, row_count BIGINT, selected_primary_key_alias VARCHAR, schema_version VARCHAR); INSERT INTO sergio_load_metadata VALUES ('origen.xlsx', CURRENT_TIMESTAMP, ?, 'CUIL', NULL)";
        metadata.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter { Value = rows.Length });
        metadata.ExecuteNonQuery();
    }

    private sealed class CatalogWorkspace : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(directory, "test.duckdb");
        public string CatalogDirectory => Path.Combine(directory, "catalog");
        public string CatalogPath => Path.Combine(CatalogDirectory, StockCatalogStore.RelativePath);
        public CatalogWorkspace() => Directory.CreateDirectory(directory);
        public void Dispose() { try { Directory.Delete(directory, true); } catch { } }
    }
}
