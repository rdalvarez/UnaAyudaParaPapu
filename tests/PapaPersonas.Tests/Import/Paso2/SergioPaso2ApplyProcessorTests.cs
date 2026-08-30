using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.Core.Import.Paso2;
using PapaPersonas.Infrastructure.Database;
using PapaPersonas.Infrastructure.Import.Paso2;

namespace PapaPersonas.Tests.Import.Paso2;

public sealed class SergioPaso2ApplyProcessorTests
{
    [Fact]
    public void Apply_ReadyImport_InsertsNewPersonas()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "NUEVO");
        }

        var processor = new SergioPaso2ApplyProcessor();
        var result = processor.Apply(new SergioApplyRequest(ctx.DatabasePath, importId));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Summary.InsertedRows);
        Assert.Equal(0, result.Summary.UpdatedRows);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(1, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
    }

    [Fact]
    public void Apply_ReadyImport_Insert_PersistsSourceMetadata()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 42, "20123456789", "valid", null, apellido: "NUEVO");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));

        Assert.True(result.IsSuccess);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(
            1,
            Scalar<int>(
                verify,
                "SELECT COUNT(*) FROM personas WHERE cuil='20123456789' AND source_import_id = $importId AND source_row_number = 42;",
                new DuckDBParameter("importId", importId)));
    }

    [Fact]
    public void Apply_ReadyImport_UpdatesExistingPersonas()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            ExecuteNonQuery(connection, "INSERT INTO personas (cuil, apellido, fecha_actualizacion) VALUES ('20123456789', 'OLD', TIMESTAMP '2024-01-01 00:00:00');");
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "NEW");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Summary.InsertedRows);
        Assert.Equal(1, result.Summary.UpdatedRows);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal("NEW", Scalar<string>(verify, "SELECT apellido FROM personas WHERE cuil='20123456789';"));
    }

    [Fact]
    public void Apply_ReadyImport_Update_ReplacesSourceMetadataFromNewRun()
    {
        using var ctx = CreateDbContext();
        var firstImportId = Guid.NewGuid();
        var secondImportId = Guid.NewGuid();

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            ExecuteNonQuery(
                connection,
                "INSERT INTO personas (cuil, apellido, source_import_id, source_row_number, fecha_actualizacion) VALUES ('20123456789', 'OLD', $firstImportId, 7, TIMESTAMP '2024-01-01 00:00:00');",
                new DuckDBParameter("firstImportId", firstImportId));

            InsertImportRun(connection, secondImportId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, secondImportId, 99, "20123456789", "valid", null, apellido: "NEW");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, secondImportId));

        Assert.True(result.IsSuccess);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(
            1,
            Scalar<int>(
                verify,
                "SELECT COUNT(*) FROM personas WHERE cuil='20123456789' AND source_import_id = $secondImportId AND source_row_number = 99;",
                new DuckDBParameter("secondImportId", secondImportId)));
    }

    [Fact]
    public void Apply_AbsentField_PreservesExistingValue()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            ExecuteNonQuery(connection, "INSERT INTO personas (cuil, apellido, fecha_actualizacion) VALUES ('20123456789', 'KEEP', TIMESTAMP '2024-01-01 00:00:00');");
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"nombre\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: null, nombre: "N");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.True(result.IsSuccess);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal("KEEP", Scalar<string>(verify, "SELECT apellido FROM personas WHERE cuil='20123456789';"));
    }

    [Fact]
    public void Apply_PresentNull_ClearsExistingValue()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            ExecuteNonQuery(connection, "INSERT INTO personas (cuil, apellido, fecha_actualizacion) VALUES ('20123456789', 'CLEARME', TIMESTAMP '2024-01-01 00:00:00');");
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: null);
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.True(result.IsSuccess);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(1, Scalar<int>(verify, "SELECT COUNT(*) FROM personas WHERE cuil='20123456789' AND apellido IS NULL;"));
    }

    [Fact]
    public void Apply_FechaActualizacion_IsUpdatedForAppliedRows()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            ExecuteNonQuery(connection, "INSERT INTO personas (cuil, apellido, fecha_actualizacion) VALUES ('20123456789', 'OLD', TIMESTAMP '2020-01-01 00:00:00');");
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "NEW");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.True(result.IsSuccess);

        using var verify = OpenConnection(ctx.DatabasePath);
        var updatedAt = Scalar<DateTime>(verify, "SELECT fecha_actualizacion FROM personas WHERE cuil='20123456789';");
        Assert.True(updatedAt > new DateTime(2020, 1, 1));
    }

    [Fact]
    public void Apply_WritesFechaImportacionFromRun_OnUpdatedAndInsertedRows()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            ExecuteNonQuery(connection, "INSERT INTO personas (cuil, apellido, fecha_actualizacion) VALUES ('20123456789', 'OLD', TIMESTAMP '2020-01-01 00:00:00');");
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]", "2026-08-09");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "UPDATED");
            InsertStaging(connection, importId, 3, "20987654321", "valid", null, apellido: "INSERTED");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.True(result.IsSuccess);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(
            2,
            Scalar<int>(verify, "SELECT COUNT(*) FROM personas WHERE fecha_importacion = DATE '2026-08-09';"));
    }

    [Fact]
    public void Apply_RejectedRows_AreNotApplied()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "OK");
            InsertStaging(connection, importId, 3, "20987654321", "rejected", "MissingCuil", apellido: "NO");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Summary.AppliedRows);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(1, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
    }

    [Theory]
    [InlineData("pending", "sergio_return")]
    [InlineData("ready_for_confirmation", "hernan_raw")]
    public void Apply_InvalidRunStateOrStage_FailsWithoutMutating(string status, string stage)
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, status, stage, "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "X");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.False(result.IsSuccess);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(0, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
    }

    [Fact]
    public void Apply_DoubleApply_IsRefused()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "X");
        }

        var processor = new SergioPaso2ApplyProcessor();
        var first = processor.Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        var second = processor.Apply(new SergioApplyRequest(ctx.DatabasePath, importId));

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task Apply_ConcurrentDoubleApply_OnlyOneSucceeds()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "X");
        }

        var processor = new SergioPaso2ApplyProcessor();
        var request = new SergioApplyRequest(ctx.DatabasePath, importId);

        var t1 = Task.Run(() => processor.Apply(request));
        var t2 = Task.Run(() => processor.Apply(request));
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.Equal(1, results.Count(r => !r.IsSuccess));

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(1, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
        Assert.Equal("completed", Scalar<string>(verify, "SELECT status FROM import_runs WHERE import_id = $id;", new DuckDBParameter("id", importId)));
    }

    [Fact]
    public void Apply_UnsupportedSourceColumnsOnly_FailsSafely()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"not_supported\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "X");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.False(result.IsSuccess);
        Assert.Equal(SergioApplyStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public void Apply_ImportRunWithNullFechaImportacion_IsBlocked()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]", importDate: null);
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "X");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));

        Assert.False(result.IsSuccess);
        Assert.Equal(SergioApplyStatus.ValidationFailed, result.Status);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(0, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
    }

    [Fact]
    public void Apply_MixedSupportedAndUnsupportedSourceColumns_FailsWithoutMutation()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\",\"unknown_field\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "X");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.False(result.IsSuccess);
        Assert.Equal(SergioApplyStatus.ValidationFailed, result.Status);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(0, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
        Assert.Equal("ready_for_confirmation", Scalar<string>(verify, "SELECT status FROM import_runs WHERE import_id = $id;", new DuckDBParameter("id", importId)));
    }

    [Theory]
    [InlineData("{bad json")]
    [InlineData("{\"foo\":\"bar\"}")]
    [InlineData("\"not-array\"")]
    [InlineData("[1,2,3]")]
    public void Apply_MalformedOrNonArraySourceColumnsJson_FailsWithoutMutation(string sourceColumnsJson)
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", sourceColumnsJson);
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "X");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.False(result.IsSuccess);
        Assert.Equal(SergioApplyStatus.ValidationFailed, result.Status);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(0, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
    }

    [Fact]
    public void Apply_RejectedCsv_IsGeneratedWhenRequested()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        var outputPath = Path.Combine(ctx.RootDirectory, "rejected.csv");

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "OK");
            InsertStaging(connection, importId, 3, "20987654321", "rejected", "InvalidSmallIntValue", apellido: "NO");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId, RejectedCsvOutputPath: outputPath));
        Assert.True(result.IsSuccess);
        Assert.Equal(outputPath, result.RejectedCsvPath);
        Assert.True(File.Exists(outputPath));

        var text = File.ReadAllText(outputPath);
        Assert.Contains("source_row_number,reason_code,normalized_cuil", text);
        Assert.Contains("InvalidSmallIntValue", text);
    }

    [Fact]
    public void Apply_ZeroValidRows_CompletesWithZeroAppliedAndCanExportRejectedCsv()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        var outputPath = Path.Combine(ctx.RootDirectory, "zero-valid-rejected.csv");

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "rejected", "DuplicateCuilInBatch", apellido: "NO");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId, RejectedCsvOutputPath: outputPath));
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Summary.AppliedRows);
        Assert.Equal(0, result.Summary.InsertedRows);
        Assert.Equal(0, result.Summary.UpdatedRows);
        Assert.Equal(1, result.Summary.RejectedRowsExported);
        Assert.Equal(outputPath, result.RejectedCsvPath);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(0, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
        Assert.Equal("completed", Scalar<string>(verify, "SELECT status FROM import_runs WHERE import_id = $id;", new DuckDBParameter("id", importId)));
    }

    [Fact]
    public void Apply_CountMismatch_RollsBack()
    {
        using var ctx = CreateDbContext();
        var importId = Guid.NewGuid();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertImportRun(connection, importId, "ready_for_confirmation", "sergio_return", "[\"cuil\",\"apellido\"]");
            InsertStaging(connection, importId, 2, "20123456789", "valid", null, apellido: "A");
            InsertStaging(connection, importId, 3, "20123456789", "valid", null, apellido: "B");
        }

        var result = new SergioPaso2ApplyProcessor().Apply(new SergioApplyRequest(ctx.DatabasePath, importId));
        Assert.False(result.IsSuccess);
        Assert.StartsWith(
            "La aplicación de Paso 2 falló. Verificá el estado y la consistencia de los datos, y volvé a intentar. Detalle técnico: ",
            result.FailureMessage,
            StringComparison.Ordinal);
        Assert.Contains("Constraint Error", result.FailureMessage, StringComparison.Ordinal);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(0, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
    }

    private static void InsertImportRun(DuckDBConnection connection, Guid importId, string status, string stageType, string sourceColumnsJson, string? importDate = "2026-08-10")
    {
        var importDateExpression = string.IsNullOrWhiteSpace(importDate) ? "NULL" : "CAST($importDate AS DATE)";
        ExecuteNonQuery(
            connection,
            $"""
            INSERT INTO import_runs (
                import_id,
                stage_type,
                source_file_name,
                source_file_path,
                status,
                source_columns_present_json,
                fecha_importacion)
            VALUES ($id, $stage, 'synthetic.xlsx', 'C:/synthetic.xlsx', $status, $json, {importDateExpression});
            """,
            new DuckDBParameter("id", importId),
            new DuckDBParameter("stage", stageType),
            new DuckDBParameter("status", status),
            new DuckDBParameter("json", sourceColumnsJson),
            new DuckDBParameter("importDate", (object?)importDate ?? DBNull.Value));
    }

    private static void InsertStaging(
        DuckDBConnection connection,
        Guid importId,
        long sourceRow,
        string cuil,
        string outcome,
        string? error,
        string? apellido = null,
        string? nombre = null)
    {
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO personas_staging (
                import_id,
                source_row_number,
                cuil,
                apellido,
                nombre,
                validation_outcome,
                validation_error)
            VALUES ($id, $row, $cuil, $apellido, $nombre, $outcome, $error);
            """,
            new DuckDBParameter("id", importId),
            new DuckDBParameter("row", sourceRow),
            new DuckDBParameter("cuil", cuil),
            new DuckDBParameter("apellido", (object?)apellido ?? DBNull.Value),
            new DuckDBParameter("nombre", (object?)nombre ?? DBNull.Value),
            new DuckDBParameter("outcome", outcome),
            new DuckDBParameter("error", (object?)error ?? DBNull.Value));
    }

    private static TestDbContext CreateDbContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "PapaPersonas.duckdb");

        var bootstrapper = new DuckDbBootstrapper();
        var bootstrap = bootstrapper.Initialize(dbPath);
        Assert.True(bootstrap.IsSuccess, bootstrap.Message);

        return new TestDbContext(root, dbPath);
    }

    private static DuckDBConnection OpenConnection(string dbPath)
    {
        var builder = new DuckDBConnectionStringBuilder { DataSource = dbPath };
        var connection = new DuckDBConnection(builder.ConnectionString);
        connection.Open();
        return connection;
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
