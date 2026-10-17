using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class StockExtractionTests
{
    [Fact]
    public void ExtractionReservesPendingAndFinalizesSameMembers()
    {
        using var workspace = new ExtractionWorkspace();
        var service = Seed(workspace);
        var destination = Path.Combine(workspace.Directory, "venta.csv");

        var result = service.BeginExtraction(Request(destination, [new("osde", 2), new("", 1)], "venta 1"));

        Assert.Equal(StockGenerateStatus.Completed, result.Status);
        Assert.Equal(3, result.RowCount);
        using var connection = Open(workspace.DatabasePath);
        Assert.Equal(3, Scalar(connection, "SELECT COUNT(*) FROM stock_status WHERE sold = TRUE"));
        Assert.Equal("VENTA 1", ScalarText(connection, "SELECT sale_reference FROM stock_extractions"));
        Assert.Contains("020", File.ReadAllText(destination));
    }

    [Fact]
    public void OversaleAndSecondPendingDoNotMutate()
    {
        using var workspace = new ExtractionWorkspace();
        var service = Seed(workspace);
        var first = service.BeginExtraction(Request(Path.Combine(workspace.Directory, "ok.csv"), [new("OSDE", 2)], "  "));
        var oversale = service.BeginExtraction(Request(Path.Combine(workspace.Directory, "over.csv"), [new("OSDE", 9)], "otra"));
        var pending = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory),
            beforePublish: _ => throw new IOException("corte"));
        pending.BeginExtraction(Request(Path.Combine(workspace.Directory, "pending.csv"), [new("", 1)], null));
        var second = service.BeginExtraction(Request(Path.Combine(workspace.Directory, "second.csv"), [new("", 1)], null));

        Assert.Equal(StockGenerateStatus.Completed, first.Status);
        Assert.Equal(StockGenerateStatus.ValidationFailed, oversale.Status);
        Assert.Equal(StockGenerateStatus.ValidationFailed, second.Status);
        Assert.False(File.Exists(Path.Combine(workspace.Directory, "over.csv")));
        using var connection = Open(workspace.DatabasePath);
        Assert.Equal(2, Scalar(connection, "SELECT COUNT(*) FROM stock_status WHERE sold = TRUE"));
        Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM stock_extractions WHERE state = 'completed'"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveQuantityFailsBeforeMutation(int quantity)
    {
        using var workspace = new ExtractionWorkspace();
        var service = Seed(workspace);

        var result = service.BeginExtraction(Request(Path.Combine(workspace.Directory, "bad.csv"), [new("OSDE", quantity)], null));

        Assert.Equal(StockGenerateStatus.ValidationFailed, result.Status);
        using var connection = Open(workspace.DatabasePath);
        Assert.Equal(0, Scalar(connection, "SELECT COUNT(*) FROM stock_extractions"));
    }

    [Fact]
    public void PublishFailureLeavesRecoverablePendingAndRetryUsesSameMembers()
    {
        using var workspace = new ExtractionWorkspace();
        var failing = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory),
            beforePublish: _ => throw new IOException("disco lleno"));
        Seed(workspace);
        var destination = Path.Combine(workspace.Directory, "pendiente.csv");
        var failed = failing.BeginExtraction(Request(destination, [new("OSDE", 1)], "ref"));
        Assert.Equal(StockGenerateStatus.Failed, failed.Status);
        Assert.False(File.Exists(destination));

        var retry = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory)).RetryPending();

        Assert.True(retry.Status == StockGenerateStatus.Completed, retry.Message);
        Assert.Equal(1, retry.RowCount);
        Assert.Contains("020", File.ReadAllText(destination));
    }

    [Fact]
    public void InterruptedFinalizeRetriesSamePublishedFileWithoutDoubleSale()
    {
        using var workspace = new ExtractionWorkspace();
        var failing = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory),
            beforeFinalize: _ => throw new IOException("corte después de publicar"));
        Seed(workspace);
        var destination = Path.Combine(workspace.Directory, "publicada.csv");
        var stale = Path.Combine(workspace.Directory, ".publicada.csv.tmp");
        File.WriteAllText(stale, "ajeno");
        var failed = failing.BeginExtraction(Request(destination, [new("OSDE", 1)], "ref"));

        Assert.Equal(StockGenerateStatus.Failed, failed.Status);
        Assert.Equal("ajeno", File.ReadAllText(stale));
        var retry = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory)).RetryPending();

        Assert.True(retry.Status == StockGenerateStatus.Completed, retry.Message);
        Assert.Equal("ajeno", File.ReadAllText(stale));
        using var connection = Open(workspace.DatabasePath);
        Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM stock_status WHERE sold = TRUE"));
        Assert.Equal("REF", ScalarText(connection, "SELECT sale_reference FROM stock_extractions WHERE state = 'completed'"));
    }

    [Fact]
    public void DifferentExistingOutputStaysPendingUntilAlternatePath()
    {
        using var workspace = new ExtractionWorkspace();
        var failing = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory),
            beforeFinalize: _ => throw new IOException("corte"));
        Seed(workspace);
        var destination = Path.Combine(workspace.Directory, "conflicto.csv");
        failing.BeginExtraction(Request(destination, [new("OSDE", 1)], "ref"));
        File.WriteAllText(destination, "contenido ajeno");

        var service = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory));
        var conflict = service.RetryPending();
        Assert.Equal(StockGenerateStatus.Failed, conflict.Status);
        Assert.Equal("contenido ajeno", File.ReadAllText(destination));

        var alternate = Path.Combine(workspace.Directory, "alternativa.csv");
        var recovered = service.RetryPending(alternate);

        Assert.True(recovered.Status == StockGenerateStatus.Completed, recovered.Message);
        Assert.Equal("contenido ajeno", File.ReadAllText(destination));
        Assert.Contains("020", File.ReadAllText(alternate));
        using var connection = Open(workspace.DatabasePath);
        Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM stock_status WHERE sold = TRUE"));
        Assert.Equal(alternate, ScalarText(connection, "SELECT output_path FROM stock_extractions WHERE state = 'completed'"));
    }

    [Fact]
    public void CancelDoesNotDeleteUnrelatedTempFile()
    {
        using var workspace = new ExtractionWorkspace();
        var service = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory),
            beforePublish: _ => throw new IOException("corte"));
        Seed(workspace);
        var destination = Path.Combine(workspace.Directory, "cancel.csv");
        var unrelated = Path.Combine(workspace.Directory, ".cancel.csv.tmp");
        File.WriteAllText(unrelated, "no tocar");
        service.BeginExtraction(Request(destination, [new("OSDE", 1)], null));

        new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory)).CancelPending();

        Assert.Equal("no tocar", File.ReadAllText(unrelated));
    }

    [Fact]
    public void CancelReleasesPendingWithoutSelling()
    {
        using var workspace = new ExtractionWorkspace();
        var service = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory),
            beforePublish: _ => throw new IOException("corte"));
        Seed(workspace);
        service.BeginExtraction(Request(Path.Combine(workspace.Directory, "cancel.csv"), [new("OSDE", 1)], null));

        var cancel = new StockSnapshotService(
            new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)),
            new StockCatalogStore(workspace.CatalogDirectory)).CancelPending();

        Assert.Equal(StockGenerateStatus.Completed, cancel.Status);
        using var connection = Open(workspace.DatabasePath);
        Assert.Equal(0, Scalar(connection, "SELECT COUNT(*) FROM stock_status WHERE extraction_id IS NOT NULL OR sold = TRUE"));
        Assert.Equal(0, Scalar(connection, "SELECT COUNT(*) FROM stock_extractions"));
    }

    [Fact]
    public void ReexportUsesStoredColumnsAfterCatalogAndSergioChange()
    {
        using var workspace = new ExtractionWorkspace();
        var service = Seed(workspace);
        var original = Path.Combine(workspace.Directory, "original.csv");
        var begun = service.BeginExtraction(new StockExtractionRequest(original, ["NOMBRE"], [new("OSDE", 2)], "abc"));
        service.SaveCatalog(new Dictionary<string, string> { ["OSDE"] = "Cambiada" });
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "DELETE FROM sergio_current; INSERT INTO sergio_current VALUES ('999', 'zzz', 'Nuevo'); UPDATE sergio_load_metadata SET row_count = 1, source_file_name = 'nuevo.xlsx'");
        }

        var copy = Path.Combine(workspace.Directory, "copia.csv");
        var reexport = service.ReexportCompleted(begun.ExtractionId!, copy);

        Assert.Equal(2, reexport.RowCount);
        Assert.Equal(File.ReadAllLines(original).Length, File.ReadAllLines(copy).Length);
        Assert.Contains("OSDE", File.ReadAllText(copy));
        Assert.DoesNotContain("Cambiada", File.ReadAllText(copy));
        Assert.DoesNotContain("999", File.ReadAllText(copy));
    }

    private static StockExtractionRequest Request(string path, StockGroupQuantity[] groups, string? reference) =>
        new(path, ["CUIL", "CODIGO", "NOMBRE"], groups, reference);

    private static StockSnapshotService Seed(ExtractionWorkspace workspace)
    {
        using var connection = Open(workspace.DatabasePath);
        Execute(connection, "CREATE TABLE sergio_current(CUIL VARCHAR PRIMARY KEY, CODIGO VARCHAR, NOMBRE VARCHAR)");
        Execute(connection, "INSERT INTO sergio_current VALUES ('020', 'osde', 'OSDE'), ('021', 'osde', 'OSDE'), ('030', '', 'vacio')");
        Execute(connection, "CREATE TABLE sergio_load_metadata(source_file_name VARCHAR, loaded_at_utc TIMESTAMP, row_count BIGINT, selected_primary_key_alias VARCHAR, schema_version VARCHAR)");
        using var metadata = connection.CreateCommand();
        metadata.CommandText = "INSERT INTO sergio_load_metadata VALUES ('origen.xlsx', ?, 3, 'CUIL', NULL)";
        metadata.Parameters.Add(new DuckDBParameter { Value = DateTime.UtcNow });
        metadata.ExecuteNonQuery();
        var service = new StockSnapshotService(new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)), new StockCatalogStore(workspace.CatalogDirectory));
        Assert.Equal(StockGenerateStatus.Completed, service.Generate(new StockGenerateRequest("CUIL", "CODIGO", "NOMBRE", false)).Status);
        return service;
    }

    private static long Scalar(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string ScalarText(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)!;
    }

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

    private sealed class ExtractionWorkspace : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(Directory, "test.duckdb");
        public string CatalogDirectory => Path.Combine(Directory, "catalog");
        public ExtractionWorkspace() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { try { System.IO.Directory.Delete(Directory, true); } catch { } }
    }
}
