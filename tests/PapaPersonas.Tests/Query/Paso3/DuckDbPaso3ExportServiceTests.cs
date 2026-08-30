using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.Core.Query.Paso3;
using PapaPersonas.Infrastructure.Database;
using PapaPersonas.Infrastructure.Query.Paso3;

namespace PapaPersonas.Tests.Query.Paso3;

public sealed class DuckDbPaso3ExportServiceTests
{
    [Fact]
    public async Task ExportCsvAsync_Success_StreamsToTempThenRenamesAtomically()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "20123456789", "LOPEZ", "ANA", "2026-08-10", "2026-08-12 10:00:00");
            InsertPersona(connection, "20987654321", "PEREZ", "BETA", "2026-08-11", "2026-08-12 11:00:00");
        }

        var destination = Path.Combine(ctx.RootDirectory, "query-export.csv");
        var progressEvents = new List<Paso3ExportProgress>();
        var progress = new Progress<Paso3ExportProgress>(value => progressEvents.Add(value));

        var service = new DuckDbPaso3ExportService();
        var result = await service.ExportCsvAsync(
            new Paso3ExportRequest(
                new Paso3PreviewRequest(ctx.DatabasePath, null, [], null, null, ["cuil", "apellido"], 1, 100),
                destination),
            progress,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(Paso3ExportStatus.Completed, result.Status);
        Assert.Equal(destination, result.OutputPath);
        Assert.Equal(2, result.RowsWritten);

        Assert.True(File.Exists(destination));
        Assert.DoesNotContain(Directory.GetFiles(ctx.RootDirectory), file => file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));

        var bytes = await File.ReadAllBytesAsync(destination);
        Assert.True(bytes.Length >= 3);
        Assert.Equal((byte)0xEF, bytes[0]);
        Assert.Equal((byte)0xBB, bytes[1]);
        Assert.Equal((byte)0xBF, bytes[2]);

        var lines = await File.ReadAllLinesAsync(destination);
        Assert.Equal("cuil,apellido", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain(progressEvents, evt => ContainsPii(evt.Message));
    }

    [Fact]
    public async Task ExportCsvAsync_Progress_UsesSingularAtFirstRowAndPluralAtThousandRows()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            for (var i = 0; i < 1000; i++)
            {
                InsertPersona(
                    connection,
                    (20120000000L + i).ToString(CultureInfo.InvariantCulture),
                    "PROGRESS",
                    "ROW",
                    "2026-08-10",
                    "2026-08-12 10:00:00");
            }
        }

        var progressEvents = new List<Paso3ExportProgress>();
        var progress = new SynchronousProgress<Paso3ExportProgress>(progressEvents.Add);
        var result = await new DuckDbPaso3ExportService().ExportCsvAsync(
            new Paso3ExportRequest(
                new Paso3PreviewRequest(ctx.DatabasePath, null, [], null, null, ["cuil"], 1, 2000),
                Path.Combine(ctx.RootDirectory, "query-export-progress.csv")),
            progress,
            CancellationToken.None);

        Assert.Equal(Paso3ExportStatus.Completed, result.Status);
        Assert.Equal(1000, result.RowsWritten);
        Assert.Collection(
            progressEvents,
            first =>
            {
                Assert.Equal(1, first.RowsWritten);
                Assert.Equal("Progreso de exportación: 1 fila escrita.", first.Message);
            },
            thousand =>
            {
                Assert.Equal(1000, thousand.RowsWritten);
                Assert.Equal("Progreso de exportación: 1000 filas escritas.", thousand.Message);
            },
            completed =>
            {
                Assert.Equal(1000, completed.RowsWritten);
                Assert.Equal("Exportación completada.", completed.Message);
            });
    }

    [Fact]
    public async Task ExportCsvAsync_Cancelled_StopsAndDeletesPartialTempFile()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            for (var i = 0; i < 3000; i++)
            {
                InsertPersona(connection,
                    (20120000000L + i).ToString(CultureInfo.InvariantCulture),
                    "CANCEL",
                    "ROW",
                    "2026-08-10",
                    "2026-08-12 10:00:00");
            }
        }

        var destination = Path.Combine(ctx.RootDirectory, "query-export-cancel.csv");
        using var cts = new CancellationTokenSource();
        var progress = new Progress<Paso3ExportProgress>(evt =>
        {
            if (evt.RowsWritten >= 10)
            {
                cts.Cancel();
            }
        });

        var service = new DuckDbPaso3ExportService();
        var result = await service.ExportCsvAsync(
            new Paso3ExportRequest(
                new Paso3PreviewRequest(ctx.DatabasePath, null, [new Paso3Filter("apellido", "eq", "CANCEL")], null, null, ["cuil", "apellido"], 1, 5000),
                destination),
            progress,
            cts.Token);

        Assert.Equal(Paso3ExportStatus.Canceled, result.Status);
        Assert.False(File.Exists(destination));
        Assert.DoesNotContain(Directory.GetFiles(ctx.RootDirectory), file => file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportCsvAsync_RenameFailure_DeletesPartialTempFile()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "20123456789", "FAIL", "ANA", "2026-08-10", "2026-08-12 10:00:00");
        }

        var destinationDirectoryPath = Path.Combine(ctx.RootDirectory, "already-directory");
        Directory.CreateDirectory(destinationDirectoryPath);

        var service = new DuckDbPaso3ExportService();
        var result = await service.ExportCsvAsync(
            new Paso3ExportRequest(
                new Paso3PreviewRequest(ctx.DatabasePath, null, [], null, null, ["cuil"], 1, 100),
                destinationDirectoryPath),
            progress: null,
            CancellationToken.None);

        Assert.Equal(Paso3ExportStatus.Failed, result.Status);
        Assert.DoesNotContain(Directory.GetFiles(ctx.RootDirectory), file => file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportCsvAsync_CaughtFailure_PreservesExactTechnicalDetail()
    {
        using var ctx = CreateDbContext();
        var databasePath = Path.Combine(ctx.RootDirectory, "invalid.duckdb");
        File.WriteAllText(databasePath, "not a DuckDB database");
        var expectedTechnicalMessage = ReadDuckDbOpenFailure(databasePath);

        var result = await new DuckDbPaso3ExportService().ExportCsvAsync(
            new Paso3ExportRequest(
                new Paso3PreviewRequest(databasePath, null, [], null, null, ["cuil"], 1, 100),
                Path.Combine(ctx.RootDirectory, "query-export.csv")),
            progress: null,
            CancellationToken.None);

        Assert.Equal(Paso3ExportStatus.Failed, result.Status);
        Assert.Equal(
            $"No se pudo completar la exportación. Verificá la ruta de destino y volvé a intentar. Detalle técnico: {expectedTechnicalMessage}",
            result.FailureMessage);
    }

    private static bool ContainsPii(string message)
    {
        return message.Contains("201", StringComparison.Ordinal)
               || message.Contains("LOPEZ", StringComparison.OrdinalIgnoreCase)
               || message.Contains("PEREZ", StringComparison.OrdinalIgnoreCase);
    }

    private static TestDbContext CreateDbContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "PapaPersonas.duckdb");

        var bootstrap = new DuckDbBootstrapper().Initialize(dbPath);
        Assert.True(bootstrap.IsSuccess, bootstrap.Message);

        return new TestDbContext(root, dbPath);
    }

    private static void InsertPersona(
        DuckDBConnection connection,
        string cuil,
        string apellido,
        string nombre,
        string importDate,
        string updatedAt)
    {
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO personas (cuil, apellido, nombre, fecha_importacion, fecha_actualizacion)
            VALUES ($cuil, $apellido, $nombre, CAST($importDate AS DATE), CAST($updatedAt AS TIMESTAMP));
            """,
            new DuckDBParameter("cuil", cuil),
            new DuckDBParameter("apellido", apellido),
            new DuckDBParameter("nombre", nombre),
            new DuckDBParameter("importDate", importDate),
            new DuckDBParameter("updatedAt", updatedAt));
    }

    private static DuckDBConnection OpenConnection(string dbPath)
    {
        var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = dbPath }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(DuckDBConnection connection, string sql, params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        command.ExecuteNonQuery();
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

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
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
            if (!Directory.Exists(RootDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup in tests.
            }
        }
    }
}
