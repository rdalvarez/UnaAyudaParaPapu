using System.Text;
using DuckDB.NET.Data;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class StockExportTests
{
    [Fact]
    public void DetailDeduplicatesMandatoryColumnsAndKeepsWeirdIdentifiers()
    {
        using var workspace = new ExportWorkspace();
        Seed(workspace, "Clave \"persona\"", "Código, OS", "SELECT");
        var destination = Path.Combine(workspace.Directory, "detalle.csv");

        var result = Service(workspace).ExportDetail(new StockDetailExportRequest(
            destination,
            ["SELECT", "Código, OS"],
            false));

        Assert.Equal(StockGenerateStatus.Completed, result.Status);
        var text = File.ReadAllText(destination);
        Assert.Contains("\"Clave \"\"persona\"\"\",\"Código, OS\",SELECT,VENDIDO", text);
        Assert.Equal(3, result.RowCount);
        Assert.Equal(Encoding.UTF8.GetPreamble(), File.ReadAllBytes(destination).Take(3));
    }

    [Fact]
    public void OnlyAvailableExcludesSoldAndReservedRows()
    {
        using var workspace = new ExportWorkspace();
        Seed(workspace, "CUIL", "CODIGO", "NOMBRE");
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "UPDATE stock_status SET sold = TRUE WHERE member_id = '020'");
            Execute(connection, "UPDATE stock_status SET extraction_id = 'reserved' WHERE member_id = '021'");
        }

        var destination = Path.Combine(workspace.Directory, "disponible.csv");
        var result = Service(workspace).ExportDetail(new StockDetailExportRequest(destination, [], true));

        Assert.Equal(1, result.RowCount);
        Assert.Contains("022", File.ReadAllText(destination));
        Assert.DoesNotContain("020", File.ReadAllText(destination));
        Assert.DoesNotContain("021", File.ReadAllText(destination));
    }

    [Fact]
    public void SoldHeaderStaysUniqueWhenBusinessColumnsCollide()
    {
        using var workspace = new ExportWorkspace();
        Seed(workspace, "CUIL", "VENDIDO", "estado_vendido");
        var destination = Path.Combine(workspace.Directory, "colision.csv");

        var result = Service(workspace).ExportDetail(new StockDetailExportRequest(destination, ["VENDIDO", "estado_vendido"], false));

        Assert.Equal(StockGenerateStatus.Completed, result.Status);
        var header = File.ReadAllText(destination).Split(["\r\n", "\n"], StringSplitOptions.None)[0];
        Assert.Equal("CUIL,VENDIDO,estado_vendido,ESTADO_VENDIDO_2", header);
    }

    [Fact]
    public void FailedExportLeavesDestinationFreeForRetry()
    {
        using var workspace = new ExportWorkspace();
        Seed(workspace, "CUIL", "CODIGO", "NOMBRE");
        var destination = Path.Combine(workspace.Directory, "reintento.csv");
        var failing = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory),
            beforePublish: _ => throw new IOException("disco lleno"));

        var failed = failing.ExportSummary(destination);
        var retried = Service(workspace).ExportSummary(destination);

        Assert.Equal(StockGenerateStatus.Failed, failed.Status);
        Assert.Equal(StockGenerateStatus.Completed, retried.Status);
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void ExistingOutputAndDatabasePathAreRejected()
    {
        using var workspace = new ExportWorkspace();
        Seed(workspace, "CUIL", "CODIGO", "NOMBRE");
        var destination = Path.Combine(workspace.Directory, "resumen.csv");
        File.WriteAllText(destination, "previo");

        var existing = Service(workspace).ExportSummary(destination);
        var database = Service(workspace).ExportSummary(workspace.DatabasePath);

        Assert.Equal(StockGenerateStatus.Failed, existing.Status);
        Assert.Equal("previo", File.ReadAllText(destination));
        Assert.Equal(StockGenerateStatus.Failed, database.Status);
        Assert.True(File.Exists(workspace.DatabasePath));
    }

    [Fact]
    public void SummaryUsesSnapshotNamesAndDoesNotFollowCatalogRenameAfterExport()
    {
        using var workspace = new ExportWorkspace();
        Seed(workspace, "CUIL", "CODIGO", "NOMBRE");
        var destination = Path.Combine(workspace.Directory, "resumen.csv");
        var service = Service(workspace);
        service.ExportSummary(destination);
        service.SaveCatalog(new Dictionary<string, string> { ["OSDE"] = "Renombrada" });

        Assert.Contains("OSDE", File.ReadAllText(destination));
        Assert.DoesNotContain("Renombrada", File.ReadAllText(destination));
    }

    private static StockSnapshotService Service(ExportWorkspace workspace) =>
        new(new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)), new StockCatalogStore(workspace.CatalogDirectory));

    private static void Seed(ExportWorkspace workspace, string key, string code, string description)
    {
        using var connection = Open(workspace.DatabasePath);
        Execute(connection, "CREATE TABLE sergio_current(" + Quote(key) + " VARCHAR PRIMARY KEY, " + Quote(code) + " VARCHAR, " + Quote(description) + " VARCHAR)");
        Execute(connection, "INSERT INTO sergio_current VALUES ('020', 'osde', 'OSDE'), ('021', 'osde,\"nota\"', 'linea\n2'), ('022', '', NULL)");
        Execute(connection, "CREATE TABLE sergio_load_metadata(source_file_name VARCHAR, loaded_at_utc TIMESTAMP, row_count BIGINT, selected_primary_key_alias VARCHAR, schema_version VARCHAR)");
        using var metadata = connection.CreateCommand();
        metadata.CommandText = "INSERT INTO sergio_load_metadata VALUES ('origen.xlsx', ?, 3, ?, NULL)";
        metadata.Parameters.Add(new DuckDBParameter { Value = DateTime.UtcNow });
        metadata.Parameters.Add(new DuckDBParameter { Value = key });
        metadata.ExecuteNonQuery();
        new StockSnapshotService(new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)), new StockCatalogStore(workspace.CatalogDirectory))
            .Generate(new StockGenerateRequest(key, code, description, false));
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    private static DuckDBConnection Open(string path)
    {
        var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();
        return connection;
    }
    private static void Execute(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class ExportWorkspace : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(Directory, "test.duckdb");
        public string CatalogDirectory => Path.Combine(Directory, "catalog");
        public ExportWorkspace() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { try { System.IO.Directory.Delete(Directory, true); } catch { } }
    }
}
