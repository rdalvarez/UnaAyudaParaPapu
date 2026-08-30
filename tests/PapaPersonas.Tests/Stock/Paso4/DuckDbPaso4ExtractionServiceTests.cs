using System.Globalization;
using System.Collections.Concurrent;
using DuckDB.NET.Data;
using PapaPersonas.Core.Stock.Paso4;
using PapaPersonas.Infrastructure.Database;
using PapaPersonas.Infrastructure.Query.Paso3;
using PapaPersonas.Infrastructure.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class DuckDbPaso4ExtractionServiceTests
{
    [Fact]
    public async Task BeginExtraction_TwoGroups_ReservesExportsFinalizes_WithSameFechaVenta_AndNoReuseNextExtraction()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedStockForExtraction(connection);

        var service = new DuckDbPaso4ExtractionService();
        var output = Path.Combine(ctx.RootDirectory, "extract.csv");
        var result = await service.BeginExtractionAsync(
            new Paso4BeginExtractionRequest(
                ctx.DatabasePath,
                output,
                ["APELLIDO"],
                [
                    new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 2),
                    new Paso4ExtractionGroupQuantityRequest("OS2", "PLAN B", 1)
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(3, result.ExportedRows);
        Assert.Equal(3, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE vendido = TRUE;"));
        Assert.Equal(1, Scalar<int>(connection, "SELECT COUNT(DISTINCT fecha_venta) FROM stock_members WHERE vendido = TRUE;"));

        var second = await service.BeginExtractionAsync(
            new Paso4BeginExtractionRequest(
                ctx.DatabasePath,
                Path.Combine(ctx.RootDirectory, "extract-2.csv"),
                ["APELLIDO"],
                [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 2)]),
            CancellationToken.None);

        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task BeginExtraction_InsufficientGroup_FailsWithoutReservationOrSaleOrFile()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedStockForExtraction(connection);

        var service = new DuckDbPaso4ExtractionService();
        var output = Path.Combine(ctx.RootDirectory, "extract.csv");
        var result = await service.BeginExtractionAsync(
            new Paso4BeginExtractionRequest(
                ctx.DatabasePath,
                output,
                ["APELLIDO"],
                [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 99)]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(output));
        Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE extraction_token IS NOT NULL;"));
        Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE vendido = TRUE;"));
    }

    [Fact]
    public void PreflightExtraction_InsufficientStock_ReturnsTypedDetails_AndNoMutation()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedStockForExtraction(connection);

        var service = new DuckDbPaso4ExtractionService();
        var beforePending = Scalar<int>(connection, "SELECT COUNT(*) FROM stock_headers WHERE pending_extraction_token IS NOT NULL;");
        var beforeReserved = Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE extraction_token IS NOT NULL;");
        var beforeSold = Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE vendido = TRUE;");

        var preflight = service.PreflightExtraction(
            ctx.DatabasePath,
            [new Paso4ExtractionGroupQuantityRequest("OS2", "PLAN B", 2)]);

        Assert.Equal(Paso4ExtractionPreflightStatus.InsufficientStock, preflight.Status);
        Assert.Equal("OS2", preflight.NormalizedCodigoObraSocial);
        Assert.Equal("PLAN B", preflight.NormalizedObraSocial);
        Assert.Equal(2, preflight.RequestedQuantity);
        Assert.Equal(1, preflight.AvailableQuantity);

        Assert.Equal(beforePending, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_headers WHERE pending_extraction_token IS NOT NULL;"));
        Assert.Equal(beforeReserved, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE extraction_token IS NOT NULL;"));
        Assert.Equal(beforeSold, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE vendido = TRUE;"));
    }

    [Fact]
    public void PreflightExtraction_Ready_ForValidMultiGroupRequest()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedStockForExtraction(connection);

        var service = new DuckDbPaso4ExtractionService();
        var preflight = service.PreflightExtraction(
            ctx.DatabasePath,
            [
                new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 2),
                new Paso4ExtractionGroupQuantityRequest("OS2", "PLAN B", 1)
            ]);

        Assert.Equal(Paso4ExtractionPreflightStatus.Ready, preflight.Status);
        Assert.True(preflight.IsReady);
    }

    [Fact]
    public void PreflightExtraction_ReturnsPending_NoStock_Duplicate_Invalid_AndGroupNotFound()
    {
        using var ctx = CreateDbContext();
        var service = new DuckDbPaso4ExtractionService();

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            var noStock = service.PreflightExtraction(
                ctx.DatabasePath,
                [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 1)]);
            Assert.Equal(Paso4ExtractionPreflightStatus.NoStock, noStock.Status);
        }

        using (var connection = OpenConnection(ctx.DatabasePath))
        {
            SeedStockForExtraction(connection);
            var duplicate = service.PreflightExtraction(
                ctx.DatabasePath,
                [
                    new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 1),
                    new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 1)
                ]);
            Assert.Equal(Paso4ExtractionPreflightStatus.InvalidRequest, duplicate.Status);

            var invalidQty = service.PreflightExtraction(
                ctx.DatabasePath,
                [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 0)]);
            Assert.Equal(Paso4ExtractionPreflightStatus.InvalidRequest, invalidQty.Status);

            var notFound = service.PreflightExtraction(
                ctx.DatabasePath,
                [new Paso4ExtractionGroupQuantityRequest("OSX", "PLAN X", 1)]);
            Assert.Equal(Paso4ExtractionPreflightStatus.GroupNotFound, notFound.Status);
            Assert.Equal("OSX", notFound.NormalizedCodigoObraSocial);
            Assert.Equal("PLAN X", notFound.NormalizedObraSocial);

            var pendingToken = Guid.NewGuid();
            ExecuteNonQuery(
                connection,
                "UPDATE stock_headers SET pending_extraction_token = $token, pending_started_utc = CURRENT_TIMESTAMP, pending_output_path = 'C:/tmp/p.csv', pending_expected_rows = 1, pending_selected_columns_json = '[\"CUIL\",\"CODIGOOS\",\"OBRASOCIAL\"]' WHERE stock_id='11111111-1111-1111-1111-111111111111';",
                new DuckDBParameter("token", pendingToken));

            var pending = service.PreflightExtraction(
                ctx.DatabasePath,
                [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 1)]);
            Assert.Equal(Paso4ExtractionPreflightStatus.Pending, pending.Status);
        }
    }

    [Fact]
    public async Task BeginExtraction_InvalidRequestsRejected()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedStockForExtraction(connection);

        var service = new DuckDbPaso4ExtractionService();
        var duplicate = await service.BeginExtractionAsync(
            new Paso4BeginExtractionRequest(
                ctx.DatabasePath,
                Path.Combine(ctx.RootDirectory, "extract.csv"),
                ["APELLIDO"],
                [
                    new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 1),
                    new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 1)
                ]),
            CancellationToken.None);

        Assert.False(duplicate.IsSuccess);

        var invalidQty = await service.BeginExtractionAsync(
            new Paso4BeginExtractionRequest(
                ctx.DatabasePath,
                Path.Combine(ctx.RootDirectory, "extract2.csv"),
                ["APELLIDO"],
                [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 0)]),
            CancellationToken.None);

        Assert.False(invalidQty.IsSuccess);
    }

    [Fact]
    public async Task BeginExtraction_SelectsBySourceOrderThenCuil_AndWritesFechaCargaHeader()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedStockForSelectionOrder(connection);

        var service = new DuckDbPaso4ExtractionService();
        var output = Path.Combine(ctx.RootDirectory, "extract.csv");
        var result = await service.BeginExtractionAsync(
            new Paso4BeginExtractionRequest(
                ctx.DatabasePath,
                output,
                ["APELLIDO"],
                [new Paso4ExtractionGroupQuantityRequest("OSX", "PLAN X", 2)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.FailureMessage);
        var lines = await File.ReadAllLinesAsync(output);
        Assert.Equal("CUIL,APELLIDO,CODIGOOS,OBRASOCIAL,FECHA_CARGA", lines[0]);
        Assert.StartsWith("20000000001", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("20999999999", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryPendingExtraction_ExportsSameReservedSet_AndFinalizes()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedPendingExtraction(connection, out var token, out var finalPath);

        var service = new DuckDbPaso4ExtractionService();
        var retry = await service.RetryPendingExtractionAsync(ctx.DatabasePath, CancellationToken.None);

        Assert.True(retry.IsSuccess, retry.FailureMessage);
        Assert.Equal(token, retry.Token);
        Assert.True(File.Exists(finalPath));
        var lines = await File.ReadAllLinesAsync(finalPath);
        Assert.Equal("CUIL,FECNANAC,APELLIDO,NOMBRE,CP,LOCALIDAD,PARTIDO,PROVINCIA,NACIONALIDAD,CELULAR1,CELULAR2,CELULAR3,CELULAR4,CELULAR5,CODIGOOS,OBRASOCIAL,EDAD,FECHA_CARGA", lines[0]);
        Assert.Contains("20123456789", string.Join("\n", lines));
        Assert.Contains("20987654321", string.Join("\n", lines));
        Assert.Equal(2, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE extraction_token = $token AND vendido = TRUE;", new DuckDBParameter("token", token)));
        Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_headers WHERE pending_extraction_token IS NOT NULL;"));
    }

    [Fact]
    public async Task RetryPendingExtraction_UsesPersistedPendingSelectedColumns_NotDefaults()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedPendingExtraction(connection, out _, out var finalPath, "[\"CUIL\",\"APELLIDO\",\"CODIGOOS\",\"OBRASOCIAL\"]");

        var service = new DuckDbPaso4ExtractionService();
        var retry = await service.RetryPendingExtractionAsync(ctx.DatabasePath, CancellationToken.None);

        Assert.True(retry.IsSuccess, retry.FailureMessage);
        var lines = await File.ReadAllLinesAsync(finalPath);
        Assert.Equal("CUIL,APELLIDO,CODIGOOS,OBRASOCIAL,FECHA_CARGA", lines[0]);
    }

    [Fact]
    public async Task BeginExtraction_WhenPendingAlreadyExists_IsBlocked()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedPendingExtraction(connection, out _, out _);

        var service = new DuckDbPaso4ExtractionService();
        var result = await service.BeginExtractionAsync(
            new Paso4BeginExtractionRequest(
                ctx.DatabasePath,
                Path.Combine(ctx.RootDirectory, "blocked.csv"),
                ["APELLIDO"],
                [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 1)]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Paso4BeginExtractionStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public void CancelPendingExtraction_ReleasesReservations_AndClearsPending()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedPendingExtraction(connection, out var token, out var finalPath);
        File.WriteAllText(finalPath, "dummy");
        var tempPath = BuildTokenTempPath(finalPath, token);
        File.WriteAllText(tempPath, "temp");

        var service = new DuckDbPaso4ExtractionService();
        var result = service.CancelPendingExtraction(ctx.DatabasePath);

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(token, result.Token);
        Assert.False(File.Exists(tempPath));
        Assert.False(File.Exists(finalPath));
        Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE extraction_token = $token;", new DuckDBParameter("token", token)));
        Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_headers WHERE pending_extraction_token IS NOT NULL;"));
    }

    [Fact]
    public void CancelPendingExtraction_FileDeletionFailure_KeepsPendingBlock()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedPendingExtraction(connection, out _, out var finalPath);
        Directory.CreateDirectory(finalPath);

        var service = new DuckDbPaso4ExtractionService();
        var result = service.CancelPendingExtraction(ctx.DatabasePath);

        Assert.Equal(Paso4RecoveryStatus.Blocked, result.Status);
        Assert.Equal(1, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_headers WHERE pending_extraction_token IS NOT NULL;"));
    }

    [Fact]
    public async Task ReExportCompleted_UsesSameCuilAndSnapshotOs_ButCurrentContactData_NoMutation()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedCompletedExtraction(connection, out var token);
        ExecuteNonQuery(connection, "UPDATE personas SET apellido = 'UPDATED' WHERE cuil = '20123456789';");

        var service = new DuckDbPaso4ExtractionService();
        var output = Path.Combine(ctx.RootDirectory, "reexport.csv");
        var result = await service.ReExportCompletedAsync(
            new Paso4ReExportExtractionRequest(ctx.DatabasePath, token, output, ["APELLIDO"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.FailureMessage);
        var lines = await File.ReadAllLinesAsync(output);
        Assert.Contains("20123456789,UPDATED,SNAP1,PLAN1", lines[1]);
        Assert.Equal(1, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE extraction_token = $token AND vendido = TRUE;", new DuckDBParameter("token", token)));
    }

    [Fact]
    public async Task RetryPendingExtraction_ExpectedCountMismatch_LeavesPendingAndNoSold()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedPendingExtraction(connection, out _, out _);
        ExecuteNonQuery(connection, "UPDATE stock_headers SET pending_expected_rows = 3 WHERE pending_extraction_token IS NOT NULL;");

        var service = new DuckDbPaso4ExtractionService();
        var retry = await service.RetryPendingExtractionAsync(ctx.DatabasePath, CancellationToken.None);

        Assert.False(retry.IsSuccess);
        Assert.Equal(1, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_headers WHERE pending_extraction_token IS NOT NULL;"));
        Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE vendido = TRUE;"));
    }

    [Fact]
    public void ListCompletedExtractions_ReturnsNewestFirstWithCounts()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);

        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ('77777777-7777-7777-7777-777777777777', DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta, extraction_token)
            VALUES
              ('77777777-7777-7777-7777-777777777777', '201', 'A', 'A', 1, TRUE, TIMESTAMP '2026-08-10 10:00:00', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
              ('77777777-7777-7777-7777-777777777777', '202', 'A', 'A', 2, TRUE, TIMESTAMP '2026-08-10 10:00:00', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
              ('77777777-7777-7777-7777-777777777777', '203', 'B', 'B', 3, TRUE, TIMESTAMP '2026-08-10 11:00:00', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb');
            """);

        var service = new DuckDbPaso4ExtractionService();
        var options = service.ListCompletedExtractions(ctx.DatabasePath);

        Assert.Equal(2, options.Count);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), options[0].Token);
        Assert.Equal(1, options[0].RowCount);
        Assert.Equal(2, options[1].RowCount);
    }

    [Fact]
    public async Task ReExportCompleted_UnknownToken_IsValidationFailure_NoFile()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedCompletedExtraction(connection, out _);

        var output = Path.Combine(ctx.RootDirectory, "missing-token.csv");
        var service = new DuckDbPaso4ExtractionService();
        var result = await service.ReExportCompletedAsync(
            new Paso4ReExportExtractionRequest(ctx.DatabasePath, Guid.NewGuid(), output, ["APELLIDO"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task CompletedExtractions_AreRemovedAfterStockRegenerate()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedCompletedExtraction(connection, out _);

        ExecuteNonQuery(
            connection,
            """
            UPDATE personas
            SET fecha_importacion = DATE '2026-08-10',
                source_row_number = 1
            WHERE cuil = '20123456789';
            """);

        var stockService = new DuckDbPaso4StockService();
        var regen = stockService.GenerateOrRegenerateStock(new Paso4GenerateStockRequest(ctx.DatabasePath, DateOnly.Parse("2026-08-10", CultureInfo.InvariantCulture)));
        Assert.True(regen.IsSuccess, regen.FailureMessage);

        var extractionService = new DuckDbPaso4ExtractionService();
        var options = extractionService.ListCompletedExtractions(ctx.DatabasePath);
        Assert.Empty(options);
    }

    [Fact]
    public async Task BeginExtraction_ConcurrentSameStock_AtMostOneSucceeds_NoDoubleSell()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedStockForExtraction(connection);

        var service = new DuckDbPaso4ExtractionService();
        var outputs = new ConcurrentBag<string>();
        var requestFactory = (int i) => new Paso4BeginExtractionRequest(
            ctx.DatabasePath,
            Path.Combine(ctx.RootDirectory, $"concurrent-{i}.csv"),
            ["APELLIDO"],
            [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 2)]);

        var t1 = Task.Run(async () =>
        {
            var result = await service.BeginExtractionAsync(requestFactory(1), CancellationToken.None);
            if (result.IsSuccess && result.OutputPath is not null)
            {
                outputs.Add(result.OutputPath);
            }

            return result;
        });

        var t2 = Task.Run(async () =>
        {
            var result = await service.BeginExtractionAsync(requestFactory(2), CancellationToken.None);
            if (result.IsSuccess && result.OutputPath is not null)
            {
                outputs.Add(result.OutputPath);
            }

            return result;
        });

        var results = await Task.WhenAll(t1, t2);

        Assert.InRange(results.Count(r => r.IsSuccess), 0, 1);
        Assert.InRange(results.Count(r => !r.IsSuccess), 1, 2);
        Assert.InRange(Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE vendido = TRUE;"), 0, 2);
        Assert.Equal(Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE vendido = TRUE;"), Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE extraction_token IS NOT NULL AND vendido = TRUE;"));
        Assert.InRange(outputs.Count(path => File.Exists(path)), 0, 1);
    }

    [Fact]
    public async Task FinalFileExists_WhenFinalizeCasFails_PendingRemainsZeroSold_ThenRetryResolves()
    {
        using var ctx = CreateDbContext();
        using var connection = OpenConnection(ctx.DatabasePath);
        SeedStockForExtraction(connection);

        var output = Path.Combine(ctx.RootDirectory, "finalize-cas-fail.csv");
        var beginService = new DuckDbPaso4ExtractionService(path => path + ".mismatch");

        var begin = await beginService.BeginExtractionAsync(
            new Paso4BeginExtractionRequest(
                ctx.DatabasePath,
                output,
                ["APELLIDO"],
                [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 2)]),
            CancellationToken.None);

        Assert.False(begin.IsSuccess);
        Assert.True(File.Exists(output));
        Assert.Equal(1, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_headers WHERE pending_extraction_token IS NOT NULL;"));
        Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE vendido = TRUE;"));

        var retryService = new DuckDbPaso4ExtractionService();
        var retry = await retryService.RetryPendingExtractionAsync(ctx.DatabasePath, CancellationToken.None);
        Assert.True(retry.IsSuccess, retry.FailureMessage);
        Assert.True(File.Exists(output));
        Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_headers WHERE pending_extraction_token IS NOT NULL;"));
        Assert.Equal(2, Scalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE vendido = TRUE;"));
    }

    [Fact]
    public async Task ServiceCaughtFailures_PreserveExactTechnicalDetail()
    {
        using var ctx = CreateDbContext();
        var databasePath = Path.Combine(ctx.RootDirectory, "invalid.duckdb");
        File.WriteAllText(databasePath, "not a DuckDB database");
        var expectedTechnicalMessage = ReadDuckDbOpenFailure(databasePath);
        var service = new DuckDbPaso4ExtractionService();

        var preflight = service.PreflightExtraction(
            databasePath,
            [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 1)]);
        var begin = await service.BeginExtractionAsync(
            new Paso4BeginExtractionRequest(
                databasePath,
                Path.Combine(ctx.RootDirectory, "begin.csv"),
                ["APELLIDO"],
                [new Paso4ExtractionGroupQuantityRequest("OS1", "PLAN A", 1)]),
            CancellationToken.None);
        var retry = await service.RetryPendingExtractionAsync(databasePath, CancellationToken.None);
        var cancel = service.CancelPendingExtraction(databasePath);
        var reexport = await service.ReExportCompletedAsync(
            new Paso4ReExportExtractionRequest(
                databasePath,
                Guid.NewGuid(),
                Path.Combine(ctx.RootDirectory, "reexport.csv"),
                ["APELLIDO"]),
            CancellationToken.None);

        Assert.Equal(
            $"No se pudo validar la extracción. Verificá la base y volvé a intentar. Detalle técnico: {expectedTechnicalMessage}",
            preflight.TechnicalMessage);
        Assert.Equal(
            $"No se pudo completar la extracción. Verificá la base y volvé a intentar. Detalle técnico: {expectedTechnicalMessage}",
            begin.FailureMessage);
        Assert.Equal(
            $"No se pudo reintentar la extracción pendiente. Detalle técnico: {expectedTechnicalMessage}",
            retry.FailureMessage);
        Assert.Equal(
            $"No se pudo cancelar la extracción pendiente. Detalle técnico: {expectedTechnicalMessage}",
            cancel.FailureMessage);
        Assert.Equal(
            $"No se pudo reexportar la extracción. Verificá la base y volvé a intentar. Detalle técnico: {expectedTechnicalMessage}",
            reexport.FailureMessage);
    }

    private static string BuildTokenTempPath(string destinationPath, Guid token)
    {
        var directory = Path.GetDirectoryName(destinationPath)!;
        var fileName = Path.GetFileName(destinationPath);
        return Path.Combine(directory, $".{fileName}.token-{token:N}.tmp");
    }

    private static void SeedStockForExtraction(DuckDBConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ('11111111-1111-1111-1111-111111111111', DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO personas (cuil, apellido, fecha_actualizacion)
            VALUES ('20123456789', 'A', CURRENT_TIMESTAMP), ('20987654321', 'B', CURRENT_TIMESTAMP), ('20000000001', 'C', CURRENT_TIMESTAMP), ('20000000002', 'D', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
            VALUES
              ('11111111-1111-1111-1111-111111111111', '20123456789', 'OS1', 'PLAN A', 1, FALSE, NULL),
              ('11111111-1111-1111-1111-111111111111', '20987654321', 'OS1', 'PLAN A', 2, FALSE, NULL),
              ('11111111-1111-1111-1111-111111111111', '20000000001', 'OS1', 'PLAN A', 3, FALSE, NULL),
              ('11111111-1111-1111-1111-111111111111', '20000000002', 'OS2', 'PLAN B', 4, FALSE, NULL);
            """);
    }

    private static void SeedStockForSelectionOrder(DuckDBConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ('22222222-2222-2222-2222-222222222222', DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO personas (cuil, apellido, fecha_actualizacion)
            VALUES ('20999999999', 'B', CURRENT_TIMESTAMP), ('20000000001', 'A', CURRENT_TIMESTAMP), ('20111111111', 'C', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta)
            VALUES
              ('22222222-2222-2222-2222-222222222222', '20999999999', 'OSX', 'PLAN X', 10, FALSE, NULL),
              ('22222222-2222-2222-2222-222222222222', '20000000001', 'OSX', 'PLAN X', 10, FALSE, NULL),
              ('22222222-2222-2222-2222-222222222222', '20111111111', 'OSX', 'PLAN X', 11, FALSE, NULL);
            """);
    }

    private static void SeedPendingExtraction(DuckDBConnection connection, out Guid token, out string finalPath, string? pendingSelectedColumnsJson = null)
    {
        token = Guid.Parse("33333333-3333-3333-3333-333333333333");
        finalPath = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"), "pending.csv");
        var parent = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(parent);
        pendingSelectedColumnsJson ??= "[\"CUIL\",\"APELLIDO\",\"NOMBRE\",\"CODIGOOS\",\"OBRASOCIAL\",\"CP\",\"LOCALIDAD\",\"PARTIDO\",\"PROVINCIA\",\"NACIONALIDAD\",\"CELULAR1\",\"CELULAR2\",\"CELULAR3\",\"CELULAR4\",\"CELULAR5\",\"FECNANAC\",\"EDAD\"]";

        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (
                stock_id,
                source_fecha_importacion,
                generated_utc,
                pending_extraction_token,
                pending_started_utc,
                pending_output_path,
                pending_expected_rows,
                pending_selected_columns_json)
            VALUES ('44444444-4444-4444-4444-444444444444', DATE '2026-08-10', CURRENT_TIMESTAMP, $token, CURRENT_TIMESTAMP, $path, 2, $selectedColumnsJson);

            INSERT INTO personas (cuil, apellido, fecha_actualizacion)
            VALUES ('20123456789', 'A', CURRENT_TIMESTAMP), ('20987654321', 'B', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta, extraction_token)
            VALUES
              ('44444444-4444-4444-4444-444444444444', '20123456789', 'OS1', 'PLAN A', 1, FALSE, NULL, $token),
              ('44444444-4444-4444-4444-444444444444', '20987654321', 'OS1', 'PLAN A', 2, FALSE, NULL, $token);
            """,
            new DuckDBParameter("token", token),
            new DuckDBParameter("path", finalPath),
            new DuckDBParameter("selectedColumnsJson", pendingSelectedColumnsJson));
    }

    private static void SeedCompletedExtraction(DuckDBConnection connection, out Guid token)
    {
        token = Guid.Parse("55555555-5555-5555-5555-555555555555");
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO stock_headers (stock_id, source_fecha_importacion, generated_utc)
            VALUES ('66666666-6666-6666-6666-666666666666', DATE '2026-08-10', CURRENT_TIMESTAMP);

            INSERT INTO personas (cuil, apellido, fecha_actualizacion)
            VALUES ('20123456789', 'ORIGINAL', CURRENT_TIMESTAMP);

            INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta, extraction_token)
            VALUES ('66666666-6666-6666-6666-666666666666', '20123456789', 'SNAP1', 'PLAN1', 1, TRUE, CURRENT_TIMESTAMP, $token);
            """,
            new DuckDBParameter("token", token));
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
