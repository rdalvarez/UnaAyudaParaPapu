using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.Core.Stock.Paso4;
using PapaPersonas.Infrastructure.Database;
using PapaPersonas.Infrastructure.Query.Paso3;
using PapaPersonas.Infrastructure.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class DuckDbPaso4StockExportTests
{
    [Fact]
    public async Task GetSummary_And_ExportSummaryCsv_AppliesNormalization_Sorting_Totals_AndHeaders()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        var stockId = Guid.NewGuid();
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ($stockId, DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
            VALUES
                ($stockId, '201', ' os1 ', ' plan a ', 1, FALSE, NULL),
                ($stockId, '202', 'OS1', 'PLAN A', 2, TRUE, CURRENT_TIMESTAMP),
                ($stockId, '203', 'ÖS1', 'PLAN A', 3, FALSE, NULL),
                ($stockId, '204', '', '', 4, FALSE, NULL);
            """,
            new DuckDBParameter("stockId", stockId));

        var service = CreateStockService(ctx.RootDirectory);
        var summary = service.GetCurrentStockSummary(ctx.DatabasePath);

        Assert.Equal(4, summary.Header.TotalMembers);
        Assert.Equal(1, summary.Header.SoldMembers);
        Assert.Equal(3, summary.Header.AvailableMembers);
        Assert.Equal(3, summary.Header.GroupCount);
        Assert.Empty(summary.CatalogWarnings);

        Assert.Equal(1, summary.Groups[0].Orden);
        Assert.Equal("OS1", summary.Groups[0].NormalizedCodigoObraSocial);
        Assert.Equal("PLAN A", summary.Groups[0].NormalizedObraSocial);
        Assert.Equal(2, summary.Groups[0].Total);
        Assert.Equal(1, summary.Groups[0].Sold);
        Assert.Equal(1, summary.Groups[0].Available);

        Assert.Equal("ÖS1", summary.Groups[1].NormalizedCodigoObraSocial);
        Assert.Equal("", summary.Groups[2].NormalizedCodigoObraSocial);
        Assert.Equal("", summary.Groups[2].NormalizedObraSocial);

        var output = Path.Combine(ctx.RootDirectory, "summary.csv");
        var exportResult = await service.ExportSummaryCsvAsync(
            new Paso4SummaryExportRequest(ctx.DatabasePath, output),
            CancellationToken.None);

        Assert.True(exportResult.IsSuccess, exportResult.FailureMessage);
        var lines = await File.ReadAllLinesAsync(output);
        Assert.Equal("ORDEN,COD_O_SOCIAL,DESCRIPCION_O_SOCIAL,CANTIDAD,CANTIDAD_VENDIDA,CANTIDAD_DISPONIBLE,FECHA_CARGA", lines[0]);
        Assert.StartsWith("1,OS1,PLAN A,2,1,1,2026-08-10", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSummary_MergesSameNormalizedCode_AndUsesDeterministicFallbackName()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        var stockId = Guid.NewGuid();
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ($stockId, DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
            VALUES
                ($stockId, '201', ' os1 ', 'Plan B', 1, FALSE, NULL),
                ($stockId, '202', 'OS1', 'Plan A', 2, TRUE, CURRENT_TIMESTAMP),
                ($stockId, '203', 'OS1', 'Plan A', 3, FALSE, NULL),
                ($stockId, '204', ' os1 ', 'Plan C', 4, FALSE, NULL),
                ($stockId, '205', '', 'Nombre suelto', 5, FALSE, NULL),
                ($stockId, '206', '   ', NULL, 6, FALSE, NULL);
            """,
            new DuckDBParameter("stockId", stockId));

        var service = CreateStockService(ctx.RootDirectory);
        var summary = service.GetCurrentStockSummary(ctx.DatabasePath);

        Assert.Equal(2, summary.Header.GroupCount);
        Assert.Equal("OS1", summary.Groups[0].NormalizedCodigoObraSocial);
        Assert.Equal("Plan A", summary.Groups[0].NormalizedObraSocial);
        Assert.Equal(4, summary.Groups[0].Total);
        Assert.Equal(1, summary.Groups[0].Sold);
        Assert.Equal(3, summary.Groups[0].Available);

        Assert.Equal("", summary.Groups[1].NormalizedCodigoObraSocial);
        Assert.Equal("Nombre suelto", summary.Groups[1].NormalizedObraSocial);
        Assert.Equal(2, summary.Groups[1].Total);

        var output = Path.Combine(ctx.RootDirectory, "summary-merged.csv");
        var exportResult = await service.ExportSummaryCsvAsync(
            new Paso4SummaryExportRequest(ctx.DatabasePath, output),
            CancellationToken.None);

        Assert.True(exportResult.IsSuccess, exportResult.FailureMessage);
        var lines = await File.ReadAllLinesAsync(output);
        Assert.Equal("1,OS1,Plan A,4,1,3,2026-08-10", lines[1]);
        Assert.Equal("2,,Nombre suelto,2,0,2,2026-08-10", lines[2]);
    }

    [Fact]
    public async Task GetSummary_UsesCatalogOverride_AndKeepsRawNamesInFullExport()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        var stockId = Guid.NewGuid();
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ($stockId, DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
            VALUES
                ($stockId, '20123456789', 'OS1', 'Plan A', 1, FALSE, NULL),
                ($stockId, '20987654321', 'OS1', 'Plan B', 2, FALSE, NULL);
            """,
            new DuckDBParameter("stockId", stockId));

        var catalog = new Paso4ObraSocialCatalogStore(ctx.RootDirectory);
        catalog.Save(new Dictionary<string, string> { ["OS1"] = "Obra Unificada" });
        var service = new DuckDbPaso4StockService(catalog);

        var summary = service.GetCurrentStockSummary(ctx.DatabasePath);
        Assert.Equal("Obra Unificada", summary.Groups[0].NormalizedObraSocial);

        var summaryPath = Path.Combine(ctx.RootDirectory, "summary-catalog.csv");
        var summaryExport = await service.ExportSummaryCsvAsync(
            new Paso4SummaryExportRequest(ctx.DatabasePath, summaryPath),
            CancellationToken.None);
        Assert.True(summaryExport.IsSuccess, summaryExport.FailureMessage);
        var summaryLines = await File.ReadAllLinesAsync(summaryPath);
        Assert.Equal("1,OS1,Obra Unificada,2,0,2,2026-08-10", summaryLines[1]);

        var fullPath = Path.Combine(ctx.RootDirectory, "full-raw.csv");
        var fullExport = await service.ExportFullCsvAsync(
            new Paso4FullExportRequest(ctx.DatabasePath, fullPath, OnlyAvailable: true, SelectedColumns: ["APELLIDO"]),
            CancellationToken.None);
        Assert.True(fullExport.IsSuccess, fullExport.FailureMessage);
        var fullLines = await File.ReadAllLinesAsync(fullPath);
        Assert.Contains("20123456789,,OS1,Plan A", fullLines[1]);
        Assert.Contains("20987654321,,OS1,Plan B", fullLines[2]);
    }

    [Fact]
    public void GetSummary_WhenCatalogCorrupt_UsesSuggestedNameAndWarning()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        var stockId = Guid.NewGuid();
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ($stockId, DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
            VALUES ($stockId, '201', 'OS1', 'Plan A', 1, FALSE, NULL);
            """,
            new DuckDBParameter("stockId", stockId));

        Directory.CreateDirectory(Path.Combine(ctx.RootDirectory, "config"));
        File.WriteAllText(Path.Combine(ctx.RootDirectory, "config", "paso4-obras-sociales.json"), "{bad json");

        var summary = CreateStockService(ctx.RootDirectory).GetCurrentStockSummary(ctx.DatabasePath);

        Assert.Equal("Plan A", summary.Groups[0].NormalizedObraSocial);
        Assert.NotEmpty(summary.CatalogWarnings);
        Assert.Equal("{bad json", File.ReadAllText(Path.Combine(ctx.RootDirectory, "config", "paso4-obras-sociales.json")));
    }

    [Fact]
    public void GetSummary_WhenCatalogMissing_CreatesCatalogWithSuggestedNames()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedMembers(
            connection,
            ("201", "OS1", "Plan B", false),
            ("202", "OS1", "Plan A", true),
            ("203", "OS1", "Plan A", false),
            ("204", "OS1", "Plan C", false));

        var catalogPath = CatalogPath(ctx.RootDirectory);
        Assert.False(File.Exists(catalogPath));

        var summary = CreateStockService(ctx.RootDirectory).GetCurrentStockSummary(ctx.DatabasePath);

        Assert.Equal("Plan A", summary.Groups[0].NormalizedObraSocial);
        Assert.True(File.Exists(catalogPath));
        var loaded = new Paso4ObraSocialCatalogStore(ctx.RootDirectory).Load();
        Assert.Equal("Plan A", loaded.NamesByNormalizedCode["OS1"]);
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public void GetSummary_UsesAlphabeticalTieBreakForSuggestedName()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedMembers(
            connection,
            ("201", "OS1", "Plan B", false),
            ("202", "OS1", "Plan A", false));

        var summary = CreateStockService(ctx.RootDirectory).GetCurrentStockSummary(ctx.DatabasePath);

        Assert.Equal("Plan A", summary.Groups[0].NormalizedObraSocial);
        Assert.Equal("Plan A", new Paso4ObraSocialCatalogStore(ctx.RootDirectory).Load().NamesByNormalizedCode["OS1"]);
    }

    [Fact]
    public void GetSummary_WhenCatalogValidButIncomplete_AddsOnlyMissingCodes()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedMembers(
            connection,
            ("201", "OS1", "Plan A", false),
            ("202", "OS2", "Plan Z", false),
            ("203", "OS2", "Plan Y", false));

        var store = new Paso4ObraSocialCatalogStore(ctx.RootDirectory);
        store.Save(new Dictionary<string, string> { ["OS1"] = "Keep Me" });
        var counting = new CountingCatalogStore(store);

        var summary = new DuckDbPaso4StockService(counting).GetCurrentStockSummary(ctx.DatabasePath);
        var loaded = store.Load();

        Assert.Equal(1, counting.SaveCount);
        Assert.Equal("Keep Me", summary.Groups[0].NormalizedObraSocial);
        Assert.Equal("Plan Y", summary.Groups[1].NormalizedObraSocial);
        Assert.Equal("Keep Me", loaded.NamesByNormalizedCode["OS1"]);
        Assert.Equal("Plan Y", loaded.NamesByNormalizedCode["OS2"]);
        Assert.Equal(2, loaded.NamesByNormalizedCode.Count);
    }

    [Fact]
    public void GetSummary_WhenCatalogComplete_DoesNotRewriteCatalog()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedMembers(
            connection,
            ("201", "OS1", "Plan A", false),
            ("202", "OS1", "Plan B", false));

        var store = new Paso4ObraSocialCatalogStore(ctx.RootDirectory);
        store.Save(new Dictionary<string, string> { ["OS1"] = "Catalogada" });
        var counting = new CountingCatalogStore(store);
        var service = new DuckDbPaso4StockService(counting);

        var first = service.GetCurrentStockSummary(ctx.DatabasePath);
        var second = service.GetCurrentStockSummary(ctx.DatabasePath);

        Assert.Equal(0, counting.SaveCount);
        Assert.Equal("Catalogada", first.Groups[0].NormalizedObraSocial);
        Assert.Equal("Catalogada", second.Groups[0].NormalizedObraSocial);
        Assert.Equal("Catalogada", store.Load().NamesByNormalizedCode["OS1"]);
    }

    [Fact]
    public void GetSummary_WhenCandidateIsBlank_DoesNotPersistName()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedMembers(
            connection,
            ("201", "OS1", "", false),
            ("202", "OS1", "   ", false),
            ("203", "OS1", null, false));

        var summary = CreateStockService(ctx.RootDirectory).GetCurrentStockSummary(ctx.DatabasePath);

        Assert.Equal("", summary.Groups[0].NormalizedObraSocial);
        Assert.False(File.Exists(CatalogPath(ctx.RootDirectory)));
    }

    [Fact]
    public async Task ExportFullCsv_UsesSnapshotForMandatoryFields_UsesCurrentPersonasForOtherFields_AndOnlyAvailableRules()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        var stockId = Guid.NewGuid();
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ($stockId, DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO personas (cuil, apellido, nombre, codigo_postal, localidad, partido, provincia, nacionalidad, celular_1, celular_2, celular_3, celular_4, celular_5, fecha_nacimiento, edad, fecha_actualizacion)
            VALUES
                ('20123456789', 'LOPEZ', 'ANA', '1000', 'LOC', 'PAR', 'PROV', 'ARG', '11', '22', '33', '44', '55', DATE '1990-05-06', 36, CURRENT_TIMESTAMP),
                ('20987654321', 'PEREZ', 'BETA', '2000', 'LOC2', 'PAR2', 'PROV2', 'ARG', '66', '77', '88', '99', '00', DATE '1992-01-02', 34, CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
            VALUES
                ($stockId, '20123456789', 'SNAP1', 'SNAP PLAN 1', 1, FALSE, NULL),
                ($stockId, '20987654321', 'SNAP2', 'SNAP PLAN 2', 2, TRUE, CURRENT_TIMESTAMP);
            """,
            new DuckDBParameter("stockId", stockId));

        var service = CreateStockService(ctx.RootDirectory);
        var outputAll = Path.Combine(ctx.RootDirectory, "full-all.csv");
        var outputAvailable = Path.Combine(ctx.RootDirectory, "full-available.csv");

        var allResult = await service.ExportFullCsvAsync(
            new Paso4FullExportRequest(ctx.DatabasePath, outputAll, OnlyAvailable: false, SelectedColumns: ["APELLIDO", "NOMBRE", "UNKNOWN_COLUMN"]),
            CancellationToken.None);

        Assert.True(allResult.IsSuccess, allResult.FailureMessage);
        var allLines = await File.ReadAllLinesAsync(outputAll);
        Assert.Equal("CUIL,APELLIDO,NOMBRE,CODIGOOS,OBRASOCIAL,VENDIDO", allLines[0]);
        Assert.Contains("20123456789,LOPEZ,ANA,SNAP1,SNAP PLAN 1,0", allLines[1]);
        Assert.Contains("20987654321,PEREZ,BETA,SNAP2,SNAP PLAN 2,1", allLines[2]);

        var availableResult = await service.ExportFullCsvAsync(
            new Paso4FullExportRequest(ctx.DatabasePath, outputAvailable, OnlyAvailable: true, SelectedColumns: ["APELLIDO", "NOMBRE"]),
            CancellationToken.None);

        Assert.True(availableResult.IsSuccess, availableResult.FailureMessage);
        var availableLines = await File.ReadAllLinesAsync(outputAvailable);
        Assert.Equal("CUIL,APELLIDO,NOMBRE,CODIGOOS,OBRASOCIAL", availableLines[0]);
        Assert.Single(availableLines.Skip(1));
        Assert.Contains("20123456789,LOPEZ,ANA,SNAP1,SNAP PLAN 1", availableLines[1]);
    }

    [Fact]
    public async Task ExportFullCsv_AllBusinessAliases_AreSelectableAndExported()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        var stockId = Guid.NewGuid();
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ($stockId, DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO personas (
                cuil, dni, fecha_nacimiento, sexo, tipo_dni, apellido, nombre, direccion,
                codigo_postal, localidad, partido, provincia, nacionalidad,
                telefono_fijo_1, telefono_fijo_2, telefono_fijo_3, telefono_fijo_4, telefono_fijo_5,
                celular_1, celular_2, celular_3, celular_4, celular_5,
                whatsapp_1, whatsapp_2, whatsapp_3, whatsapp_4, whatsapp_5,
                email_1, email_2, email_3, email_4, email_5,
                codigo_obra_social, obra_social, cuit_empleador, edad, anio,
                fecha_importacion, fecha_actualizacion)
            VALUES
            (
                '20123456789','12345678',DATE '1990-05-06','F','DNI','LOPEZ','ANA','CALLE 123',
                '1000','LOC','PAR','PROV','ARG',
                '111','222','333','444','555',
                '11','22','33','44','55',
                'w1','w2','w3','w4','w5',
                'e1@x.com','e2@x.com','e3@x.com','e4@x.com','e5@x.com',
                'PERS-COD','PERS-OBRA','20304050607',36,2026,
                DATE '2026-08-10', CURRENT_TIMESTAMP
            );

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
            VALUES ($stockId, '20123456789', 'SNAP-COD', 'SNAP-OBRA', 1, FALSE, NULL);
            """,
            new DuckDBParameter("stockId", stockId));

        var service = CreateStockService(ctx.RootDirectory);
        var output = Path.Combine(ctx.RootDirectory, "full-all-business.csv");

        var result = await service.ExportFullCsvAsync(
            new Paso4FullExportRequest(ctx.DatabasePath, output, OnlyAvailable: true, SelectedColumns: Paso4ColumnCatalog.AvailableColumns),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.FailureMessage);
        var lines = await File.ReadAllLinesAsync(output);
        Assert.Equal(string.Join(',', Paso4ColumnCatalog.AvailableColumns), lines[0]);

        var values = lines[1].Split(',');
        Assert.Equal("20123456789", values[0]);
        Assert.Equal("12345678", values[1]);
        Assert.Equal("1990-05-06", values[2]);
        Assert.Equal("F", values[3]);
        Assert.Equal("DNI", values[4]);
        Assert.Equal("LOPEZ", values[5]);
        Assert.Equal("ANA", values[6]);
        Assert.Equal("CALLE 123", values[7]);
        Assert.Equal("1000", values[8]);
        Assert.Equal("LOC", values[9]);
        Assert.Equal("PAR", values[10]);
        Assert.Equal("PROV", values[11]);
        Assert.Equal("ARG", values[12]);
        Assert.Equal("111", values[13]);
        Assert.Equal("222", values[14]);
        Assert.Equal("333", values[15]);
        Assert.Equal("444", values[16]);
        Assert.Equal("555", values[17]);
        Assert.Equal("11", values[18]);
        Assert.Equal("22", values[19]);
        Assert.Equal("33", values[20]);
        Assert.Equal("44", values[21]);
        Assert.Equal("55", values[22]);
        Assert.Equal("w1", values[23]);
        Assert.Equal("w2", values[24]);
        Assert.Equal("w3", values[25]);
        Assert.Equal("w4", values[26]);
        Assert.Equal("w5", values[27]);
        Assert.Equal("e1@x.com", values[28]);
        Assert.Equal("e2@x.com", values[29]);
        Assert.Equal("e3@x.com", values[30]);
        Assert.Equal("e4@x.com", values[31]);
        Assert.Equal("e5@x.com", values[32]);
        Assert.Equal("SNAP-COD", values[33]);
        Assert.Equal("SNAP-OBRA", values[34]);
        Assert.Equal("20304050607", values[35]);
        Assert.Equal("36", values[36]);
        Assert.Equal("2026", values[37]);
    }

    [Fact]
    public async Task ExportFullCsv_DefaultColumnsMatchSliceOrder()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        var stockId = Guid.NewGuid();
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ($stockId, DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO personas (cuil, apellido, nombre, codigo_postal, localidad, partido, provincia, nacionalidad, celular_1, celular_2, celular_3, celular_4, celular_5, fecha_nacimiento, edad, fecha_actualizacion)
            VALUES ('20123456789', 'LOPEZ', 'ANA', '1000', 'LOC', 'PAR', 'PROV', 'ARG', '11', '22', '33', '44', '55', DATE '1990-05-06', 36, CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
            VALUES ($stockId, '20123456789', 'SNAP1', 'SNAP PLAN 1', 1, FALSE, NULL);
            """,
            new DuckDBParameter("stockId", stockId));

        var service = CreateStockService(ctx.RootDirectory);
        var output = Path.Combine(ctx.RootDirectory, "full-default.csv");
        var result = await service.ExportFullCsvAsync(
            new Paso4FullExportRequest(ctx.DatabasePath, output, OnlyAvailable: true, SelectedColumns: []),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.FailureMessage);
        var lines = await File.ReadAllLinesAsync(output);
        Assert.Equal("CUIL,APELLIDO,NOMBRE,CODIGOOS,OBRASOCIAL,CP,LOCALIDAD,PARTIDO,PROVINCIA,NACIONALIDAD,CELULAR1,CELULAR2,CELULAR3,CELULAR4,CELULAR5,FECNANAC,EDAD", lines[0]);
    }

    [Fact]
    public async Task ExportCaughtFailures_PreserveExactTechnicalDetail()
    {
        using var ctx = CreateDbContext();
        var databasePath = Path.Combine(ctx.RootDirectory, "invalid.duckdb");
        File.WriteAllText(databasePath, "not a DuckDB database");
        var expectedTechnicalMessage = ReadDuckDbOpenFailure(databasePath);
        var service = CreateStockService(ctx.RootDirectory);

        var summary = await service.ExportSummaryCsvAsync(
            new Paso4SummaryExportRequest(databasePath, Path.Combine(ctx.RootDirectory, "summary.csv")),
            CancellationToken.None);
        var full = await service.ExportFullCsvAsync(
            new Paso4FullExportRequest(databasePath, Path.Combine(ctx.RootDirectory, "full.csv"), OnlyAvailable: true, SelectedColumns: []),
            CancellationToken.None);

        Assert.Equal(
            $"No se pudo exportar el resumen de stock. Detalle técnico: {expectedTechnicalMessage}",
            summary.FailureMessage);
        Assert.Equal(
            $"No se pudo exportar el stock completo. Detalle técnico: {expectedTechnicalMessage}",
            full.FailureMessage);
    }

    private static DuckDbPaso4StockService CreateStockService(string rootDirectory)
        => new(new Paso4ObraSocialCatalogStore(rootDirectory));

    private static string CatalogPath(string rootDirectory)
        => Path.Combine(rootDirectory, "config", "paso4-obras-sociales.json");

    private static void SeedMembers(
        DuckDBConnection connection,
        params (string Cuil, string Codigo, string? ObraSocial, bool Sold)[] members)
    {
        var stockId = Guid.NewGuid();
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ($stockId, DATE '2026-08-10', CURRENT_TIMESTAMP);
            """,
            new DuckDBParameter("stockId", stockId));

        var order = 1;
        foreach (var member in members)
        {
            ExecuteNonQuery(
                connection,
                member.Sold
                    ? """
                      INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
                      VALUES ($stockId, $cuil, $codigo, $obra, $order, TRUE, CURRENT_TIMESTAMP);
                      """
                    : """
                      INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
                      VALUES ($stockId, $cuil, $codigo, $obra, $order, FALSE, NULL);
                      """,
                new DuckDBParameter("stockId", stockId),
                new DuckDBParameter("cuil", member.Cuil),
                new DuckDBParameter("codigo", member.Codigo),
                new DuckDBParameter("obra", (object?)member.ObraSocial ?? DBNull.Value),
                new DuckDBParameter("order", order));
            order++;
        }
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

    private sealed class CountingCatalogStore : IPaso4ObraSocialCatalogStore
    {
        private readonly IPaso4ObraSocialCatalogStore _inner;

        public CountingCatalogStore(IPaso4ObraSocialCatalogStore inner)
        {
            _inner = inner;
        }

        public int SaveCount { get; private set; }

        public Paso4ObraSocialCatalogState Load()
            => _inner.Load();

        public void Save(IReadOnlyDictionary<string, string> namesByNormalizedCode)
        {
            SaveCount++;
            _inner.Save(namesByNormalizedCode);
        }
    }
}
