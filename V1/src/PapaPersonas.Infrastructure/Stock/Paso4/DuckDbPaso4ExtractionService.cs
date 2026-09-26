using System.Globalization;
using System.Text;
using System.Text.Json;
using DuckDB.NET.Data;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Stock.Paso4;
using PapaPersonas.Infrastructure.Csv;
using PapaPersonas.Infrastructure.Query.Paso3;

namespace PapaPersonas.Infrastructure.Stock.Paso4;

public sealed class DuckDbPaso4ExtractionService : IPaso4ExtractionService
{
    private readonly Func<string, string>? _finalizeOutputPathOverride;

    public DuckDbPaso4ExtractionService()
    {
    }

    public DuckDbPaso4ExtractionService(Func<string, string>? finalizeOutputPathOverride)
    {
        _finalizeOutputPathOverride = finalizeOutputPathOverride;
    }

    /// <summary>Verifica stock, pendientes, grupos y disponibilidad sin reservar ni modificar filas.</summary>
    public Paso4ExtractionPreflightResult PreflightExtraction(string databasePath, IReadOnlyList<Paso4ExtractionGroupQuantityRequest> groupRequests)
    {
        try
        {
            EnsureDatabaseExists(databasePath);

            using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);
            using var tx = connection.BeginTransaction();

            var stock = GetCurrentStock(connection, tx);
            if (stock is null)
            {
                tx.Rollback();
                return Paso4ExtractionPreflightResult.NoStock();
            }

            if (stock.Value.PendingToken.HasValue)
            {
                tx.Rollback();
                return Paso4ExtractionPreflightResult.Pending();
            }

            var validation = ValidateGroupRequests(groupRequests);
            if (!validation.IsValid)
            {
                tx.Rollback();
                return Paso4ExtractionPreflightResult.InvalidRequest(validation.Error);
            }

            var availability = QueryAvailableByGroup(connection, tx, stock.Value.StockId);
            foreach (var group in validation.NormalizedRequests)
            {
                if (!availability.TryGetValue(group.NormalizedCodigoObraSocial, out var available))
                {
                    tx.Rollback();
                    return Paso4ExtractionPreflightResult.GroupNotFound(
                        group.NormalizedCodigoObraSocial,
                        group.NormalizedObraSocial,
                        group.Quantity);
                }

                if (available < group.Quantity)
                {
                    tx.Rollback();
                    return Paso4ExtractionPreflightResult.InsufficientStock(
                        group.NormalizedCodigoObraSocial,
                        group.NormalizedObraSocial,
                        group.Quantity,
                        available);
                }
            }

            tx.Rollback();
            return Paso4ExtractionPreflightResult.Ready();
        }
        catch (Exception ex)
        {
            return Paso4ExtractionPreflightResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo validar la extracción. Verificá la base y volvé a intentar.",
                    ex));
        }
    }

    /// <summary>Reserva grupos de forma atómica, exporta el token pendiente y finaliza la venta mediante CAS.</summary>
    public async Task<Paso4BeginExtractionResult> BeginExtractionAsync(Paso4BeginExtractionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            EnsureDatabaseExists(request.DatabasePath);
            ValidateDestinationPath(request.DestinationCsvPath);

            using var connection = DuckDbPaso3QueryService.OpenConnection(request.DatabasePath);
            using var tx = connection.BeginTransaction();

            var stock = GetCurrentStock(connection, tx);
            if (stock is null)
            {
                tx.Rollback();
                return Paso4BeginExtractionResult.ValidationFailed("No existe un stock actual.");
            }

            var currentStock = stock.Value;

            if (currentStock.PendingToken.HasValue)
            {
                tx.Rollback();
                return Paso4BeginExtractionResult.ValidationFailed("Ya existe una extracción pendiente.");
            }

            var validation = ValidateGroupRequests(request.GroupRequests);
            if (!validation.IsValid)
            {
                tx.Rollback();
                return Paso4BeginExtractionResult.ValidationFailed(validation.Error!);
            }

            var availability = QueryAvailableByGroup(connection, tx, currentStock.StockId);
            foreach (var group in validation.NormalizedRequests)
            {
                if (!availability.TryGetValue(group.NormalizedCodigoObraSocial, out var count) || count < group.Quantity)
                {
                    tx.Rollback();
                    return Paso4BeginExtractionResult.ValidationFailed("El grupo solicitado no tiene suficientes personas disponibles.");
                }
            }

            var token = Guid.NewGuid();
            var expectedRows = validation.NormalizedRequests.Sum(x => x.Quantity);
            var selectedColumns = Paso4ColumnCatalog.NormalizeSelectedColumns(request.SelectedColumns);
            var selectedColumnsJson = JsonSerializer.Serialize(selectedColumns);

            foreach (var group in validation.NormalizedRequests)
            {
                var affected = ReserveGroupRows(connection, tx, currentStock.StockId, token, group);
                if (affected != group.Quantity)
                {
                    tx.Rollback();
                    return Paso4BeginExtractionResult.ValidationFailed("No se pudieron reservar todas las personas solicitadas de forma atómica.");
                }
            }

            var pendingHeaderUpdated = ExecuteNonQuery(
                connection,
                tx,
                """
                UPDATE stock_headers
                SET pending_extraction_token = $token,
                    pending_started_utc = CURRENT_TIMESTAMP,
                    pending_output_path = $outputPath,
                    pending_expected_rows = $expectedRows,
                    pending_selected_columns_json = $selectedColumnsJson
                WHERE stock_id = $stockId
                  AND pending_extraction_token IS NULL;
                """,
                new DuckDBParameter("token", token),
                new DuckDBParameter("outputPath", request.DestinationCsvPath),
                new DuckDBParameter("expectedRows", expectedRows),
                new DuckDBParameter("selectedColumnsJson", selectedColumnsJson),
                new DuckDBParameter("stockId", currentStock.StockId));

            if (pendingHeaderUpdated != 1)
            {
                tx.Rollback();
                return Paso4BeginExtractionResult.ValidationFailed("El stock actual está ocupado por otro intento de extracción.");
            }

            tx.Commit();

            var exportedRows = await ExportReservedTokenAsync(
                request.DatabasePath,
                currentStock.StockId,
                token,
                request.DestinationCsvPath,
                selectedColumns,
                currentStock.SourceDate,
                expectedRows,
                cancellationToken);

            var finalizePath = ResolveFinalizePath(request.DestinationCsvPath);
            await FinalizeTokenAsync(request.DatabasePath, currentStock.StockId, token, finalizePath, expectedRows, cancellationToken);
            return Paso4BeginExtractionResult.Completed(token, expectedRows, exportedRows, request.DestinationCsvPath);
        }
        catch (DuckDBException ex) when (IsWriteConflict(ex))
        {
            return Paso4BeginExtractionResult.ValidationFailed("Se detectó un conflicto de extracción. Reintentá cuando termine la operación actual.");
        }
        catch (OperationCanceledException)
        {
            return Paso4BeginExtractionResult.Failed("La extracción fue cancelada.");
        }
        catch (Exception ex)
        {
            return Paso4BeginExtractionResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo completar la extracción. Verificá la base y volvé a intentar.",
                    ex));
        }
    }

    public Paso4PendingExtractionState? GetPendingState(string databasePath)
    {
        EnsureDatabaseExists(databasePath);

        using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                h.stock_id,
                h.pending_extraction_token,
                h.pending_started_utc,
                h.pending_output_path,
                h.pending_expected_rows,
                h.pending_selected_columns_json,
                (SELECT COUNT(*) FROM stock_members sm WHERE sm.stock_id = h.stock_id AND sm.extraction_token = h.pending_extraction_token AND sm.vendido = FALSE) AS reserved_rows
            FROM stock_headers h
            WHERE h.pending_extraction_token IS NOT NULL
            ORDER BY h.generated_utc DESC, h.stock_id DESC
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new Paso4PendingExtractionState(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetDateTime(2),
            reader.GetString(3),
            Convert.ToInt32(reader.GetInt64(4), CultureInfo.InvariantCulture),
            reader.GetString(5),
            reader.GetInt32(6));
    }

    /// <summary>Reintenta una extracción pendiente reutilizando su token, archivo y cantidad esperada.</summary>
    public async Task<Paso4RetryPendingExtractionResult> RetryPendingExtractionAsync(string databasePath, CancellationToken cancellationToken)
    {
        try
        {
            var pending = GetPendingState(databasePath);
            if (pending is null)
            {
                return Paso4RetryPendingExtractionResult.ValidationFailed("No existe una extracción pendiente.");
            }

            using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);
            var sourceDate = QueryStockSourceDate(connection, pending.StockId);
            var selected = ParsePendingSelectedColumns(pending.SelectedColumnsJson);

            var exported = await ExportReservedTokenAsync(
                databasePath,
                pending.StockId,
                pending.Token,
                pending.OutputPath,
                selected,
                sourceDate,
                pending.ExpectedRows,
                cancellationToken);

            var finalizePath = ResolveFinalizePath(pending.OutputPath);
            await FinalizeTokenAsync(databasePath, pending.StockId, pending.Token, finalizePath, pending.ExpectedRows, cancellationToken);
            return Paso4RetryPendingExtractionResult.Completed(pending.Token, exported, pending.OutputPath);
        }
        catch (DuckDBException ex) when (IsWriteConflict(ex))
        {
            return Paso4RetryPendingExtractionResult.ValidationFailed("Se detectó un conflicto de extracción. Reintentá cuando termine la operación actual.");
        }
        catch (Exception ex)
        {
            return Paso4RetryPendingExtractionResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo reintentar la extracción pendiente.",
                    ex));
        }
    }

    /// <summary>Cancela una extracción pendiente sólo después de eliminar sus archivos y libera sus reservas.</summary>
    public Paso4CancelPendingExtractionResult CancelPendingExtraction(string databasePath)
    {
        try
        {
            var pending = GetPendingState(databasePath);
            if (pending is null)
            {
                return Paso4CancelPendingExtractionResult.ValidationFailed("No existe una extracción pendiente.");
            }

            var tempPath = BuildTokenTempPath(pending.OutputPath, pending.Token);
            if (!TryDeleteFileIfExists(tempPath) || !TryDeleteFileIfExists(pending.OutputPath))
            {
                return Paso4CancelPendingExtractionResult.Blocked(pending.Token, "No se pudieron eliminar los archivos pendientes.");
            }

            using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);
            using var tx = connection.BeginTransaction();

            var released = ExecuteNonQuery(
                connection,
                tx,
                """
                UPDATE stock_members
                SET extraction_token = NULL
                WHERE stock_id = $stockId
                  AND vendido = FALSE
                  AND extraction_token = $token;
                """,
                new DuckDBParameter("stockId", pending.StockId),
                new DuckDBParameter("token", pending.Token));

            ExecuteNonQuery(
                connection,
                tx,
                """
                UPDATE stock_headers
                SET pending_extraction_token = NULL,
                    pending_started_utc = NULL,
                    pending_output_path = NULL,
                    pending_expected_rows = NULL,
                    pending_selected_columns_json = NULL
                WHERE stock_id = $stockId
                  AND pending_extraction_token = $token;
                """,
                new DuckDBParameter("stockId", pending.StockId),
                new DuckDBParameter("token", pending.Token));

            tx.Commit();
            return Paso4CancelPendingExtractionResult.Completed(pending.Token, released);
        }
        catch (Exception ex)
        {
            return Paso4CancelPendingExtractionResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo cancelar la extracción pendiente.",
                    ex));
        }
    }

    public IReadOnlyList<Paso4CompletedExtractionOption> ListCompletedExtractions(string databasePath)
    {
        EnsureDatabaseExists(databasePath);

        using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);
        var stockId = QueryCurrentStockId(connection);
        if (!stockId.HasValue)
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT extraction_token, fecha_venta, COUNT(*)
            FROM stock_members
            WHERE stock_id = $stockId
              AND vendido = TRUE
              AND extraction_token IS NOT NULL
              AND fecha_venta IS NOT NULL
            GROUP BY extraction_token, fecha_venta
            ORDER BY fecha_venta DESC, extraction_token DESC;
            """;
        command.Parameters.Add(new DuckDBParameter("stockId", stockId.Value));

        using var reader = command.ExecuteReader();
        var result = new List<Paso4CompletedExtractionOption>();
        while (reader.Read())
        {
            result.Add(new Paso4CompletedExtractionOption(
                reader.GetGuid(0),
                reader.GetDateTime(1),
                reader.GetInt32(2)));
        }

        return result;
    }

    /// <summary>Reexporta filas vendidas de un token completado sin alterar el estado del stock.</summary>
    public async Task<Paso4ReExportExtractionResult> ReExportCompletedAsync(Paso4ReExportExtractionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            EnsureDatabaseExists(request.DatabasePath);
            ValidateDestinationPath(request.DestinationCsvPath);

            using var connection = DuckDbPaso3QueryService.OpenConnection(request.DatabasePath);
            var stockId = QueryCurrentStockId(connection);
            if (!stockId.HasValue)
            {
                return Paso4ReExportExtractionResult.ValidationFailed("No existe un stock actual.");
            }

            var exists = ExecuteScalarInt(
                connection,
                """
                SELECT COUNT(*)
                FROM stock_members
                WHERE stock_id = $stockId
                  AND vendido = TRUE
                  AND extraction_token = $token;
                """,
                new DuckDBParameter("stockId", stockId.Value),
                new DuckDBParameter("token", request.Token));

            if (exists <= 0)
            {
                return Paso4ReExportExtractionResult.ValidationFailed("No se encontró el token de extracción solicitado en el stock actual.");
            }

            var sourceDate = QueryStockSourceDate(connection, stockId.Value);
            var selected = Paso4ColumnCatalog.NormalizeSelectedColumns(request.SelectedColumns);
            var rows = await ExportSoldTokenAsync(connection, stockId.Value, request.Token, request.DestinationCsvPath, selected, sourceDate, cancellationToken);

            return Paso4ReExportExtractionResult.Completed(request.Token, rows, request.DestinationCsvPath);
        }
        catch (Exception ex)
        {
            return Paso4ReExportExtractionResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo reexportar la extracción. Verificá la base y volvé a intentar.",
                    ex));
        }
    }

    private static async Task<int> ExportReservedTokenAsync(
        string databasePath,
        Guid stockId,
        Guid token,
        string destinationPath,
        IReadOnlyList<string> selectedColumns,
        DateOnly sourceDate,
        int expectedRows,
        CancellationToken cancellationToken)
    {
        using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);
        var rows = await ExportByTokenAsync(connection, stockId, token, destinationPath, selectedColumns, sourceDate, soldOnly: false, cancellationToken);
        if (rows != expectedRows)
        {
            throw new InvalidOperationException("La cantidad de filas exportadas no coincide con la cantidad pendiente esperada.");
        }

        return rows;
    }

    private static Task<int> ExportSoldTokenAsync(
        DuckDBConnection connection,
        Guid stockId,
        Guid token,
        string destinationPath,
        IReadOnlyList<string> selectedColumns,
        DateOnly sourceDate,
        CancellationToken cancellationToken)
    {
        return ExportByTokenAsync(connection, stockId, token, destinationPath, selectedColumns, sourceDate, soldOnly: true, cancellationToken);
    }

    // Escribe el archivo de un token en temporal y lo publica sólo al completar todas las filas.
    private static async Task<int> ExportByTokenAsync(
        DuckDBConnection connection,
        Guid stockId,
        Guid token,
        string destinationPath,
        IReadOnlyList<string> selectedColumns,
        DateOnly sourceDate,
        bool soldOnly,
        CancellationToken cancellationToken)
    {
        var headers = selectedColumns.Concat(["FECHA_CARGA"]).ToList();
        var tempPath = BuildTokenTempPath(destinationPath, token);
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
                                    ?? throw new ArgumentException("La ruta del CSV de destino debe incluir una carpeta.", nameof(destinationPath));
        Directory.CreateDirectory(destinationDirectory);

        var selectList = string.Join(", ", selectedColumns.Select(column => $"{Paso4ColumnCatalog.SelectExpressions[column]} AS {column}"));
        var soldPredicate = soldOnly ? "sm.vendido = TRUE" : "sm.vendido = FALSE";

        var sql =
            $"""
            SELECT {selectList}
            FROM stock_members sm
            LEFT JOIN personas p ON p.cuil = sm.cuil
            WHERE sm.stock_id = $stockId
              AND sm.extraction_token = $token
              AND {soldPredicate}
            ORDER BY sm.source_order ASC, sm.cuil ASC;
            """;

        try
        {
            var rowsWritten = 0;
            await using (var writer = new StreamWriter(tempPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                await CsvExportFileWriter.WriteRowAsync(writer, headers, cancellationToken);

                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.Add(new DuckDBParameter("stockId", stockId));
                command.Parameters.Add(new DuckDBParameter("token", token));

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var values = new List<string>(reader.FieldCount + 1);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        values.Add(CsvExportFileWriter.FormatValue(reader.GetValue(i)));
                    }

                    values.Add(sourceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    await CsvExportFileWriter.WriteRowAsync(writer, values, cancellationToken);
                    rowsWritten++;
                }

                await writer.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            return rowsWritten;
        }
        catch
        {
            TryDeleteFileIfExists(tempPath);
            throw;
        }
    }

    // Finaliza un token mediante comprobaciones CAS y convierte sus reservas en ventas de forma atómica.
    private static async Task FinalizeTokenAsync(
        string databasePath,
        Guid stockId,
        Guid token,
        string outputPath,
        int expectedRows,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);
        using var tx = connection.BeginTransaction();

        var casMatches = ExecuteScalarInt(
            connection,
            tx,
            """
            SELECT COUNT(*)
            FROM stock_headers
            WHERE stock_id = $stockId
              AND pending_extraction_token = $token
              AND pending_output_path = $outputPath
              AND pending_expected_rows = $expectedRows
              AND pending_selected_columns_json IS NOT NULL;
            """,
            new DuckDBParameter("stockId", stockId),
            new DuckDBParameter("token", token),
            new DuckDBParameter("outputPath", outputPath),
            new DuckDBParameter("expectedRows", expectedRows));

        if (casMatches != 1)
        {
            tx.Rollback();
            throw new InvalidOperationException("Falló la validación CAS de la extracción pendiente.");
        }

        var saleTimestamp = DateTime.UtcNow;
        var affected = ExecuteNonQuery(
            connection,
            tx,
            """
            UPDATE stock_members
            SET vendido = TRUE,
                fecha_venta = $saleTimestamp
            WHERE stock_id = $stockId
              AND extraction_token = $token
              AND vendido = FALSE;
            """,
            new DuckDBParameter("saleTimestamp", saleTimestamp),
            new DuckDBParameter("stockId", stockId),
            new DuckDBParameter("token", token));

        if (affected != expectedRows)
        {
            tx.Rollback();
            throw new InvalidOperationException("La cantidad de filas vendidas no coincide al finalizar.");
        }

        var cleared = ExecuteNonQuery(
            connection,
            tx,
            """
            UPDATE stock_headers
            SET pending_extraction_token = NULL,
                pending_started_utc = NULL,
                pending_output_path = NULL,
                pending_expected_rows = NULL,
                pending_selected_columns_json = NULL
            WHERE stock_id = $stockId
              AND pending_extraction_token = $token
              AND pending_output_path = $outputPath
              AND pending_expected_rows = $expectedRows
              AND pending_selected_columns_json IS NOT NULL;
            """,
            new DuckDBParameter("stockId", stockId),
            new DuckDBParameter("token", token),
            new DuckDBParameter("outputPath", outputPath),
            new DuckDBParameter("expectedRows", expectedRows));

        if (cleared != 1)
        {
            tx.Rollback();
            throw new InvalidOperationException("Falló la limpieza CAS de la extracción pendiente.");
        }

        tx.Commit();
    }

    private string ResolveFinalizePath(string outputPath)
        => _finalizeOutputPathOverride is null ? outputPath : _finalizeOutputPathOverride(outputPath);

    private static bool IsWriteConflict(DuckDBException ex)
        => ex.Message.Contains("Conflict", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("serialization", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("Transaction", StringComparison.OrdinalIgnoreCase);

    // Reserva exactamente la cantidad solicitada de un grupo según su orden estable y estado disponible.
    private static int ReserveGroupRows(
        DuckDBConnection connection,
        System.Data.Common.DbTransaction tx,
        Guid stockId,
        Guid token,
        Paso4ExtractionGroupQuantityRequest group)
    {
        return ExecuteNonQuery(
            connection,
            tx,
            """
            UPDATE stock_members AS sm
            SET extraction_token = $token
            FROM (
                SELECT cuil
                FROM stock_members
                WHERE stock_id = $stockId
                  AND vendido = FALSE
                  AND extraction_token IS NULL
                   AND UPPER(TRIM(COALESCE(codigo_obra_social, ''))) = $normCodigo
                 ORDER BY source_order ASC, cuil ASC
                 LIMIT $qty
            ) AS picked
            WHERE sm.stock_id = $stockId
              AND sm.cuil = picked.cuil
              AND sm.vendido = FALSE
              AND sm.extraction_token IS NULL;
            """,
            new DuckDBParameter("token", token),
            new DuckDBParameter("stockId", stockId),
            new DuckDBParameter("normCodigo", group.NormalizedCodigoObraSocial),
            new DuckDBParameter("qty", group.Quantity));
    }

    private static Dictionary<string, int> QueryAvailableByGroup(DuckDBConnection connection, System.Data.Common.DbTransaction tx, Guid stockId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT
                UPPER(TRIM(COALESCE(codigo_obra_social, ''))) AS norm_codigo,
                COUNT(*)
            FROM stock_members
            WHERE stock_id = $stockId
              AND vendido = FALSE
              AND extraction_token IS NULL
            GROUP BY 1;
            """;
        command.Parameters.Add(new DuckDBParameter("stockId", stockId));

        using var reader = command.ExecuteReader();
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        while (reader.Read())
        {
            map[reader.GetString(0)] = reader.GetInt32(1);
        }

        return map;
    }

    // Normaliza solicitudes y rechaza cantidades inválidas o grupos repetidos antes de reservar.
    private static (bool IsValid, string? Error, IReadOnlyList<Paso4ExtractionGroupQuantityRequest> NormalizedRequests) ValidateGroupRequests(IReadOnlyList<Paso4ExtractionGroupQuantityRequest> requests)
    {
        if (requests is null || requests.Count == 0)
        {
            return (false, "Se requiere al menos un grupo para extraer.", []);
        }

        var normalized = new List<Paso4ExtractionGroupQuantityRequest>(requests.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            if (request.Quantity <= 0)
            {
                return (false, "Cada cantidad solicitada debe ser mayor que cero.", []);
            }

            var code = NormalizeGroupValue(request.NormalizedCodigoObraSocial);
            var desc = NormalizeGroupValue(request.NormalizedObraSocial);
            if (!seen.Add(code))
            {
                return (false, "No se permiten grupos normalizados duplicados.", []);
            }

            normalized.Add(new Paso4ExtractionGroupQuantityRequest(code, desc, request.Quantity));
        }

        return (true, null, normalized);
    }

    private static string NormalizeGroupValue(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static IReadOnlyList<string> ParsePendingSelectedColumns(string selectedColumnsJson)
    {
        if (string.IsNullOrWhiteSpace(selectedColumnsJson))
        {
            throw new InvalidOperationException("Faltan los metadatos de columnas seleccionadas pendientes.");
        }

        string[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<string[]>(selectedColumnsJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Los metadatos de columnas seleccionadas pendientes no contienen JSON válido.", ex);
        }

        if (parsed is null)
        {
            throw new InvalidOperationException("Los metadatos de columnas seleccionadas pendientes están vacíos.");
        }

        return Paso4ColumnCatalog.NormalizeSelectedColumns(parsed);
    }

    private static DateOnly QueryStockSourceDate(DuckDBConnection connection, Guid stockId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_fecha_importacion FROM stock_headers WHERE stock_id = $stockId;";
        command.Parameters.Add(new DuckDBParameter("stockId", stockId));
        var value = command.ExecuteScalar();
        if (value is DateTime dt)
        {
            return DateOnly.FromDateTime(dt);
        }

        if (value is DateOnly d)
        {
            return d;
        }

        return DateOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
    }

    private static (Guid StockId, DateOnly SourceDate, Guid? PendingToken) ? GetCurrentStock(DuckDBConnection connection, System.Data.Common.DbTransaction tx)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT stock_id, source_fecha_importacion, pending_extraction_token
            FROM stock_headers
            ORDER BY generated_utc DESC, stock_id DESC
            LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return (
            reader.GetGuid(0),
            DateOnly.FromDateTime(reader.GetDateTime(1)),
            reader.IsDBNull(2) ? null : reader.GetGuid(2));
    }

    private static Guid? QueryCurrentStockId(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT stock_id FROM stock_headers ORDER BY generated_utc DESC, stock_id DESC LIMIT 1;";
        var value = command.ExecuteScalar();
        return value is null || value is DBNull ? null : (Guid)value;
    }

    private static string BuildTokenTempPath(string destinationPath, Guid token)
    {
        var directory = Path.GetDirectoryName(destinationPath)
                        ?? throw new ArgumentException("La ruta del CSV de destino debe incluir una carpeta.", nameof(destinationPath));
        var fileName = Path.GetFileName(destinationPath);
        return Path.Combine(directory, $".{fileName}.token-{token:N}.tmp");
    }

    private static bool TryDeleteFileIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        if (Directory.Exists(path))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateDestinationPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("La ruta del CSV de destino debe incluir una carpeta.", nameof(destinationPath));
        }
    }

    private static void EnsureDatabaseExists(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            throw new ArgumentException("No se encontró el archivo de base de datos.", nameof(databasePath));
        }
    }

    private static int ExecuteScalarInt(DuckDBConnection connection, string sql, params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters)
        {
            command.Parameters.Add(p);
        }

        var value = command.ExecuteScalar();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static int ExecuteScalarInt(DuckDBConnection connection, System.Data.Common.DbTransaction tx, string sql, params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var p in parameters)
        {
            command.Parameters.Add(p);
        }

        var value = command.ExecuteScalar();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static int ExecuteNonQuery(DuckDBConnection connection, System.Data.Common.DbTransaction tx, string sql, params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var p in parameters)
        {
            command.Parameters.Add(p);
        }

        return command.ExecuteNonQuery();
    }
}
