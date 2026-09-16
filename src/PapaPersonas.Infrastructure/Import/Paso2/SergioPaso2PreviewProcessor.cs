using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DuckDB.NET.Data;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso2;
using PapaPersonas.Infrastructure.Database;

namespace PapaPersonas.Infrastructure.Import.Paso2;

/// <summary>
/// Process 2A analyzer:
    /// - conservative Sergio header validation with explicit unknown-header confirmation,
/// - streaming load into personas_staging,
/// - duplicate rejection and preview counts.
///
/// INTENT: this unit prepares a safe, reviewable snapshot in staging and import_runs
/// before ANY change to personas. Future Process 2B consumes this snapshot after user confirmation.
/// </summary>
public sealed class SergioPaso2PreviewProcessor
{
    private const double OaDateMin = -657435d;
    private const double OaDateMax = 2958465.99999999d;

    private readonly ISergioRowSource _rowSource;

    public SergioPaso2PreviewProcessor()
        : this(new ExcelDataReaderSergioRowSource())
    {
    }

    public SergioPaso2PreviewProcessor(ISergioRowSource rowSource)
    {
        _rowSource = rowSource;
    }

    /// <summary>Analiza y valida el archivo de Sergio en staging antes de permitir su aplicación.</summary>
    public SergioStagePreviewResult Analyze(SergioStagePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        Guid? importId = null;
        var failureNotices = new List<ValidationIssue>();
        SourceSnapshot? sourceSnapshot = null;

        try
        {
            if (string.IsNullOrWhiteSpace(request.SergioXlsxPath) || !File.Exists(request.SergioXlsxPath))
            {
                return SergioStagePreviewResult.Failed(
                    "No se encontró el archivo de entrada.",
                    stopwatch.Elapsed);
            }

            if (string.IsNullOrWhiteSpace(request.DatabasePath) || !File.Exists(request.DatabasePath))
            {
                return SergioStagePreviewResult.Failed(
                    "No se encontró el archivo de base de datos.",
                    stopwatch.Elapsed);
            }

            sourceSnapshot = request.AllowUnknownHeaders
                ? OpenApprovedSnapshot(request)
                : CreateSourceSnapshot(request.SergioXlsxPath);

            var headers = sourceSnapshot.Headers;
            var sourceShapeFingerprint = HeaderContractValidator.GetSourceShapeFingerprint(headers);
            var sourceIdentity = sourceSnapshot.Identity with { SourceShapeFingerprint = sourceShapeFingerprint };
            var strictValidation = HeaderContractValidator.Validate(ImportStage.SergioReturn, headers);
            var structuralIssues = strictValidation.Issues
                .Where(issue => issue.Code != ValidationErrorCode.UnknownHeader
                    || issue.Header is null
                    || !strictValidation.UnknownSourceHeaders.Contains(issue.Header, StringComparer.Ordinal))
                .ToArray();

            if (structuralIssues.Length > 0)
            {
                return SergioStagePreviewResult.ValidationFailed(
                    strictValidation.Issues,
                    strictValidation.Notices,
                    strictValidation.PresentCanonicalFields,
                    stopwatch.Elapsed);
            }

            if (strictValidation.UnknownSourceHeaders.Count > 0 && !request.AllowUnknownHeaders)
            {
                sourceSnapshot.KeepForConfirmation = true;
                return SergioStagePreviewResult.ConfirmationRequired(
                    strictValidation.Issues,
                    strictValidation.Notices,
                    strictValidation.PresentCanonicalFields,
                    strictValidation.UnknownSourceHeaders,
                    sourceShapeFingerprint,
                    sourceIdentity,
                    sourceSnapshot.SnapshotPath,
                    stopwatch.Elapsed);
            }

            var headerValidation = HeaderContractValidator.Validate(
                ImportStage.SergioReturn,
                headers,
                allowUnknownHeaders: request.AllowUnknownHeaders);

            if (!headerValidation.IsValid)
            {
                return SergioStagePreviewResult.ValidationFailed(
                    headerValidation.Issues,
                    headerValidation.Notices,
                    headerValidation.PresentCanonicalFields,
                    stopwatch.Elapsed);
            }

            using var connection = OpenConnection(request.DatabasePath);
            importId = Guid.NewGuid();

            var sourceColumnsJson = BuildSourceColumnsJson(headerValidation.PresentCanonicalFields);
            InsertImportRunAnalyzing(connection, importId.Value, request.SergioXlsxPath, sourceColumnsJson, request.ImportDate);

            int totalRows = 0;
            int missingCuilRows = 0;
            int malformedCuilRows = 0;
            int invalidTypedValueRows = 0;

            using var transaction = connection.BeginTransaction();
            try
            {
                // El appender permite ingerir grandes volúmenes en una sola transacción sin perder la validación por fila.
                using var appender = connection.CreateAppender("personas_staging");

                foreach (var row in _rowSource.ReadRows(sourceSnapshot.SnapshotPath, headerValidation))
                {
                    totalRows++;

                    var cuilValidation = CuilValidator.Validate(row.RawCuil, row.SourceRowNumber);
                    var validationOutcome = "valid";
                    string? validationError = null;
                    var normalizedCuil = default(string);

                    if (!cuilValidation.IsValid)
                    {
                        validationOutcome = "rejected";
                        validationError = cuilValidation.Issue!.Code.ToString();
                        if (cuilValidation.Issue.Code == ValidationErrorCode.MissingCuil)
                        {
                            missingCuilRows++;
                        }
                        else
                        {
                            malformedCuilRows++;
                        }
                    }
                    else
                    {
                        normalizedCuil = cuilValidation.NormalizedCuil;

                        // La validación de tipos rechaza sólo la fila inválida y no interrumpe toda la vista previa.
                        if (!TryParseDateOrNull(row.GetValue("fecha_nacimiento"), out _)
                            || !TryParseSmallIntOrNull(row.GetValue("edad"), out _)
                            || !TryParseSmallIntOrNull(row.GetValue("anio"), out _))
                        {
                            validationOutcome = "rejected";
                            validationError = GuessTypedErrorCode(row).ToString();
                            invalidTypedValueRows++;
                        }
                    }

                    AppendStagingRow(
                        appender,
                        importId.Value,
                        row,
                        normalizedCuil,
                        validationOutcome,
                        validationError);
                }

                appender.Close();

                MarkDuplicateCuilRowsRejected(connection, transaction, importId.Value);

                var duplicateCuilRows = ExecuteScalarInt(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM personas_staging WHERE import_id = $importId AND validation_error = 'DuplicateCuilInBatch';",
                    new DuckDBParameter("importId", importId.Value));

                var validRows = ExecuteScalarInt(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM personas_staging WHERE import_id = $importId AND validation_outcome = 'valid';",
                    new DuckDBParameter("importId", importId.Value));

                var rejectedRows = totalRows - validRows;

                var rowsToUpdate = ExecuteScalarInt(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM personas_staging s
                    WHERE s.import_id = $importId
                      AND s.validation_outcome = 'valid'
                      AND EXISTS (SELECT 1 FROM personas p WHERE p.cuil = s.cuil);
                    """,
                    new DuckDBParameter("importId", importId.Value));

                var rowsToInsert = ExecuteScalarInt(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM personas_staging s
                    WHERE s.import_id = $importId
                      AND s.validation_outcome = 'valid'
                      AND NOT EXISTS (SELECT 1 FROM personas p WHERE p.cuil = s.cuil);
                    """,
                    new DuckDBParameter("importId", importId.Value));

                UpdateImportRunReadyForConfirmation(
                    connection,
                    transaction,
                    importId.Value,
                    totalRows,
                    validRows,
                    rejectedRows,
                    missingCuilRows,
                    malformedCuilRows,
                    duplicateCuilRows,
                    rowsToInsert,
                    rowsToUpdate);

                transaction.Commit();

                var summary = new SergioStagePreviewSummary(
                    totalRows,
                    validRows,
                    rejectedRows,
                    missingCuilRows,
                    malformedCuilRows,
                    duplicateCuilRows,
                    rowsToInsert,
                    rowsToUpdate,
                    invalidTypedValueRows);

                return SergioStagePreviewResult.Success(
                    importId.Value,
                    summary,
                    headerValidation.Notices,
                    headerValidation.PresentCanonicalFields,
                    stopwatch.Elapsed,
                    headerValidation.UnknownSourceHeaders,
                    sourceShapeFingerprint);
            }
            catch
            {
                transaction.Rollback();

                if (!TryMarkImportRunFailed(connection, importId.Value, "El análisis de vista previa falló antes de la confirmación.", out var diagnostic))
                {
                    failureNotices.Add(new ValidationIssue(
                        ValidationErrorCode.ImportRunStatusUpdateFailed,
                        diagnostic ?? "No se pudo actualizar el estado fallido de la importación."));
                }

                throw;
            }
        }
        catch (SourceSnapshotMismatchException)
        {
            return SergioStagePreviewResult.ValidationFailed(
                [new ValidationIssue(ValidationErrorCode.SourceShapeMismatch, "El archivo de origen cambió desde el análisis. Ejecutá Analizar nuevamente.")],
                [],
                [],
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            return SergioStagePreviewResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "La vista previa de Paso 2 falló. Verificá el formato del archivo y la base, y volvé a intentar.",
                    ex),
                stopwatch.Elapsed,
                importId,
                notices: failureNotices);
        }
        finally
        {
            if (sourceSnapshot is not null && !sourceSnapshot.KeepForConfirmation)
            {
                CleanupSnapshot(sourceSnapshot.SnapshotPath);
            }
            else if (sourceSnapshot is null && request.AllowUnknownHeaders)
            {
                CleanupSnapshot(request.SourceSnapshotPath);
            }

            stopwatch.Stop();
        }
    }

    // Crea un snapshot privado e inmutable para que el staging consuma exactamente los bytes preflightados.
    private SourceSnapshot CreateSourceSnapshot(string sourcePath)
    {
        var normalizedPath = Path.GetFullPath(sourcePath);
        var snapshotDirectory = Path.Combine(Path.GetTempPath(), "PapaPersonas", "SergioSnapshots");
        Directory.CreateDirectory(snapshotDirectory);
        var snapshotPath = Path.Combine(snapshotDirectory, $"{Guid.NewGuid():N}.xlsx");

        try
        {
            string contentHash;
            using (var source = new FileStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var snapshot = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                contentHash = CopyAndHash(source, snapshot);
                snapshot.Flush(flushToDisk: true);
            }

            var headers = _rowSource.ReadHeaders(snapshotPath);
            return new SourceSnapshot(
                snapshotPath,
                new SergioSourceIdentity(normalizedPath, contentHash, HeaderContractValidator.GetSourceShapeFingerprint(headers)),
                headers);
        }
        catch
        {
            CleanupSnapshot(snapshotPath);
            throw;
        }
    }

    // Revalida ruta, bytes y forma antes de consumir el snapshot aprobado por el usuario.
    private SourceSnapshot OpenApprovedSnapshot(SergioStagePreviewRequest request)
    {
        if (request.ExpectedSourceIdentity is null || string.IsNullOrWhiteSpace(request.SourceSnapshotPath))
        {
            throw new SourceSnapshotMismatchException("La aprobación de columnas desconocidas no contiene una identidad de origen completa.");
        }

        var expected = request.ExpectedSourceIdentity;
        var current = CaptureSourceIdentity(request.SergioXlsxPath);
        if (!string.Equals(current.NormalizedAbsolutePath, expected.NormalizedAbsolutePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.ContentHash, expected.ContentHash, StringComparison.Ordinal)
            || !string.Equals(current.SourceShapeFingerprint, expected.SourceShapeFingerprint, StringComparison.Ordinal))
        {
            throw new SourceSnapshotMismatchException("El archivo cambió desde la confirmación de columnas desconocidas. Ejecutá Analizar nuevamente.");
        }

        var snapshotPath = Path.GetFullPath(request.SourceSnapshotPath);
        if (!IsSnapshotPath(snapshotPath) || !File.Exists(snapshotPath))
        {
            throw new SourceSnapshotMismatchException("El snapshot aprobado ya no está disponible. Ejecutá Analizar nuevamente.");
        }

        var snapshotHeaders = _rowSource.ReadHeaders(snapshotPath);
        var snapshotHash = ComputeFileHash(snapshotPath);
        var snapshotShape = HeaderContractValidator.GetSourceShapeFingerprint(snapshotHeaders);
        if (!string.Equals(snapshotHash, expected.ContentHash, StringComparison.Ordinal)
            || !string.Equals(snapshotShape, expected.SourceShapeFingerprint, StringComparison.Ordinal))
        {
            throw new SourceSnapshotMismatchException("El snapshot aprobado fue alterado. Ejecutá Analizar nuevamente.");
        }

        return new SourceSnapshot(snapshotPath, expected, snapshotHeaders);
    }

    private SergioSourceIdentity CaptureSourceIdentity(string sourcePath)
    {
        var normalizedPath = Path.GetFullPath(sourcePath);
        using var source = new FileStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var contentHash = ComputeStreamHash(source);
        var headers = _rowSource.ReadHeaders(normalizedPath);
        return new SergioSourceIdentity(
            normalizedPath,
            contentHash,
            HeaderContractValidator.GetSourceShapeFingerprint(headers));
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ComputeStreamHash(stream);
    }

    private static string ComputeStreamHash(Stream stream)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 128];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string CopyAndHash(Stream source, Stream destination)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 128];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static void CleanupSnapshot(string? snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(snapshotPath);
            if (IsSnapshotPath(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            // La limpieza temporal es de mejor esfuerzo.
        }
    }

    private static bool IsSnapshotPath(string path)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PapaPersonas", "SergioSnapshots")) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SourceSnapshot
    {
        public SourceSnapshot(string snapshotPath, SergioSourceIdentity identity, IReadOnlyList<string> headers)
        {
            SnapshotPath = snapshotPath;
            Identity = identity;
            Headers = headers;
        }

        public string SnapshotPath { get; }
        public SergioSourceIdentity Identity { get; }
        public IReadOnlyList<string> Headers { get; }
        public bool KeepForConfirmation { get; set; }
    }

    private sealed class SourceSnapshotMismatchException : Exception
    {
        public SourceSnapshotMismatchException(string message)
            : base(message)
        {
        }
    }

    // Marca como rechazadas todas las filas que repiten un CUIL normalizado dentro de la importación.
    private static void MarkDuplicateCuilRowsRejected(DuckDBConnection connection, DbTransaction transaction, Guid importId)
    {
        // La detección usa CUIL normalizados estructuralmente válidos de todas las filas y rechaza cada repetición.
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE personas_staging AS s
            SET validation_outcome = 'rejected',
                validation_error = 'DuplicateCuilInBatch'
            FROM (
                SELECT cuil
                FROM personas_staging
                WHERE import_id = $importId
                  AND cuil IS NOT NULL
                GROUP BY cuil
                HAVING COUNT(*) > 1
            ) duplicates
            WHERE s.import_id = $importId
              AND s.cuil = duplicates.cuil;
            """,
            new DuckDBParameter("importId", importId));
    }

    private static ValidationErrorCode GuessTypedErrorCode(SergioRowRecord row)
    {
        if (!TryParseDateOrNull(row.GetValue("fecha_nacimiento"), out _))
        {
            return ValidationErrorCode.InvalidDateValue;
        }

        return ValidationErrorCode.InvalidSmallIntValue;
    }

    private static bool TryParseDateOrNull(string? rawValue, out DateTime? parsed)
    {
        parsed = null;
        var normalized = NormalizeText(rawValue);
        if (normalized is null)
        {
            return true;
        }

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var oaSerial))
        {
            if (oaSerial is < OaDateMin or > OaDateMax)
            {
                return false;
            }

            try
            {
                parsed = DateTime.FromOADate(oaSerial).Date;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyyMMdd", "MM/dd/yyyy", "M/d/yyyy" };
        if (DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            parsed = exact.Date;
            return true;
        }

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            parsed = parsedDate.Date;
            return true;
        }

        return false;
    }

    private static bool TryParseSmallIntOrNull(string? rawValue, out short? parsed)
    {
        parsed = null;
        var normalized = NormalizeText(rawValue);
        if (normalized is null)
        {
            return true;
        }

        if (short.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            parsed = value;
            return true;
        }

        return false;
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static DuckDBConnection OpenConnection(string databasePath)
    {
        var builder = new DuckDBConnectionStringBuilder
        {
            DataSource = databasePath
        };

        var connection = new DuckDBConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    // Conserva en JSON las columnas presentes para distinguir ausencia de celda vacía durante el apply.
    private static string BuildSourceColumnsJson(IReadOnlyCollection<string> presentCanonicalFields)
    {
        var ordered = presentCanonicalFields
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(ordered);
    }

    private static void InsertImportRunAnalyzing(
        DuckDBConnection connection,
        Guid importId,
        string sourceFilePath,
        string sourceColumnsJson,
        DateOnly importDate)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO import_runs (
                import_id,
                stage_type,
                source_file_name,
                source_file_path,
                status,
                started_utc,
                source_columns_present_json,
                fecha_importacion)
            VALUES (
                $importId,
                'sergio_return',
                $sourceFileName,
                $sourceFilePath,
                'analyzing',
                CURRENT_TIMESTAMP,
                $sourceColumnsJson,
                $importDate);
            """;

        command.Parameters.Add(new DuckDBParameter("importId", importId));
        command.Parameters.Add(new DuckDBParameter("sourceFileName", Path.GetFileName(sourceFilePath)));
        command.Parameters.Add(new DuckDBParameter("sourceFilePath", sourceFilePath));
        command.Parameters.Add(new DuckDBParameter("sourceColumnsJson", sourceColumnsJson));
        command.Parameters.Add(new DuckDBParameter("importDate", importDate));
        command.ExecuteNonQuery();
    }

    // Agrega una fila de staging con sus valores tipados y el resultado de validación correspondiente.
    private static void AppendStagingRow(
        DuckDBAppender appender,
        Guid importId,
        SergioRowRecord row,
        string? normalizedCuil,
        string validationOutcome,
        string? validationError)
    {
        TryParseDateOrNull(row.GetValue("fecha_nacimiento"), out var fechaNacimiento);
        TryParseSmallIntOrNull(row.GetValue("edad"), out var edad);
        TryParseSmallIntOrNull(row.GetValue("anio"), out var anio);

        appender.AppendRow(target =>
        {
            // El número de fila se envía como Int64 para evitar colisiones en importaciones grandes.
            var sourceRowNumber = (long)row.SourceRowNumber;

            target.AppendValue(importId)
                .AppendValue(sourceRowNumber);

            AppendNullableString(target, normalizedCuil);
            AppendNullableString(target, NormalizeText(row.GetValue("dni")));
            AppendNullableDate(target, fechaNacimiento);
            AppendNullableString(target, NormalizeText(row.GetValue("sexo")));
            AppendNullableString(target, NormalizeText(row.GetValue("tipo_dni")));
            AppendNullableString(target, NormalizeText(row.GetValue("apellido")));
            AppendNullableString(target, NormalizeText(row.GetValue("nombre")));
            AppendNullableString(target, NormalizeText(row.GetValue("direccion")));
            AppendNullableString(target, NormalizeText(row.GetValue("codigo_postal")));
            AppendNullableString(target, NormalizeText(row.GetValue("localidad")));
            AppendNullableString(target, NormalizeText(row.GetValue("partido")));
            AppendNullableString(target, NormalizeText(row.GetValue("provincia")));
            AppendNullableString(target, NormalizeText(row.GetValue("nacionalidad")));
            AppendNullableString(target, NormalizeText(row.GetValue("telefono_fijo_1")));
            AppendNullableString(target, NormalizeText(row.GetValue("telefono_fijo_2")));
            AppendNullableString(target, NormalizeText(row.GetValue("telefono_fijo_3")));
            AppendNullableString(target, NormalizeText(row.GetValue("telefono_fijo_4")));
            AppendNullableString(target, NormalizeText(row.GetValue("telefono_fijo_5")));
            AppendNullableString(target, NormalizeText(row.GetValue("celular_1")));
            AppendNullableString(target, NormalizeText(row.GetValue("celular_2")));
            AppendNullableString(target, NormalizeText(row.GetValue("celular_3")));
            AppendNullableString(target, NormalizeText(row.GetValue("celular_4")));
            AppendNullableString(target, NormalizeText(row.GetValue("celular_5")));
            AppendNullableString(target, NormalizeText(row.GetValue("whatsapp_1")));
            AppendNullableString(target, NormalizeText(row.GetValue("whatsapp_2")));
            AppendNullableString(target, NormalizeText(row.GetValue("whatsapp_3")));
            AppendNullableString(target, NormalizeText(row.GetValue("whatsapp_4")));
            AppendNullableString(target, NormalizeText(row.GetValue("whatsapp_5")));
            AppendNullableString(target, NormalizeText(row.GetValue("email_1")));
            AppendNullableString(target, NormalizeText(row.GetValue("email_2")));
            AppendNullableString(target, NormalizeText(row.GetValue("email_3")));
            AppendNullableString(target, NormalizeText(row.GetValue("email_4")));
            AppendNullableString(target, NormalizeText(row.GetValue("email_5")));
            AppendNullableString(target, NormalizeText(row.GetValue("codigo_obra_social")));
            AppendNullableString(target, NormalizeText(row.GetValue("obra_social")));
            AppendNullableString(target, NormalizeText(row.GetValue("cuit_empleador")));
            AppendNullableSmallInt(target, edad);
            target.AppendNullValue(); // fecha_actualizacion en el Proceso 2A
            target.AppendValue(validationOutcome);
            AppendNullableString(target, validationError);
            AppendNullableSmallInt(target, anio);
            target.EndRow();
        });
    }

    private static void AppendNullableString(IDuckDBAppenderRow row, string? value)
    {
        if (value is null)
        {
            row.AppendNullValue();
            return;
        }

        row.AppendValue(value);
    }

    private static void AppendNullableDate(IDuckDBAppenderRow row, DateTime? value)
    {
        if (!value.HasValue)
        {
            row.AppendNullValue();
            return;
        }

        row.AppendValue(value.Value);
    }

    private static void AppendNullableSmallInt(IDuckDBAppenderRow row, short? value)
    {
        if (!value.HasValue)
        {
            row.AppendNullValue();
            return;
        }

        row.AppendValue(value.Value);
    }

    private static void UpdateImportRunReadyForConfirmation(
        DuckDBConnection connection,
        DbTransaction transaction,
        Guid importId,
        int totalRows,
        int validRows,
        int rejectedRows,
        int missingCuilRows,
        int malformedCuilRows,
        int duplicateCuilRows,
        int rowsToInsert,
        int rowsToUpdate)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE import_runs
            SET status = 'ready_for_confirmation',
                analyzed_utc = CURRENT_TIMESTAMP,
                total_rows = $totalRows,
                valid_rows = $validRows,
                rejected_rows = $rejectedRows,
                missing_cuil_rows = $missingCuilRows,
                malformed_cuil_rows = $malformedCuilRows,
                duplicate_cuil_rows = $duplicateCuilRows,
                rows_to_insert = $rowsToInsert,
                rows_to_update = $rowsToUpdate,
                error_message = NULL
            WHERE import_id = $importId;
            """,
            new DuckDBParameter("totalRows", totalRows),
            new DuckDBParameter("validRows", validRows),
            new DuckDBParameter("rejectedRows", rejectedRows),
            new DuckDBParameter("missingCuilRows", missingCuilRows),
            new DuckDBParameter("malformedCuilRows", malformedCuilRows),
            new DuckDBParameter("duplicateCuilRows", duplicateCuilRows),
            new DuckDBParameter("rowsToInsert", rowsToInsert),
            new DuckDBParameter("rowsToUpdate", rowsToUpdate),
            new DuckDBParameter("importId", importId));
    }

    private static bool TryMarkImportRunFailed(
        DuckDBConnection connection,
        Guid importId,
        string errorMessage,
        out string? diagnostic)
    {
        diagnostic = null;
        try
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
            var affected = command.ExecuteNonQuery();
            if (affected <= 0)
            {
                diagnostic = "La actualización del estado fallido no afectó ninguna fila.";
                return false;
            }

            return true;
        }
        catch
        {
            diagnostic = "No se pudo guardar la actualización del estado fallido de la importación.";
            return false;
        }
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

    private static void ExecuteNonQuery(
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

        command.ExecuteNonQuery();
    }
}
