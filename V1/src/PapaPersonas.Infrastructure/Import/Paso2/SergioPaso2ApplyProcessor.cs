using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DuckDB.NET.Data;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso2;

namespace PapaPersonas.Infrastructure.Import.Paso2;

/// <summary>
/// Process 2B apply service.
///
/// WHY: Process 2A only prepares staging + preview. Process 2B is the explicit confirmation step
/// that mutates personas, so this service must enforce run-state guards and transaction safety.
/// </summary>
public sealed class SergioPaso2ApplyProcessor
{
    private static readonly string[] WritableCanonicalFields =
    [
        "dni","fecha_nacimiento","sexo","tipo_dni","apellido","nombre","direccion","codigo_postal",
        "localidad","partido","provincia","nacionalidad","telefono_fijo_1","telefono_fijo_2","telefono_fijo_3",
        "telefono_fijo_4","telefono_fijo_5","celular_1","celular_2","celular_3","celular_4","celular_5",
        "whatsapp_1","whatsapp_2","whatsapp_3","whatsapp_4","whatsapp_5","email_1","email_2","email_3",
        "email_4","email_5","codigo_obra_social","obra_social","cuit_empleador","edad","anio"
    ];

    /// <summary>Aplica una importación confirmada con controles de estado, columnas autorizadas y transacción segura.</summary>
    public SergioApplyResult Apply(SergioApplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var notices = new List<ValidationIssue>();

        try
        {
            if (string.IsNullOrWhiteSpace(request.DatabasePath) || !File.Exists(request.DatabasePath))
            {
                return SergioApplyResult.ValidationFailed(
                    request.ImportId,
                    [new ValidationIssue(ValidationErrorCode.MissingRequiredHeader, "No se encontró el archivo de base de datos.")],
                    "No se encontró el archivo de base de datos.",
                    stopwatch.Elapsed);
            }

            using var connection = OpenConnection(request.DatabasePath);
            var run = GetImportRun(connection, request.ImportId);
            if (run is null)
            {
                return SergioApplyResult.ValidationFailed(
                    request.ImportId,
                    [new ValidationIssue(ValidationErrorCode.MissingRequiredHeader, "No se encontró la ejecución de importación.")],
                    "No se encontró la ejecución de importación.",
                    stopwatch.Elapsed);
            }

            if (!string.Equals(run.StageType, "sergio_return", StringComparison.OrdinalIgnoreCase))
            {
                return SergioApplyResult.ValidationFailed(
                    request.ImportId,
                    [new ValidationIssue(ValidationErrorCode.MissingRequiredHeader, "La etapa de la ejecución no es sergio_return.")],
                    "La etapa de la ejecución no es sergio_return.",
                    stopwatch.Elapsed);
            }

            if (!run.ImportDate.HasValue)
            {
                return SergioApplyResult.ValidationFailed(
                    request.ImportId,
                    [new ValidationIssue(ValidationErrorCode.InvalidDateValue, "La ejecución no tiene fecha_importacion.")],
                    "La ejecución no tiene fecha_importacion.",
                    stopwatch.Elapsed);
            }

            var parse = ParseWritableFields(run.SourceColumnsPresentJson, notices);
            if (parse.Errors.Count > 0)
            {
                return SergioApplyResult.ValidationFailed(
                    request.ImportId,
                    parse.Errors,
                    "Los metadatos de columnas de origen no son válidos para aplicar.",
                    stopwatch.Elapsed);
            }

            if (parse.WritableFields.Count == 0)
            {
                return SergioApplyResult.ValidationFailed(
                    request.ImportId,
                    [new ValidationIssue(ValidationErrorCode.MissingRequiredHeader, "No se encontraron campos editables en source_columns_present_json.")],
                    "No se encontraron campos editables en source_columns_present_json.",
                    stopwatch.Elapsed);
            }

            var rejectedCount = CountRejectedRows(connection, request.ImportId);
            var rejectedCsvPath = ExportRejectedCsvIfRequested(connection, request, rejectedCount);

            using var transaction = connection.BeginTransaction();
            try
            {
                // La transición a applying usa comparación y cambio atómicos para impedir aplicar dos veces la misma importación.
                var locked = TryTransitionReadyToApplying(connection, transaction, request.ImportId);
                if (!locked)
                {
                    transaction.Rollback();
                    return SergioApplyResult.ValidationFailed(
                        request.ImportId,
                        [new ValidationIssue(ValidationErrorCode.MissingRequiredHeader, "La ejecución está desactualizada o ya se está aplicando.")],
                        "La ejecución está desactualizada o ya se está aplicando.",
                        stopwatch.Elapsed);
                }

                var validCount = CountValidRows(connection, transaction, request.ImportId);
                var updatedRows = ExecuteUpdateExisting(connection, transaction, request.ImportId, parse.WritableFields, run.ImportDate.Value);
                var insertedRows = ExecuteInsertMissing(connection, transaction, request.ImportId, parse.WritableFields, run.ImportDate.Value);

                var appliedRows = updatedRows + insertedRows;

                // Si el total aplicado no coincide con staging, el rollback evita perder filas válidas en silencio.
                if (appliedRows != validCount)
                {
                    throw new InvalidOperationException("La cantidad de filas aplicadas no coincide con las filas válidas de staging.");
                }

                SetImportRunCompleted(
                    connection,
                    transaction,
                    request.ImportId,
                    insertedRows,
                    updatedRows,
                    validCount,
                    rejectedCount);

                transaction.Commit();

                return SergioApplyResult.Success(
                    request.ImportId,
                    new SergioApplySummary(appliedRows, insertedRows, updatedRows, rejectedCount),
                    rejectedCsvPath,
                    notices,
                    stopwatch.Elapsed);
            }
            catch
            {
                transaction.Rollback();
                TryMarkRunFailed(connection, request.ImportId, "La aplicación falló y se revirtió.");
                throw;
            }
        }
        catch (Exception ex)
        {
            return SergioApplyResult.Failed(
                request.ImportId,
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "La aplicación de Paso 2 falló. Verificá el estado y la consistencia de los datos, y volvé a intentar.",
                    ex),
                stopwatch.Elapsed,
                notices);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    // Interpreta los metadatos de columnas presentes y limita la actualización a campos autorizados.
    private static (HashSet<string> WritableFields, List<ValidationIssue> Errors) ParseWritableFields(
        string? sourceColumnsJson,
        List<ValidationIssue> notices)
    {
        var errors = new List<ValidationIssue>();
        var writable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(sourceColumnsJson))
        {
            errors.Add(new ValidationIssue(ValidationErrorCode.MissingRequiredHeader, "Falta source_columns_present_json."));
            return (writable, errors);
        }

        string[]? fields;
        try
        {
            fields = JsonSerializer.Deserialize<string[]>(sourceColumnsJson);
        }
        catch (JsonException)
        {
            errors.Add(new ValidationIssue(ValidationErrorCode.MissingRequiredHeader, "source_columns_present_json no contiene JSON válido."));
            return (writable, errors);
        }

        if (fields is null)
        {
            errors.Add(new ValidationIssue(ValidationErrorCode.MissingRequiredHeader, "source_columns_present_json está vacío."));
            return (writable, errors);
        }

        foreach (var raw in fields)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var field = raw.Trim();
            if (field.Equals("cuil", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!WritableCanonicalFields.Contains(field, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationIssue(
                    ValidationErrorCode.UnknownHeader,
                    $"El campo canónico '{field}' no está permitido en source_columns_present_json."));
                continue;
            }

            writable.Add(field);
        }

        return (writable, errors);
    }

    private static int ExecuteUpdateExisting(
        DuckDBConnection connection,
        DbTransaction transaction,
        Guid importId,
        IReadOnlyCollection<string> writableFields,
        DateOnly importDate)
    {
        var setClauses = string.Join(", ", writableFields.Select(f => $"{f} = s.{f}"));
        if (!string.IsNullOrWhiteSpace(setClauses))
        {
            setClauses += ", ";
        }

        var sql =
            $"""
            UPDATE personas AS p
            SET {setClauses}fecha_actualizacion = CURRENT_TIMESTAMP,
                fecha_importacion = $importDate,
                source_import_id = $importId,
                source_row_number = s.source_row_number
            FROM personas_staging AS s
            WHERE s.import_id = $importId
              AND s.validation_outcome = 'valid'
              AND p.cuil = s.cuil;
            """;

        return ExecuteNonQuery(
            connection,
            transaction,
            sql,
            new DuckDBParameter("importId", importId),
            new DuckDBParameter("importDate", importDate));
    }

    private static int ExecuteInsertMissing(
        DuckDBConnection connection,
        DbTransaction transaction,
        Guid importId,
        IReadOnlyCollection<string> writableFields,
        DateOnly importDate)
    {
        var insertColumns = new List<string> { "cuil" };
        insertColumns.AddRange(writableFields.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        insertColumns.Add("fecha_importacion");
        insertColumns.Add("source_import_id");
        insertColumns.Add("source_row_number");
        insertColumns.Add("fecha_actualizacion");

        var selectColumns = new List<string> { "s.cuil" };
        selectColumns.AddRange(writableFields.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(f => $"s.{f}"));
        selectColumns.Add("$importDate");
        selectColumns.Add("$importId");
        selectColumns.Add("s.source_row_number");
        selectColumns.Add("CURRENT_TIMESTAMP");

        var sql =
            $"""
            INSERT INTO personas ({string.Join(", ", insertColumns)})
            SELECT {string.Join(", ", selectColumns)}
            FROM personas_staging AS s
            LEFT JOIN personas AS p
              ON p.cuil = s.cuil
            WHERE s.import_id = $importId
              AND s.validation_outcome = 'valid'
              AND p.cuil IS NULL;
            """;

        return ExecuteNonQuery(
            connection,
            transaction,
            sql,
            new DuckDBParameter("importId", importId),
            new DuckDBParameter("importDate", importDate));
    }

    // Exporta los rechazos sólo cuando fueron solicitados y deja un CSV ordenado por fila de origen.
    private static string? ExportRejectedCsvIfRequested(
        DuckDBConnection connection,
        SergioApplyRequest request,
        int rejectedCount)
    {
        if (rejectedCount <= 0)
        {
            return null;
        }

        var explicitPath = request.RejectedCsvOutputPath;
        var outputDirectory = request.RejectedCsvOutputDirectory;
        if (string.IsNullOrWhiteSpace(explicitPath) && string.IsNullOrWhiteSpace(outputDirectory))
        {
            return null;
        }

        var outputPath = !string.IsNullOrWhiteSpace(explicitPath)
            ? explicitPath!
            : Path.Combine(outputDirectory!, $"sergio_import_{request.ImportId:D}_rejected.csv");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("La ruta del CSV rechazado no es válida."));

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT source_row_number, validation_error, cuil
            FROM personas_staging
            WHERE import_id = $importId
              AND validation_outcome = 'rejected'
            ORDER BY source_row_number;
            """;
        command.Parameters.Add(new DuckDBParameter("importId", request.ImportId));

        using var reader = command.ExecuteReader();
        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine("source_row_number,reason_code,normalized_cuil");
        while (reader.Read())
        {
            var sourceRow = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture);
            var reason = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var normalizedCuil = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

            writer.WriteLine($"{EscapeCsv(sourceRow)},{EscapeCsv(reason)},{EscapeCsv(normalizedCuil)}");
        }

        return outputPath;
    }

    private static string EscapeCsv(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static int CountValidRows(DuckDBConnection connection, DbTransaction transaction, Guid importId)
    {
        return ExecuteScalarInt(
            connection,
            transaction,
            "SELECT COUNT(*) FROM personas_staging WHERE import_id = $importId AND validation_outcome = 'valid';",
            new DuckDBParameter("importId", importId));
    }

    private static int CountRejectedRows(DuckDBConnection connection, Guid importId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM personas_staging WHERE import_id = $importId AND validation_outcome = 'rejected';";
        command.Parameters.Add(new DuckDBParameter("importId", importId));
        var value = command.ExecuteScalar();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static ImportRunState? GetImportRun(DuckDBConnection connection, Guid importId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT stage_type, status, source_columns_present_json
                 , fecha_importacion
            FROM import_runs
            WHERE import_id = $importId;
            """;
        command.Parameters.Add(new DuckDBParameter("importId", importId));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var stageType = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var status = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var sourceColumns = reader.IsDBNull(2) ? null : reader.GetString(2);
        DateOnly? importDate = reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3));

        return new ImportRunState(stageType, status, sourceColumns, importDate);
    }

    private static void SetImportRunStatus(
        DuckDBConnection connection,
        DbTransaction transaction,
        Guid importId,
        string status)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE import_runs SET status = $status WHERE import_id = $importId;",
            new DuckDBParameter("status", status),
            new DuckDBParameter("importId", importId));
    }

    private static bool TryTransitionReadyToApplying(
        DuckDBConnection connection,
        DbTransaction transaction,
        Guid importId)
    {
        var affected = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE import_runs
            SET status = 'applying'
            WHERE import_id = $importId
              AND status = 'ready_for_confirmation';
            """,
            new DuckDBParameter("importId", importId));

        return affected == 1;
    }

    private static void SetImportRunCompleted(
        DuckDBConnection connection,
        DbTransaction transaction,
        Guid importId,
        int insertedRows,
        int updatedRows,
        int validRows,
        int rejectedRows)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE import_runs
            SET status = 'completed',
                completed_utc = CURRENT_TIMESTAMP,
                valid_rows = $validRows,
                rejected_rows = $rejectedRows,
                rows_to_insert = $rowsToInsert,
                rows_to_update = $rowsToUpdate,
                error_message = NULL
            WHERE import_id = $importId;
            """,
            new DuckDBParameter("validRows", validRows),
            new DuckDBParameter("rejectedRows", rejectedRows),
            new DuckDBParameter("rowsToInsert", insertedRows),
            new DuckDBParameter("rowsToUpdate", updatedRows),
            new DuckDBParameter("importId", importId));
    }

    private static void TryMarkRunFailed(DuckDBConnection connection, Guid importId, string errorMessage)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE import_runs
            SET status = 'failed',
                analyzed_utc = CURRENT_TIMESTAMP,
                error_message = $errorMessage
            WHERE import_id = $importId;
            """;
        command.Parameters.Add(new DuckDBParameter("errorMessage", errorMessage));
        command.Parameters.Add(new DuckDBParameter("importId", importId));
        command.ExecuteNonQuery();
    }

    private static DuckDBConnection OpenConnection(string databasePath)
    {
        var builder = new DuckDBConnectionStringBuilder { DataSource = databasePath };
        var connection = new DuckDBConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static int ExecuteScalarInt(
        DuckDBConnection connection,
        DbTransaction transaction,
        string sql,
        params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
        DbTransaction transaction,
        string sql,
        params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command.ExecuteNonQuery();
    }

    private sealed record ImportRunState(string StageType, string Status, string? SourceColumnsPresentJson, DateOnly? ImportDate);
}
