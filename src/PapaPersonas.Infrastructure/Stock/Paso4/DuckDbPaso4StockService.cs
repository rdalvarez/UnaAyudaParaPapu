using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Stock.Paso4;
using PapaPersonas.Infrastructure.Csv;
using PapaPersonas.Infrastructure.Query.Paso3;

namespace PapaPersonas.Infrastructure.Stock.Paso4;

public sealed class DuckDbPaso4StockService : IPaso4StockService
{
    private readonly IPaso4ExtractionService _extractionService;

    public DuckDbPaso4StockService()
        : this(new DuckDbPaso4ExtractionService())
    {
    }

    internal DuckDbPaso4StockService(IPaso4ExtractionService extractionService)
    {
        _extractionService = extractionService;
    }

    public Paso4StockOverview GetOverview(string databasePath)
    {
        EnsureDatabaseExists(databasePath);

        using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);

        var dates = QueryAvailableDates(connection);
        var latestSuccessful = QueryLatestSuccessfulImport(connection);
        var currentHeader = QueryCurrentStockHeader(connection, latestSuccessful);

        return new Paso4StockOverview(dates, currentHeader, latestSuccessful.ImportDate, latestSuccessful.ImportId);
    }

    /// <summary>Genera o regenera un snapshot de stock transaccional para la fecha seleccionada.</summary>
    public Paso4GenerateStockResult GenerateOrRegenerateStock(Paso4GenerateStockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureDatabaseExists(request.DatabasePath);

        try
        {
            if (_extractionService.GetPendingState(request.DatabasePath) is not null)
            {
                return Paso4GenerateStockResult.ValidationFailed("El stock actual tiene una extracción pendiente.");
            }

            using var connection = DuckDbPaso3QueryService.OpenConnection(request.DatabasePath);
            // La generación y regeneración se ejecutan en una única transacción para que el encabezado y sus integrantes representen el mismo snapshot.
            using var transaction = connection.BeginTransaction();

            // Se bloquea la regeneración si el stock actual conserva una extracción pendiente, para no reemplazar datos protegidos por esa operación.
            if (CurrentStockHasPendingToken(connection, transaction))
            {
                transaction.Rollback();
                return Paso4GenerateStockResult.ValidationFailed("El stock actual tiene una extracción pendiente.");
            }

            // Confirma que la fecha elegida tenga personas cargadas antes de construir el snapshot.
            if (!SelectedDateExists(connection, transaction, request.SelectedFechaImportacion))
            {
                transaction.Rollback();
                return Paso4GenerateStockResult.ValidationFailed("No se encontró la fecha_importacion seleccionada en personas.");
            }

            // Identifica la importación completada más reciente de la fecha elegida para conservar la trazabilidad del snapshot.
            var latestImportForDate = QueryLatestSuccessfulImportForDate(connection, transaction, request.SelectedFechaImportacion);

            // Busca el stock generado más recientemente para decidir si la operación reemplaza su contenido o crea el primer snapshot.
            var currentStockId = QueryCurrentStockId(connection, transaction);
            var stockId = currentStockId ?? Guid.NewGuid();

            if (currentStockId.HasValue)
            {
                // Al regenerar, elimina los integrantes anteriores para reemplazarlos íntegramente por la selección nueva.
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM stock_members WHERE stock_id = $stockId;",
                    new DuckDBParameter("stockId", currentStockId.Value));

                // Actualiza el encabezado existente con el origen elegido y deja el snapshot sin una extracción pendiente.
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE stock_headers
                    SET source_fecha_importacion = $date,
                        source_import_id = $importId,
                        generated_utc = CURRENT_TIMESTAMP,
                        pending_extraction_token = NULL,
                        pending_started_utc = NULL,
                        pending_output_path = NULL,
                        pending_expected_rows = NULL,
                        pending_selected_columns_json = NULL
                    WHERE stock_id = $stockId;
                    """,
                    new DuckDBParameter("stockId", currentStockId.Value),
                    new DuckDBParameter("date", request.SelectedFechaImportacion),
                    new DuckDBParameter("importId", (object?)latestImportForDate ?? DBNull.Value));
            }
            else
            {
                // Crea el encabezado del primer snapshot y registra la fecha elegida y la importación completada cuando está disponible.
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "INSERT INTO stock_headers (stock_id, source_fecha_importacion, source_import_id) VALUES ($stockId, $date, $importId);",
                    new DuckDBParameter("stockId", stockId),
                    new DuckDBParameter("date", request.SelectedFechaImportacion),
                    new DuckDBParameter("importId", (object?)latestImportForDate ?? DBNull.Value));
            }

            // Cuenta las personas sin orden de origen para informar cuántas recibirán un orden de compatibilidad durante la carga.
            var fallbackCount = ExecuteScalarInt(
                connection,
                transaction,
                "SELECT COUNT(*) FROM personas WHERE fecha_importacion = $date AND source_row_number IS NULL;",
                new DuckDBParameter("date", request.SelectedFechaImportacion));

            // Construye el snapshot de la fecha elegida, conservando el orden de origen y asignando un orden determinista a filas sin ese metadato.
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta, extraction_token)
                WITH selected_personas AS (
                    SELECT
                        cuil,
                        codigo_obra_social,
                        obra_social,
                        source_row_number,
                        ROW_NUMBER() OVER (ORDER BY cuil ASC) AS cuil_order
                    FROM personas
                    WHERE fecha_importacion = $date
                ),
                max_source AS (
                    SELECT COALESCE(MAX(source_row_number), 0) AS max_source_row_number
                    FROM selected_personas
                    WHERE source_row_number IS NOT NULL
                )
                SELECT
                    $stockId,
                    p.cuil,
                    p.codigo_obra_social,
                    p.obra_social,
                    CASE
                        WHEN p.source_row_number IS NOT NULL THEN p.source_row_number
                        ELSE m.max_source_row_number + p.cuil_order
                    END AS source_order,
                    FALSE,
                    NULL,
                    NULL
                FROM selected_personas AS p
                CROSS JOIN max_source AS m
                ORDER BY source_order, p.cuil;
                """,
                new DuckDBParameter("stockId", stockId),
                new DuckDBParameter("date", request.SelectedFechaImportacion));

            // Verifica cuántos integrantes quedaron asociados al snapshot para devolver el resultado de generación.
            var insertedCount = ExecuteScalarInt(
                connection,
                transaction,
                "SELECT COUNT(*) FROM stock_members WHERE stock_id = $stockId;",
                new DuckDBParameter("stockId", stockId));

            transaction.Commit();

            return Paso4GenerateStockResult.Completed(
                stockId,
                request.SelectedFechaImportacion,
                latestImportForDate,
                insertedCount,
                fallbackCount);
        }
        catch (Exception ex)
        {
            return Paso4GenerateStockResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo generar el stock. Verificá la base y volvé a intentar.",
                    ex));
        }
    }

    public Paso4StockSummaryResult GetCurrentStockSummary(string databasePath)
    {
        EnsureDatabaseExists(databasePath);

        using var connection = DuckDbPaso3QueryService.OpenConnection(databasePath);
        var current = QueryCurrentStockHeader(connection, QueryLatestSuccessfulImport(connection));
        if (current is null)
        {
            throw new InvalidOperationException("No existe un stock actual.");
        }

        var groups = QuerySummaryGroups(connection, current.StockId);
        var headerWithGroupCount = current with { GroupCount = groups.Count };
        return new Paso4StockSummaryResult(headerWithGroupCount, groups);
    }

    public async Task<Paso4SummaryExportResult> ExportSummaryCsvAsync(Paso4SummaryExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var summary = GetCurrentStockSummary(request.DatabasePath);
            var rows = await CsvExportFileWriter.WriteCsvAsync(
                request.DestinationCsvPath,
                [
                    "ORDEN",
                    "COD_O_SOCIAL",
                    "DESCRIPCION_O_SOCIAL",
                    "CANTIDAD",
                    "CANTIDAD_VENDIDA",
                    "CANTIDAD_DISPONIBLE",
                    "FECHA_CARGA"
                ],
                async (writer, token) =>
                {
                    foreach (var row in summary.Groups)
                    {
                        await CsvExportFileWriter.WriteRowAsync(
                            writer,
                            [
                                row.Orden.ToString(CultureInfo.InvariantCulture),
                                row.NormalizedCodigoObraSocial,
                                row.NormalizedObraSocial,
                                row.Total.ToString(CultureInfo.InvariantCulture),
                                row.Sold.ToString(CultureInfo.InvariantCulture),
                                row.Available.ToString(CultureInfo.InvariantCulture),
                                summary.Header.SourceFechaImportacion.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            ],
                            token);
                    }

                    return summary.Groups.Count;
                },
                cancellationToken);

            return Paso4SummaryExportResult.Completed(rows, request.DestinationCsvPath);
        }
        catch (Exception ex)
        {
            return Paso4SummaryExportResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo exportar el resumen de stock.",
                    ex));
        }
    }

    /// <summary>Exporta el snapshot completo con las columnas permitidas y el filtro opcional de disponibilidad.</summary>
    public async Task<Paso4FullExportResult> ExportFullCsvAsync(Paso4FullExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            EnsureDatabaseExists(request.DatabasePath);

            using var connection = DuckDbPaso3QueryService.OpenConnection(request.DatabasePath);
            var current = QueryCurrentStockHeader(connection, QueryLatestSuccessfulImport(connection));
            if (current is null)
            {
                return Paso4FullExportResult.Failed("No existe un stock actual.");
            }

            var selected = Paso4ColumnCatalog.NormalizeSelectedColumns(request.SelectedColumns);
            if (!request.OnlyAvailable)
            {
                selected = [.. selected, "VENDIDO"];
            }

            var selectList = string.Join(", ", selected.Select(column => $"{Paso4ColumnCatalog.SelectExpressions[column]} AS {column}"));
            var whereExtra = request.OnlyAvailable ? "AND sm.vendido = FALSE" : string.Empty;

            // Consulta las filas del snapshot actual con las columnas permitidas y, si corresponde, sólo las aún disponibles para el archivo completo.
            var sql =
                $"""
                SELECT {selectList}
                FROM stock_members AS sm
                LEFT JOIN personas AS p ON p.cuil = sm.cuil
                WHERE sm.stock_id = $stockId
                {whereExtra}
                ORDER BY sm.source_order ASC, sm.cuil ASC;
                """;

            var rows = await CsvExportFileWriter.WriteCsvAsync(
                request.DestinationCsvPath,
                selected,
                async (writer, token) =>
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.Add(new DuckDBParameter("stockId", current.StockId));

                    using var reader = command.ExecuteReader();
                    var rowsWritten = 0;
                    while (reader.Read())
                    {
                        token.ThrowIfCancellationRequested();
                        var values = new string[reader.FieldCount];
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            values[i] = CsvExportFileWriter.FormatValue(reader.GetValue(i));
                        }

                        await CsvExportFileWriter.WriteRowAsync(writer, values, token);
                        rowsWritten++;
                    }

                    return rowsWritten;
                },
                cancellationToken);

            return Paso4FullExportResult.Completed(rows, request.DestinationCsvPath);
        }
        catch (Exception ex)
        {
            return Paso4FullExportResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo exportar el stock completo.",
                    ex));
        }
    }

    private static List<Paso4DateOption> QueryAvailableDates(DuckDBConnection connection)
    {
        // Ofrece las cinco fechas de importación más recientes disponibles para seleccionar el origen del stock.
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT fecha_importacion
            FROM personas
            WHERE fecha_importacion IS NOT NULL
            ORDER BY fecha_importacion DESC
            LIMIT 5;
            """;

        using var reader = command.ExecuteReader();
        var result = new List<Paso4DateOption>();
        while (reader.Read())
        {
            var date = reader.GetFieldValue<DateTime>(0);
            result.Add(new Paso4DateOption(DateOnly.FromDateTime(date)));
        }

        return result;
    }

    private static (DateOnly? ImportDate, Guid? ImportId) QueryLatestSuccessfulImport(DuckDBConnection connection)
    {
        // Identifica la importación completada más reciente para comparar la actualidad del stock con su fuente.
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT fecha_importacion, import_id
            FROM import_runs
            WHERE stage_type = 'sergio_return'
              AND status = 'completed'
              AND fecha_importacion IS NOT NULL
            ORDER BY completed_utc DESC NULLS LAST, started_utc DESC NULLS LAST, import_id DESC
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return (null, null);
        }

        var date = DateOnly.FromDateTime(reader.GetDateTime(0));
        var importId = reader.GetGuid(1);
        return (date, importId);
    }

    private static Guid? QueryLatestSuccessfulImportForDate(DuckDBConnection connection, System.Data.Common.DbTransaction tx, DateOnly date)
    {
        // Obtiene la última importación completada de la fecha elegida para mantener la referencia de origen del snapshot.
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT import_id
            FROM import_runs
            WHERE stage_type = 'sergio_return'
              AND status = 'completed'
              AND fecha_importacion = $date
            ORDER BY completed_utc DESC NULLS LAST, started_utc DESC NULLS LAST, import_id DESC
            LIMIT 1;
            """;
        command.Parameters.Add(new DuckDBParameter("date", date));

        var value = command.ExecuteScalar();
        return value is null || value is DBNull ? null : (Guid)value;
    }

    private static Paso4StockHeaderSummary? QueryCurrentStockHeader(
        DuckDBConnection connection,
        (DateOnly? ImportDate, Guid? ImportId) latestSuccessful)
    {
        // Recupera el stock generado más recientemente, sus conteos y su estado de extracción para mostrar el estado actual y detectar desactualización.
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                h.stock_id,
                h.source_fecha_importacion,
                h.source_import_id,
                h.generated_utc,
                h.pending_extraction_token,
                (SELECT COUNT(*) FROM stock_members WHERE stock_id = h.stock_id) AS total_members,
                (SELECT COUNT(*) FROM stock_members WHERE stock_id = h.stock_id AND vendido = TRUE) AS sold_members
            FROM stock_headers AS h
            ORDER BY h.generated_utc DESC, h.stock_id DESC
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var stockId = reader.GetGuid(0);
        var sourceDate = DateOnly.FromDateTime(reader.GetDateTime(1));
        Guid? sourceImportId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
        var generatedUtc = reader.GetDateTime(3);
        var hasPending = !reader.IsDBNull(4);
        var total = reader.GetInt32(5);
        var sold = reader.GetInt32(6);
        var available = total - sold;

        var staleByDate = latestSuccessful.ImportDate.HasValue && sourceDate != latestSuccessful.ImportDate.Value;
        var staleByImportId = sourceImportId.HasValue && latestSuccessful.ImportId.HasValue && sourceImportId.Value != latestSuccessful.ImportId.Value;

        return new Paso4StockHeaderSummary(
            stockId,
            sourceDate,
            sourceImportId,
            generatedUtc,
            hasPending,
            staleByDate || staleByImportId,
            total,
            sold,
            available,
            GroupCount: 0);
    }

    private static List<Paso4StockGroupSummaryRow> QuerySummaryGroups(DuckDBConnection connection, Guid stockId)
    {
        // Resume el stock por código y obra social normalizados, conservando valores de presentación y calculando vendidos y disponibles.
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                UPPER(TRIM(COALESCE(codigo_obra_social, ''))) AS normalized_codigo,
                UPPER(TRIM(COALESCE(obra_social, ''))) AS normalized_obra,
                COUNT(*) AS total,
                SUM(CASE WHEN vendido THEN 1 ELSE 0 END) AS sold,
                MIN(COALESCE(codigo_obra_social, '')) AS raw_codigo,
                MIN(COALESCE(obra_social, '')) AS raw_obra
            FROM stock_members
            WHERE stock_id = $stockId
            GROUP BY 1, 2
            ORDER BY
                CASE WHEN UPPER(TRIM(COALESCE(codigo_obra_social, ''))) = '' THEN 1 ELSE 0 END,
                UPPER(TRIM(COALESCE(codigo_obra_social, ''))) ASC,
                UPPER(TRIM(COALESCE(obra_social, ''))) ASC;
            """;
        command.Parameters.Add(new DuckDBParameter("stockId", stockId));

        using var reader = command.ExecuteReader();
        var rows = new List<Paso4StockGroupSummaryRow>();
        var index = 1;
        while (reader.Read())
        {
            var normalizedCode = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var normalizedObra = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var total = reader.GetInt32(2);
            var sold = reader.GetInt32(3);
            var rawCode = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var rawObra = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);

            rows.Add(new Paso4StockGroupSummaryRow(
                index,
                rawCode,
                rawObra,
                normalizedCode,
                normalizedObra,
                total,
                sold,
                total - sold));

            index++;
        }

        return rows;
    }

    private static bool CurrentStockHasPendingToken(DuckDBConnection connection, System.Data.Common.DbTransaction tx)
    {
        // Comprueba si existe una extracción pendiente que deba protegerse antes de regenerar el stock.
        var count = ExecuteScalarInt(connection, tx, "SELECT COUNT(*) FROM stock_headers WHERE pending_extraction_token IS NOT NULL;");
        return count > 0;
    }

    private static bool SelectedDateExists(DuckDBConnection connection, System.Data.Common.DbTransaction tx, DateOnly selectedDate)
    {
        // Comprueba que la fecha seleccionada tenga personas cargadas y, por tanto, pueda originar un snapshot.
        var count = ExecuteScalarInt(
            connection,
            tx,
            "SELECT COUNT(*) FROM personas WHERE fecha_importacion = $date;",
            new DuckDBParameter("date", selectedDate));
        return count > 0;
    }

    private static Guid? QueryCurrentStockId(DuckDBConnection connection, System.Data.Common.DbTransaction tx)
    {
        // Obtiene el snapshot generado más recientemente para reutilizarlo durante una regeneración.
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT stock_id FROM stock_headers ORDER BY generated_utc DESC, stock_id DESC LIMIT 1;";
        var value = command.ExecuteScalar();
        return value is null || value is DBNull ? null : (Guid)value;
    }

    private static void EnsureDatabaseExists(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            throw new ArgumentException("No se encontró el archivo de base de datos.", nameof(databasePath));
        }
    }

    private static int ExecuteScalarInt(
        DuckDBConnection connection,
        System.Data.Common.DbTransaction tx,
        string sql,
        params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var value = command.ExecuteScalar();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static int ExecuteNonQuery(
        DuckDBConnection connection,
        System.Data.Common.DbTransaction tx,
        string sql,
        params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command.ExecuteNonQuery();
    }
}
