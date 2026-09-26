using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.Core.Stock.Paso4;
using PapaPersonas.Infrastructure.Database;
using PapaPersonas.Infrastructure.Query.Paso3;
using PapaPersonas.Infrastructure.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class DuckDbPaso4StockServiceTests
{
    [Fact]
    public void GetOverview_ReturnsLastFiveDistinctDates_NewestFirst_NoNulls()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        SeedCompletedRun(connection, Guid.NewGuid(), "2026-08-01", "2026-08-01 01:00:00", "2026-08-01 02:00:00");
        InsertPersona(connection, "20000000001", "2026-08-01", 1);
        InsertPersona(connection, "20000000002", "2026-08-02", 2);
        InsertPersona(connection, "20000000003", "2026-08-02", 3);
        InsertPersona(connection, "20000000004", "2026-08-03", 4);
        InsertPersona(connection, "20000000005", "2026-08-04", 5);
        InsertPersona(connection, "20000000006", "2026-08-05", 6);
        InsertPersona(connection, "20000000007", "2026-08-06", 7);
        InsertPersonaWithoutImportDate(connection, "20000000008");

        var service = CreateStockService(ctx.RootDirectory);
        var overview = service.GetOverview(ctx.DatabasePath);

        Assert.Equal(5, overview.AvailableDates.Count);
        Assert.Equal(DateOnly.Parse("2026-08-06", CultureInfo.InvariantCulture), overview.AvailableDates[0].FechaImportacion);
        Assert.Equal(DateOnly.Parse("2026-08-05", CultureInfo.InvariantCulture), overview.AvailableDates[1].FechaImportacion);
        Assert.Equal(DateOnly.Parse("2026-08-04", CultureInfo.InvariantCulture), overview.AvailableDates[2].FechaImportacion);
        Assert.Equal(DateOnly.Parse("2026-08-03", CultureInfo.InvariantCulture), overview.AvailableDates[3].FechaImportacion);
        Assert.Equal(DateOnly.Parse("2026-08-02", CultureInfo.InvariantCulture), overview.AvailableDates[4].FechaImportacion);
    }

    [Fact]
    public void GenerateOrRegenerateStock_SelectedDateSnapshotsRows_AndKeepsPersonasUntouched()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        var importId = Guid.NewGuid();
        SeedCompletedRun(connection, importId, "2026-08-10", "2026-08-10 08:00:00", "2026-08-10 08:10:00");
        InsertPersona(connection, "20123456789", "2026-08-10", 10, " os1 ", "Plan A");
        InsertPersona(connection, "20987654321", "2026-08-10", 11, "OS2", "Plan B");
        InsertPersona(connection, "20000000001", "2026-08-11", 12, "OS3", "Plan C");

        var beforePersonas = Scalar<int>(connection, "SELECT COUNT(*) FROM personas;");

        var service = CreateStockService(ctx.RootDirectory);
        var result = service.GenerateOrRegenerateStock(new Paso4GenerateStockRequest(ctx.DatabasePath, DateOnly.Parse("2026-08-10", CultureInfo.InvariantCulture)));

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(2, result.MembersSnapshotted);

        Assert.Equal(beforePersonas, Scalar<int>(connection, "SELECT COUNT(*) FROM personas;"));
        Assert.Equal(2, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE stock_id = $id;", new DuckDBParameter("id", result.StockId!.Value)));
        Assert.Equal(1, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE stock_id = $id AND cuil='20123456789' AND codigo_obra_social=' os1 ' AND obra_social='Plan A';", new DuckDBParameter("id", result.StockId!.Value)));
    }

    [Fact]
    public void GenerateOrRegenerateStock_PendingTokenBlocksRegeneration()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        InsertPersona(connection, "20123456789", "2026-08-10", 10);
        SeedCurrentStockWithPendingToken(connection, DateOnly.Parse("2026-08-10", CultureInfo.InvariantCulture));

        var service = CreateStockService(ctx.RootDirectory);
        var result = service.GenerateOrRegenerateStock(new Paso4GenerateStockRequest(ctx.DatabasePath, DateOnly.Parse("2026-08-10", CultureInfo.InvariantCulture)));

        Assert.False(result.IsSuccess);
        Assert.Equal(Paso4GenerateStockStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public void GenerateOrRegenerateStock_RegenerateReplacesPreviousMembersIncludingSoldRows()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        InsertPersona(connection, "20123456789", "2026-08-10", 10);
        InsertPersona(connection, "20987654321", "2026-08-10", 11);
        var stockId = Guid.NewGuid();
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ($id, DATE '2026-08-09', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, source_order, vendido, fecha_venta)
            VALUES ($id, '20000000001', 1, TRUE, CURRENT_TIMESTAMP);
            """,
            new DuckDBParameter("id", stockId));

        var service = CreateStockService(ctx.RootDirectory);
        var result = service.GenerateOrRegenerateStock(new Paso4GenerateStockRequest(ctx.DatabasePath, DateOnly.Parse("2026-08-10", CultureInfo.InvariantCulture)));

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(1, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_headers;"));
        Assert.Equal(2, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members;"));
        Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE cuil='20000000001';"));
    }

    [Fact]
    public void GetOverview_StalenessDetectsSameDateNewerImport_AndOlderDate()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        var oldImportId = Guid.NewGuid();
        var latestImportId = Guid.NewGuid();
        SeedCompletedRun(connection, oldImportId, "2026-08-10", "2026-08-10 08:00:00", "2026-08-10 08:10:00");
        SeedCompletedRun(connection, latestImportId, "2026-08-10", "2026-08-10 09:00:00", "2026-08-10 09:10:00");
        InsertPersona(connection, "20123456789", "2026-08-10", 10);

        var service = CreateStockService(ctx.RootDirectory);
        var generated = service.GenerateOrRegenerateStock(new Paso4GenerateStockRequest(ctx.DatabasePath, DateOnly.Parse("2026-08-10", CultureInfo.InvariantCulture)));
        Assert.True(generated.IsSuccess);

        ExecuteNonQuery(
            connection,
            "UPDATE stock_headers SET source_import_id = $oldImport WHERE stock_id = $stockId;",
            new DuckDBParameter("oldImport", oldImportId),
            new DuckDBParameter("stockId", generated.StockId!.Value));

        var staleBySameDateImport = service.GetOverview(ctx.DatabasePath);
        Assert.True(staleBySameDateImport.CurrentStock!.IsStale);

        ExecuteNonQuery(connection, "UPDATE stock_headers SET source_fecha_importacion = DATE '2026-08-09' WHERE stock_id = $stockId;", new DuckDBParameter("stockId", generated.StockId!.Value));
        var staleByOlderDate = service.GetOverview(ctx.DatabasePath);
        Assert.True(staleByOlderDate.CurrentStock!.IsStale);

        ExecuteNonQuery(
            connection,
            "UPDATE stock_headers SET source_fecha_importacion = DATE '2026-08-10', source_import_id = $latestImport WHERE stock_id = $stockId;",
            new DuckDBParameter("latestImport", latestImportId),
            new DuckDBParameter("stockId", generated.StockId!.Value));
        var current = service.GetOverview(ctx.DatabasePath);
        Assert.False(current.CurrentStock!.IsStale);
    }

    [Fact]
    public void GenerateOrRegenerateStock_SourceOrderUsesSourceRow_AndCuilFallbackForLegacy()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        InsertPersona(connection, "20123456789", "2026-08-10", 50);
        InsertPersona(connection, "20987654321", "2026-08-10", null);
        InsertPersona(connection, "20000000001", "2026-08-10", null);

        var service = CreateStockService(ctx.RootDirectory);
        var result = service.GenerateOrRegenerateStock(new Paso4GenerateStockRequest(ctx.DatabasePath, DateOnly.Parse("2026-08-10", CultureInfo.InvariantCulture)));
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.LegacyFallbackAssignedCount);

        var minFallback = Scalar<long>(
            connection,
            "SELECT MIN(source_order) FROM stock_members WHERE stock_id = $id AND cuil IN ('20000000001','20987654321');",
            new DuckDBParameter("id", result.StockId!.Value));

        Assert.True(minFallback > 50);

        var rows = QueryRows(
            connection,
            "SELECT cuil, source_order FROM stock_members WHERE stock_id = $id ORDER BY source_order, cuil;",
            new DuckDBParameter("id", result.StockId!.Value));

        Assert.Equal("20123456789", rows[0].cuil);
        Assert.Equal("20000000001", rows[1].cuil);
        Assert.Equal("20987654321", rows[2].cuil);
    }

    [Fact]
    public void GenerateStockCaughtFailure_PreservesExactTechnicalDetail()
    {
        using var ctx = CreateDbContext();
        var databasePath = Path.Combine(ctx.RootDirectory, "invalid.duckdb");
        File.WriteAllText(databasePath, "not a DuckDB database");
        var expectedTechnicalMessage = ReadDuckDbOpenFailure(databasePath);

        var result = CreateStockService(ctx.RootDirectory).GenerateOrRegenerateStock(
            new Paso4GenerateStockRequest(databasePath, DateOnly.Parse("2026-08-10", CultureInfo.InvariantCulture)));

        Assert.Equal(
            $"No se pudo generar el stock. Verificá la base y volvé a intentar. Detalle técnico: {expectedTechnicalMessage}",
            result.FailureMessage);
    }

    private static DuckDbPaso4StockService CreateStockService(string rootDirectory)
        => new(new Paso4ObraSocialCatalogStore(rootDirectory));

    private static List<(string cuil, long order)> QueryRows(DuckDBConnection connection, string sql, params DuckDBParameter[] parameters)
    {
        var rows = new List<(string cuil, long order)>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
    }

    private static void SeedCurrentStockWithPendingToken(DuckDBConnection connection, DateOnly date)
    {
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (
                stock_id,
                source_fecha_importacion,
                pending_extraction_token,
                pending_started_utc,
                pending_output_path,
                pending_expected_rows,
                pending_selected_columns_json)
            VALUES ($id, $date, $token, CURRENT_TIMESTAMP, 'C:/tmp/out.csv', 1, '["CUIL","APELLIDO","CODIGOOS","OBRASOCIAL"]');
            """,
            new DuckDBParameter("id", Guid.NewGuid()),
            new DuckDBParameter("date", date),
            new DuckDBParameter("token", Guid.NewGuid()));
    }

    private static void SeedCompletedRun(DuckDBConnection connection, Guid importId, string importDate, string startedUtc, string completedUtc)
    {
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO import_runs (
                import_id,
                stage_type,
                source_file_name,
                source_file_path,
                status,
                started_utc,
                completed_utc,
                fecha_importacion)
            VALUES ($id, 'sergio_return', 'synthetic.xlsx', 'C:/synthetic.xlsx', 'completed', CAST($started AS TIMESTAMP), CAST($completed AS TIMESTAMP), CAST($importDate AS DATE));
            """,
            new DuckDBParameter("id", importId),
            new DuckDBParameter("started", startedUtc),
            new DuckDBParameter("completed", completedUtc),
            new DuckDBParameter("importDate", importDate));
    }

    private static void InsertPersona(DuckDBConnection connection, string cuil, string importDate, long? sourceRow, string? codigoOs = null, string? obraSocial = null)
    {
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO personas (
                cuil,
                codigo_obra_social,
                obra_social,
                fecha_importacion,
                source_row_number,
                fecha_actualizacion)
            VALUES ($cuil, $codigoOs, $obraSocial, CAST($importDate AS DATE), $sourceRow, CURRENT_TIMESTAMP);
            """,
            new DuckDBParameter("cuil", cuil),
            new DuckDBParameter("codigoOs", (object?)codigoOs ?? DBNull.Value),
            new DuckDBParameter("obraSocial", (object?)obraSocial ?? DBNull.Value),
            new DuckDBParameter("importDate", importDate),
            new DuckDBParameter("sourceRow", (object?)sourceRow ?? DBNull.Value));
    }

    private static void InsertPersonaWithoutImportDate(DuckDBConnection connection, string cuil)
    {
        ExecuteNonQuery(connection, "INSERT INTO personas (cuil, fecha_actualizacion) VALUES ($cuil, CURRENT_TIMESTAMP);", new DuckDBParameter("cuil", cuil));
    }

    private static TestDbContext CreateDbContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "PapaPersonas.duckdb");

        var result = new DuckDbBootstrapper().Initialize(dbPath);
        Assert.True(result.IsSuccess, result.Message);

        return new TestDbContext(root, dbPath);
    }

    private static DuckDBConnection OpenConnection(string databasePath)
    {
        var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = databasePath }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static T Scalar<T>(DuckDBConnection connection, string sql, params DuckDBParameter[] parameters)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        var value = cmd.ExecuteScalar();
        return (T)Convert.ChangeType(value!, typeof(T), CultureInfo.InvariantCulture);
    }

    private static void ExecuteNonQuery(DuckDBConnection connection, string sql, params DuckDBParameter[] parameters)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        cmd.ExecuteNonQuery();
    }

    private static string ReadDuckDbOpenFailure(string databasePath)
    {
        var exception = Record.Exception(() =>
        {
            using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);
        });

        Assert.NotNull(exception);
        return exception!.Message;
    }

    private sealed class TestDbContext : IDisposable
    {
        public TestDbContext(string rootDirectory, string databasePath)
        {
            RootDirectory = rootDirectory;
            DatabasePath = databasePath;
        }

        public string RootDirectory { get; }
        public string DatabasePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }
}
