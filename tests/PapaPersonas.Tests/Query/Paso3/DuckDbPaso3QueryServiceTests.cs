using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.Core.Query.Paso3;
using PapaPersonas.Infrastructure.Database;
using PapaPersonas.Infrastructure.Query.Paso3;

namespace PapaPersonas.Tests.Query.Paso3;

public sealed class DuckDbPaso3QueryServiceTests
{
    [Fact]
    public void QueryPreview_ExactCuil_UsesExactEqualityWithNormalization()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "20123456789", "LOPEZ", "ANA", "CABA", "2026-08-10", "2026-08-12 10:00:00");
            InsertPersona(connection, "20123456780", "LOPEZ", "BETA", "CABA", "2026-08-10", "2026-08-12 10:00:01");
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            "20-12345678-9",
            [],
            null,
            null,
            ["cuil", "apellido"],
            PageNumber: 1,
            PageSize: 50));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("20123456789", result.Rows.Single().Values["cuil"]);
    }

    [Fact]
    public void QueryPreview_TextFilters_AreAppliedWithAndSemantics()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "20123456789", "LOPEZ", "ANA", "CABA", "2026-08-10", "2026-08-12 10:00:00");
            InsertPersona(connection, "20987654321", "LOPEZ", "MARIO", "CORDOBA", "2026-08-10", "2026-08-12 10:00:01");
            InsertPersona(connection, "27111222333", "PEREZ", "ANA", "CABA", "2026-08-10", "2026-08-12 10:00:02");
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [
                new Paso3Filter("apellido", "eq", "LOPEZ"),
                new Paso3Filter("nombre", "contains", "ana")
            ],
            null,
            null,
            ["cuil", "apellido", "nombre"],
            1,
            50));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("20123456789", result.Rows.Single().Values["cuil"]);
    }

    [Fact]
    public void QueryPreview_DatePredicates_AndLatestImportKpi_UseActiveFilters()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "20123456789", "ALFA", "UNO", "CABA", "2026-08-09", "2026-08-10 09:00:00");
            InsertPersona(connection, "20987654321", "ALFA", "DOS", "CABA", "2026-08-10", "2026-08-10 10:00:00");
            InsertPersona(connection, "27111222333", "ALFA", "TRES", "CABA", "2026-08-11", "2026-08-10 11:00:00");
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [new Paso3Filter("fecha_actualizacion", "gte", "2026-08-10")],
            ImportDateFrom: new DateOnly(2026, 8, 10),
            ImportDateTo: new DateOnly(2026, 8, 11),
            ["cuil", "fecha_importacion"],
            1,
            50));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new DateOnly(2026, 8, 11), result.LatestImportDate);
        Assert.All(result.Rows, row =>
        {
            var importDate = Assert.IsType<DateOnly>(row.Values["fecha_importacion"]);
            Assert.InRange(importDate, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 11));
        });
    }

    [Fact]
    public void QueryPreview_DefaultOrdering_IsDeterministicByImportDateDescThenCuilAsc()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "27111222333", "A", "A", "CABA", "2026-08-10", "2026-08-10 10:00:00");
            InsertPersona(connection, "20123456789", "B", "B", "CABA", "2026-08-12", "2026-08-10 10:00:00");
            InsertPersona(connection, "20987654321", "C", "C", "CABA", "2026-08-12", "2026-08-10 10:00:00");
            InsertPersona(connection, "30111222334", "D", "D", "CABA", null, "2026-08-10 10:00:00");
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [],
            null,
            null,
            ["cuil", "fecha_importacion"],
            1,
            50));

        var orderedCuils = result.Rows
            .Select(r => Assert.IsType<string>(r.Values["cuil"]))
            .ToArray();

        Assert.Equal(
            ["20123456789", "20987654321", "27111222333", "30111222334"],
            orderedCuils);
    }

    [Fact]
    public void QueryPreview_Pagination_UsesFilteredCountNotPageCount()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            for (var i = 0; i < 5; i++)
            {
                InsertPersona(
                    connection,
                    (20123456000L + i).ToString(CultureInfo.InvariantCulture),
                    "PAG",
                    "ROW",
                    "CABA",
                    "2026-08-10",
                    "2026-08-10 10:00:00");
            }
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [new Paso3Filter("apellido", "eq", "PAG")],
            null,
            null,
            ["cuil"],
            PageNumber: 2,
            PageSize: 2));

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public void QueryPreview_SelectedColumns_ControlResultShape()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "20123456789", "LOPEZ", "ANA", "CABA", "2026-08-10", "2026-08-12 10:00:00");
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [],
            null,
            null,
            ["cuil", "provincia"],
            1,
            50));

        var row = result.Rows.Single();
        Assert.Equal(["cuil", "provincia"], row.Values.Keys.ToArray());
        Assert.Equal("CABA", row.Values["provincia"]);
    }

    [Fact]
    public void QueryPreview_FilterValues_AreParameterized_NotConcatenated()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "20123456789", "SAFE", "ANA", "CABA", "2026-08-10", "2026-08-12 10:00:00");
            InsertPersona(connection, "20987654321", "DATA", "BETA", "CABA", "2026-08-10", "2026-08-12 10:00:01");
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [new Paso3Filter("apellido", "eq", "SAFE' OR 1=1 --")],
            null,
            null,
            ["cuil", "apellido"],
            1,
            50));

        Assert.Equal(0, result.TotalCount);

        using var verify = OpenConnection(ctx.DatabasePath);
        Assert.Equal(2, Scalar<int>(verify, "SELECT COUNT(*) FROM personas;"));
    }

    [Fact]
    public void QueryPreview_PageSizeLessOrEqualZero_UsesDefaultPageSize()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            for (var i = 0; i < 130; i++)
            {
                InsertPersona(
                    connection,
                    (22000000000L + i).ToString(CultureInfo.InvariantCulture),
                    "DEFAULT",
                    "ROW",
                    "CABA",
                    "2026-08-10",
                    "2026-08-12 10:00:00");
            }
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [new Paso3Filter("apellido", "eq", "DEFAULT")],
            null,
            null,
            ["cuil"],
            PageNumber: 1,
            PageSize: 0));

        Assert.Equal(130, result.TotalCount);
        Assert.Equal(Paso3PreviewDefaults.DefaultPageSize, result.Rows.Count);
    }

    [Fact]
    public void QueryPreview_PageSizeAboveMaximum_IsCapped()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            for (var i = 0; i < 700; i++)
            {
                InsertPersona(
                    connection,
                    (23000000000L + i).ToString(CultureInfo.InvariantCulture),
                    "CAP",
                    "ROW",
                    "CABA",
                    "2026-08-10",
                    "2026-08-12 10:00:00");
            }
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [new Paso3Filter("apellido", "eq", "CAP")],
            null,
            null,
            ["cuil"],
            PageNumber: 1,
            PageSize: 50_000));

        Assert.Equal(700, result.TotalCount);
        Assert.Equal(Paso3PreviewDefaults.MaxPageSize, result.Rows.Count);
    }

    [Fact]
    public void QueryPreview_InvalidFilterColumn_IsRejected()
    {
        using var ctx = CreateDbContext();
        var service = new DuckDbPaso3QueryService();

        Assert.Throws<ArgumentException>(() =>
            service.QueryPreview(new Paso3PreviewRequest(
                ctx.DatabasePath,
                null,
                [new Paso3Filter("not_a_column", "eq", "x")],
                null,
                null,
                ["cuil"],
                1,
                10)));
    }

    [Fact]
    public void QueryPreview_InvalidOperatorForTextColumn_IsRejected()
    {
        using var ctx = CreateDbContext();
        var service = new DuckDbPaso3QueryService();

        Assert.Throws<ArgumentException>(() =>
            service.QueryPreview(new Paso3PreviewRequest(
                ctx.DatabasePath,
                null,
                [new Paso3Filter("apellido", "between", "A,B")],
                null,
                null,
                ["cuil"],
                1,
                10)));
    }

    [Fact]
    public void QueryPreview_DateBetween_UsesClosedRange()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "24000000001", "BETWEEN", "A", "CABA", "2026-08-09", "2026-08-12 10:00:00");
            InsertPersona(connection, "24000000002", "BETWEEN", "B", "CABA", "2026-08-10", "2026-08-12 10:00:00");
            InsertPersona(connection, "24000000003", "BETWEEN", "C", "CABA", "2026-08-11", "2026-08-12 10:00:00");
            InsertPersona(connection, "24000000004", "BETWEEN", "D", "CABA", "2026-08-12", "2026-08-12 10:00:00");
        }

        var service = new DuckDbPaso3QueryService();
        var result = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [new Paso3Filter("fecha_importacion", "between", "2026-08-10,2026-08-11")],
            null,
            null,
            ["cuil", "fecha_importacion"],
            1,
            50));

        Assert.Equal(2, result.TotalCount);
        var cuils = result.Rows.Select(r => Assert.IsType<string>(r.Values["cuil"])).ToArray();
        Assert.Equal(["24000000003", "24000000002"], cuils);
    }

    [Theory]
    [InlineData("2026-08-10")]
    [InlineData("2026-08-10,")]
    [InlineData(",2026-08-11")]
    [InlineData("not-date,2026-08-11")]
    [InlineData("2026-08-12,2026-08-11")]
    public void QueryPreview_InvalidDateBetweenValues_AreRejected(string betweenValue)
    {
        using var ctx = CreateDbContext();
        var service = new DuckDbPaso3QueryService();

        Assert.Throws<ArgumentException>(() =>
            service.QueryPreview(new Paso3PreviewRequest(
                ctx.DatabasePath,
                null,
                [new Paso3Filter("fecha_importacion", "between", betweenValue)],
                null,
                null,
                ["cuil"],
                1,
                10)));
    }

    [Fact]
    public void QueryPreview_NumericEdadFilters_UseImportedValuesAndClosedBetween()
    {
        using var ctx = CreateDbContext();
        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            InsertPersona(connection, "25000000039", "EDAD", "A", "CABA", "2026-08-10", "2026-08-12 10:00:00", edad: 39);
            InsertPersona(connection, "25000000040", "EDAD", "B", "CABA", "2026-08-10", "2026-08-12 10:00:00", edad: 40);
            InsertPersona(connection, "25000000041", "EDAD", "C", "CABA", "2026-08-10", "2026-08-12 10:00:00", edad: 41);
            InsertPersona(connection, "25000000042", "EDAD", "D", "CABA", "2026-08-10", "2026-08-12 10:00:00", edad: 42);
        }

        var service = new DuckDbPaso3QueryService();
        var gteResult = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [new Paso3Filter("edad", "gte", "40")],
            null,
            null,
            ["cuil", "edad"],
            1,
            50));

        Assert.Equal(3, gteResult.TotalCount);
        Assert.Equal(
            ["25000000040", "25000000041", "25000000042"],
            gteResult.Rows.Select(row => Assert.IsType<string>(row.Values["cuil"])).ToArray());

        var betweenResult = service.QueryPreview(new Paso3PreviewRequest(
            ctx.DatabasePath,
            null,
            [new Paso3Filter("edad", "between", "40,41")],
            null,
            null,
            ["cuil", "edad"],
            1,
            50));

        Assert.Equal(2, betweenResult.TotalCount);
        Assert.Equal(
            ["25000000040", "25000000041"],
            betweenResult.Rows.Select(row => Assert.IsType<string>(row.Values["cuil"])).ToArray());
    }

    [Theory]
    [InlineData("edad", "contains", "40")]
    [InlineData("edad", "eq", "cuarenta")]
    [InlineData("anio", "between", "2020,not-a-number")]
    public void QueryPreview_InvalidNumericFilter_IsRejected(string column, string op, string value)
    {
        using var ctx = CreateDbContext();
        var service = new DuckDbPaso3QueryService();

        var exception = Assert.Throws<ArgumentException>(() =>
            service.QueryPreview(new Paso3PreviewRequest(
                ctx.DatabasePath,
                null,
                [new Paso3Filter(column, op, value)],
                null,
                null,
                ["cuil"],
                1,
                10)));

        Assert.Contains("numéric", exception.Message, StringComparison.OrdinalIgnoreCase);
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
        string provincia,
        string? importDate,
        string updatedAt,
        int? edad = null,
        int? anio = null)
    {
        var importDateValue = string.IsNullOrWhiteSpace(importDate) ? "NULL" : "CAST($importDate AS DATE)";
        ExecuteNonQuery(
            connection,
            $"""
            INSERT INTO personas (
                cuil,
                apellido,
                nombre,
                provincia,
                fecha_importacion,
                fecha_actualizacion,
                edad,
                anio)
            VALUES (
                $cuil,
                $apellido,
                $nombre,
                $provincia,
                {importDateValue},
                CAST($updatedAt AS TIMESTAMP),
                $edad,
                $anio);
            """,
            new DuckDBParameter("cuil", cuil),
            new DuckDBParameter("apellido", apellido),
            new DuckDBParameter("nombre", nombre),
            new DuckDBParameter("provincia", provincia),
            new DuckDBParameter("importDate", (object?)importDate ?? DBNull.Value),
            new DuckDBParameter("updatedAt", updatedAt),
            new DuckDBParameter("edad", (object?)edad ?? DBNull.Value),
            new DuckDBParameter("anio", (object?)anio ?? DBNull.Value));
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

    private static T Scalar<T>(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
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
