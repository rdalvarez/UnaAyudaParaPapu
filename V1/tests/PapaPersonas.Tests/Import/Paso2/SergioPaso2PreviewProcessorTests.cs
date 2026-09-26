using System.Globalization;
using System.Text.Json;
using DuckDB.NET.Data;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso2;
using PapaPersonas.Infrastructure.Database;
using PapaPersonas.Infrastructure.Import.Paso2;

namespace PapaPersonas.Tests.Import.Paso2;

public sealed class SergioPaso2PreviewProcessorTests
{
    private static readonly DateOnly DefaultImportDate = new(2026, 8, 10);

    [Fact]
    public void Analyze_Actual28Headers_AcceptsAndStagesRows()
    {
        var ctx = CreateDbContext();
        try
        {
            var rows = new[]
            {
                Row(2, ("cuil", "20123456789"), ("apellido", "A"), ("nombre", "B"), ("edad", "33")),
                Row(3, ("cuil", "20987654321"), ("apellido", "C"), ("nombre", "D"), ("edad", "40"))
            };

            var source = new FakeSergioRowSource(
                headers: HeaderContracts.SergioActualHeaders28,
                rows: rows);

            var processor = new SergioPaso2PreviewProcessor(source);
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.NotNull(result.ImportId);
            Assert.Equal(2, result.Summary.TotalRows);
            Assert.Equal(2, result.Summary.ValidRows);

            using var connection = OpenConnection(ctx.DatabasePath);
            var staged = ExecuteScalar<int>(
                connection,
                "SELECT COUNT(*) FROM personas_staging WHERE import_id = $id;",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Equal(2, staged);

            var importDate = ExecuteScalar<string>(
                connection,
                "SELECT CAST(fecha_importacion AS VARCHAR) FROM import_runs WHERE import_id = $id;",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Equal("2026-08-10", importDate);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_KnownOptionalColumnsAbsent_AcceptsAndPersistsPresenceJson()
    {
        var ctx = CreateDbContext();
        try
        {
            var headers = HeaderContracts.SergioTemplateHeaders36
                .Where(h => !h.Equals("ANIO", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var rows = new[]
            {
                Row(2, ("cuil", "20123456789"), ("apellido", "A"), ("nombre", "B"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(headers, rows));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.DoesNotContain("anio", result.PresentCanonicalFields, StringComparer.OrdinalIgnoreCase);

            using var connection = OpenConnection(ctx.DatabasePath);
            var json = ExecuteScalar<string>(
                connection,
                "SELECT source_columns_present_json FROM import_runs WHERE import_id = $id;",
                new DuckDBParameter("id", result.ImportId!.Value));

            var fields = JsonSerializer.Deserialize<string[]>(json) ?? [];
            Assert.DoesNotContain("anio", fields, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_UsesRequestImportDate_WhenPersistingRunMetadata()
    {
        var ctx = CreateDbContext();
        try
        {
            var rows = new[]
            {
                Row(2, ("cuil", "20123456789"), ("apellido", "A"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(HeaderContracts.SergioActualHeaders28, rows));
            var customDate = new DateOnly(2026, 8, 9);
            var result = processor.Analyze(new SergioStagePreviewRequest(ctx.InputFilePath, ctx.DatabasePath, customDate));

            AssertSuccessful(result);

            using var connection = OpenConnection(ctx.DatabasePath);
            var persisted = ExecuteScalar<string>(
                connection,
                "SELECT CAST(fecha_importacion AS VARCHAR) FROM import_runs WHERE import_id = $id;",
                new DuckDBParameter("id", result.ImportId!.Value));

            Assert.Equal("2026-08-09", persisted);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_UnknownHeader_RequiresConfirmationBeforeStaging()
    {
        var ctx = CreateDbContext();
        try
        {
            var headers = HeaderContracts.SergioActualHeaders28.Concat(["NEW_UNKNOWN_COL"]).ToArray();
            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(headers, []));

            var result = processor.Analyze(CreateRequest(ctx));

            Assert.False(result.IsSuccess);
            Assert.Equal(SergioStagePreviewStatus.ConfirmationRequired, result.Status);
            Assert.Contains(result.Errors, e => e.Code == ValidationErrorCode.UnknownHeader);
            Assert.Equal(["NEW_UNKNOWN_COL"], result.UnknownSourceHeaders);
            Assert.Equal(Path.GetFullPath(ctx.InputFilePath), result.SourceIdentity?.NormalizedAbsolutePath);
            Assert.NotNull(result.SourceIdentity?.ContentHash);
            Assert.Equal(result.SourceShapeFingerprint, result.SourceIdentity?.SourceShapeFingerprint);

            using var connection = OpenConnection(ctx.DatabasePath);
            var runCount = ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM import_runs;");
            Assert.Equal(0, runCount);
            SergioPaso2PreviewProcessor.CleanupSnapshot(result.SourceSnapshotPath);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_MultipleUnknownHeaders_PreservesSourceOrder()
    {
        var ctx = CreateDbContext();
        try
        {
            var headers = HeaderContracts.SergioActualHeaders28.Concat([" ZETA ", "ALFA", "OMEGA"]).ToArray();
            var result = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(headers, []))
                .Analyze(CreateRequest(ctx));

            Assert.Equal(SergioStagePreviewStatus.ConfirmationRequired, result.Status);
            Assert.Equal([" ZETA ", "ALFA", "OMEGA"], result.UnknownSourceHeaders);
            using var connection = OpenConnection(ctx.DatabasePath);
            Assert.Equal(0, ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM import_runs;"));
            Assert.Equal(0, ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM personas_staging;"));
            SergioPaso2PreviewProcessor.CleanupSnapshot(result.SourceSnapshotPath);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_ContinueUnknownHeaders_StagesOnlyRecognizedFields()
    {
        var ctx = CreateDbContext();
        try
        {
            var headers = HeaderContracts.SergioActualHeaders28.Concat(["NEW_UNKNOWN_COL"]).ToArray();
            var source = new FakeSergioRowSource(
                headers,
                [Row(2, ("cuil", "20123456789"), ("apellido", "RECOGNIZED"), ("unknown_field", "SHOULD_NOT_PERSIST"))]);
            var processor = new SergioPaso2PreviewProcessor(source);
            var first = processor.Analyze(CreateRequest(ctx));
            Assert.Equal(SergioStagePreviewStatus.ConfirmationRequired, first.Status);

            var continued = processor.Analyze(new SergioStagePreviewRequest(
                ctx.InputFilePath,
                ctx.DatabasePath,
                DefaultImportDate,
                AllowUnknownHeaders: true,
                ExpectedSourceShapeFingerprint: first.SourceShapeFingerprint,
                ExpectedSourceIdentity: first.SourceIdentity,
                SourceSnapshotPath: first.SourceSnapshotPath));

            AssertSuccessful(continued);
            Assert.DoesNotContain("NEW_UNKNOWN_COL", continued.PresentCanonicalFields, StringComparer.OrdinalIgnoreCase);
            using var connection = OpenConnection(ctx.DatabasePath);
            Assert.Equal(1, ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM personas_staging WHERE import_id = $id AND apellido = 'RECOGNIZED';", new DuckDBParameter("id", continued.ImportId!.Value)));
            Assert.Equal(0, ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'personas' AND column_name = 'NEW_UNKNOWN_COL';"));
            Assert.DoesNotContain("SHOULD_NOT_PERSIST", ExecuteScalar<string>(connection, "SELECT source_columns_present_json FROM import_runs WHERE import_id = $id;", new DuckDBParameter("id", continued.ImportId.Value)), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Theory]
    [InlineData("missing-cuil")]
    [InlineData("duplicate-header")]
    [InlineData("canonical-collision")]
    public void Analyze_UnknownHeaderWithStructuralBlocker_RemainsBlocked(string blocker)
    {
        var ctx = CreateDbContext();
        try
        {
            var headers = HeaderContracts.SergioActualHeaders28.Concat(["NEW_UNKNOWN_COL"]).ToList();
            if (blocker == "missing-cuil")
            {
                headers.RemoveAll(h => h.Equals("CUIL", StringComparison.OrdinalIgnoreCase));
            }
            else if (blocker == "duplicate-header")
            {
                headers.Add("CUIL");
            }
            else
            {
                headers.Add("FECHA DE NAC");
            }

            var result = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(headers, []))
                .Analyze(CreateRequest(ctx));

            Assert.Equal(SergioStagePreviewStatus.ValidationFailed, result.Status);
            Assert.NotEmpty(result.Errors);
            using var connection = OpenConnection(ctx.DatabasePath);
            Assert.Equal(0, ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM import_runs;"));
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_ChangedSourceShape_CannotReuseUnknownHeaderConfirmation()
    {
        var ctx = CreateDbContext();
        try
        {
            var firstHeaders = HeaderContracts.SergioActualHeaders28.Concat(["NEW_UNKNOWN_COL"]).ToArray();
            var changedHeaders = HeaderContracts.SergioActualHeaders28.Concat(["DIFFERENT_UNKNOWN_COL"]).ToArray();
            var source = new ShapeChangingSergioRowSource(firstHeaders, changedHeaders);
            var processor = new SergioPaso2PreviewProcessor(source);
            var first = processor.Analyze(CreateRequest(ctx));

            var second = processor.Analyze(new SergioStagePreviewRequest(
                ctx.InputFilePath,
                ctx.DatabasePath,
                DefaultImportDate,
                AllowUnknownHeaders: true,
                ExpectedSourceShapeFingerprint: first.SourceShapeFingerprint,
                ExpectedSourceIdentity: first.SourceIdentity,
                SourceSnapshotPath: first.SourceSnapshotPath));

            Assert.Equal(SergioStagePreviewStatus.ValidationFailed, second.Status);
            Assert.Contains(second.Errors, issue => issue.Code == ValidationErrorCode.SourceShapeMismatch);
            using var connection = OpenConnection(ctx.DatabasePath);
            Assert.Equal(0, ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM import_runs;"));
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_SameHeadersWithChangedBytes_CannotReuseUnknownHeaderConfirmation()
    {
        var ctx = CreateDbContext();
        try
        {
            var headers = HeaderContracts.SergioActualHeaders28.Concat(["NEW_UNKNOWN_COL"]).ToArray();
            var source = new FakeSergioRowSource(
                headers,
                [Row(2, ("cuil", "20123456789"), ("apellido", "original"))]);
            var processor = new SergioPaso2PreviewProcessor(source);
            var first = processor.Analyze(CreateRequest(ctx));

            Assert.Equal(SergioStagePreviewStatus.ConfirmationRequired, first.Status);
            Assert.True(File.Exists(first.SourceSnapshotPath));
            File.WriteAllText(ctx.InputFilePath, "same headers, changed row bytes", System.Text.Encoding.UTF8);

            var second = processor.Analyze(new SergioStagePreviewRequest(
                ctx.InputFilePath,
                ctx.DatabasePath,
                DefaultImportDate,
                AllowUnknownHeaders: true,
                ExpectedSourceIdentity: first.SourceIdentity,
                SourceSnapshotPath: first.SourceSnapshotPath));

            Assert.Equal(SergioStagePreviewStatus.ValidationFailed, second.Status);
            Assert.Contains(second.Errors, issue => issue.Code == ValidationErrorCode.SourceShapeMismatch);
            Assert.False(File.Exists(first.SourceSnapshotPath));
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_ExactApprovedSource_StagesSnapshotAndCleansIt()
    {
        var ctx = CreateDbContext();
        try
        {
            var headers = HeaderContracts.SergioActualHeaders28.Concat(["NEW_UNKNOWN_COL"]).ToArray();
            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(
                headers,
                [Row(2, ("cuil", "20123456789"), ("apellido", "snapshot"))]));
            var first = processor.Analyze(CreateRequest(ctx));

            var snapshotPath = first.SourceSnapshotPath;
            Assert.NotNull(snapshotPath);
            Assert.True(File.Exists(snapshotPath));
            var continued = processor.Analyze(new SergioStagePreviewRequest(
                ctx.InputFilePath,
                ctx.DatabasePath,
                DefaultImportDate,
                AllowUnknownHeaders: true,
                ExpectedSourceIdentity: first.SourceIdentity,
                SourceSnapshotPath: snapshotPath));

            AssertSuccessful(continued);
            Assert.False(File.Exists(snapshotPath));
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_ChangedPathWithSameBytes_CannotReuseUnknownHeaderConfirmation()
    {
        var ctx = CreateDbContext();
        var changedPath = Path.Combine(ctx.RootDirectory, "renamed.xlsx");
        try
        {
            var headers = HeaderContracts.SergioActualHeaders28.Concat(["NEW_UNKNOWN_COL"]).ToArray();
            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(headers, []));
            var first = processor.Analyze(CreateRequest(ctx));
            File.Copy(ctx.InputFilePath, changedPath);

            var second = processor.Analyze(new SergioStagePreviewRequest(
                changedPath,
                ctx.DatabasePath,
                DefaultImportDate,
                AllowUnknownHeaders: true,
                ExpectedSourceIdentity: first.SourceIdentity,
                SourceSnapshotPath: first.SourceSnapshotPath));

            Assert.Equal(SergioStagePreviewStatus.ValidationFailed, second.Status);
            Assert.Contains(second.Errors, issue => issue.Code == ValidationErrorCode.SourceShapeMismatch);
            Assert.False(File.Exists(first.SourceSnapshotPath));
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void CleanupSnapshot_DeletesApprovedSnapshotOnCancel()
    {
        var ctx = CreateDbContext();
        try
        {
            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(
                HeaderContracts.SergioActualHeaders28.Concat(["NEW_UNKNOWN_COL"]).ToArray(),
                []));
            var first = processor.Analyze(CreateRequest(ctx));

            Assert.True(File.Exists(first.SourceSnapshotPath));
            SergioPaso2PreviewProcessor.CleanupSnapshot(first.SourceSnapshotPath);

            Assert.False(File.Exists(first.SourceSnapshotPath));
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_MissingCuilHeader_BlocksBeforeStaging()
    {
        var ctx = CreateDbContext();
        try
        {
            var headers = HeaderContracts.SergioActualHeaders28
                .Where(h => !h.Equals("CUIL", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(headers, []));
            var result = processor.Analyze(CreateRequest(ctx));

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Code == ValidationErrorCode.MissingRequiredHeader);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_MissingAndMalformedCuil_AreRejectedInStaging()
    {
        var ctx = CreateDbContext();
        try
        {
            var rows = new[]
            {
                Row(2, ("cuil", ""), ("apellido", "A")),
                Row(3, ("cuil", "20-12345678-A"), ("apellido", "B")),
                Row(4, ("cuil", "20123456789"), ("apellido", "C"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(HeaderContracts.SergioActualHeaders28, rows));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.Equal(1, result.Summary.ValidRows);
            Assert.Equal(1, result.Summary.MissingCuilRows);
            Assert.Equal(1, result.Summary.MalformedCuilRows);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_DuplicateCuil_RejectsAllRowsInGroup()
    {
        var ctx = CreateDbContext();
        try
        {
            var rows = new[]
            {
                Row(2, ("cuil", "20-12345678-9"), ("apellido", "A")),
                Row(3, ("cuil", "20123456789"), ("apellido", "B")),
                Row(4, ("cuil", "20987654321"), ("apellido", "C"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(HeaderContracts.SergioActualHeaders28, rows));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.Equal(2, result.Summary.DuplicateCuilRows);
            Assert.Equal(1, result.Summary.ValidRows);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_DuplicateCuil_WithMixedTypedErrors_StillRejectsEntireGroup()
    {
        var ctx = CreateDbContext();
        try
        {
            var rows = new[]
            {
                Row(2, ("cuil", "20-12345678-9"), ("fecha_nacimiento", "not-a-date"), ("apellido", "A")),
                Row(3, ("cuil", "20123456789"), ("fecha_nacimiento", "2024-01-01"), ("apellido", "B")),
                Row(4, ("cuil", "27999999999"), ("fecha_nacimiento", "2024-01-01"), ("apellido", "C"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(HeaderContracts.SergioActualHeaders28, rows));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.Equal(2, result.Summary.DuplicateCuilRows);
            Assert.Equal(1, result.Summary.ValidRows);

            using var connection = OpenConnection(ctx.DatabasePath);
            var duplicateRows = ExecuteScalar<int>(
                connection,
                "SELECT COUNT(*) FROM personas_staging WHERE import_id = $id AND validation_error = 'DuplicateCuilInBatch';",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Equal(2, duplicateRows);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_CountsInsertVsUpdate_AgainstPersonas()
    {
        var ctx = CreateDbContext();
        try
        {
            using (var connection = OpenConnection(ctx.DatabasePath))
            {
                ExecuteNonQuery(
                    connection,
                    "INSERT INTO personas (cuil, fecha_actualizacion) VALUES ('20123456789', CURRENT_TIMESTAMP);");
            }

            var rows = new[]
            {
                Row(2, ("cuil", "20123456789"), ("apellido", "A")),
                Row(3, ("cuil", "20987654321"), ("apellido", "B"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(HeaderContracts.SergioActualHeaders28, rows));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.Equal(1, result.Summary.RowsToUpdate);
            Assert.Equal(1, result.Summary.RowsToInsert);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_PresentEmptyVsAbsent_UsesNullAndPresenceMetadata()
    {
        var ctx = CreateDbContext();
        try
        {
            var headers = new[] { "CUIL", "EMAIL1", "APELLIDO" };
            var rows = new[]
            {
                Row(2, ("cuil", "20123456789"), ("email_1", "   "), ("apellido", "A"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(headers, rows));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.Contains("email_1", result.PresentCanonicalFields, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("email_2", result.PresentCanonicalFields, StringComparer.OrdinalIgnoreCase);

            using var connection = OpenConnection(ctx.DatabasePath);
            var email1IsNull = ExecuteScalar<int>(
                connection,
                "SELECT COUNT(*) FROM personas_staging WHERE import_id = $id AND email_1 IS NULL;",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Equal(1, email1IsNull);

            var json = ExecuteScalar<string>(
                connection,
                "SELECT source_columns_present_json FROM import_runs WHERE import_id = $id;",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Contains("email_1", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("email_2", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_InvalidDateOrAge_RejectsRowWithoutAbortingImport()
    {
        var ctx = CreateDbContext();
        try
        {
            var rows = new[]
            {
                Row(2, ("cuil", "20123456789"), ("fecha_nacimiento", "not-a-date"), ("edad", "33"), ("apellido", "A")),
                Row(3, ("cuil", "20987654321"), ("fecha_nacimiento", "2024-01-01"), ("edad", "abc"), ("apellido", "B")),
                Row(4, ("cuil", "27123456789"), ("fecha_nacimiento", "2024-01-01"), ("edad", "41"), ("apellido", "C"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(HeaderContracts.SergioActualHeaders28, rows));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.Equal(1, result.Summary.ValidRows);
            Assert.Equal(2, result.Summary.InvalidTypedValueRows);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_OaDateSerial_IsAcceptedForFechaNacimiento()
    {
        var ctx = CreateDbContext();
        try
        {
            var rows = new[]
            {
                Row(2, ("cuil", "20123456789"), ("fecha_nacimiento", "45567"), ("edad", "33"), ("apellido", "A"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(HeaderContracts.SergioActualHeaders28, rows));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.Equal(1, result.Summary.ValidRows);

            using var connection = OpenConnection(ctx.DatabasePath);
            var hasDate = ExecuteScalar<int>(
                connection,
                "SELECT COUNT(*) FROM personas_staging WHERE import_id = $id AND fecha_nacimiento IS NOT NULL;",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Equal(1, hasDate);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_OaDateOutOfRange_RejectsRow()
    {
        var ctx = CreateDbContext();
        try
        {
            var rows = new[]
            {
                Row(2, ("cuil", "20123456789"), ("fecha_nacimiento", "999999999"), ("edad", "33"), ("apellido", "A"))
            };

            var processor = new SergioPaso2PreviewProcessor(new FakeSergioRowSource(HeaderContracts.SergioActualHeaders28, rows));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.Equal(0, result.Summary.ValidRows);
            Assert.Equal(1, result.Summary.InvalidTypedValueRows);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_FailureMarksRunFailedAndDoesNotTouchPersonas()
    {
        var ctx = CreateDbContext();
        try
        {
            using (var connection = OpenConnection(ctx.DatabasePath))
            {
                ExecuteNonQuery(
                    connection,
                    "INSERT INTO personas (cuil, fecha_actualizacion) VALUES ('30999999999', CURRENT_TIMESTAMP);");
            }

            var rows = new[]
            {
                Row(2, ("cuil", "20123456789"), ("apellido", "A"))
            };

            var throwing = new FakeSergioRowSource(
                HeaderContracts.SergioActualHeaders28,
                rows,
                throwAfterRows: 0);

            var processor = new SergioPaso2PreviewProcessor(throwing);
            var result = processor.Analyze(CreateRequest(ctx));

            Assert.False(result.IsSuccess);
            Assert.Equal(SergioStagePreviewStatus.Failed, result.Status);
            Assert.Equal(
                "La vista previa de Paso 2 falló. Verificá el formato del archivo y la base, y volvé a intentar. Detalle técnico: Synthetic streaming failure",
                result.FailureMessage);
            Assert.DoesNotContain(nameof(InvalidOperationException), result.FailureMessage, StringComparison.Ordinal);

            using var checkConnection = OpenConnection(ctx.DatabasePath);
            var personasCount = ExecuteScalar<int>(checkConnection, "SELECT COUNT(*) FROM personas;");
            Assert.Equal(1, personasCount);

            var failedRuns = ExecuteScalar<int>(
                checkConnection,
                "SELECT COUNT(*) FROM import_runs WHERE status = 'failed';");
            Assert.Equal(1, failedRuns);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Analyze_LargeBatch205kRows_PreservesDistinctSourceRowNumbers()
    {
        const int rowCount = 205_000;

        var ctx = CreateDbContext();
        try
        {
            var processor = new SergioPaso2PreviewProcessor(new LargeSyntheticSergioRowSource(rowCount));
            var result = processor.Analyze(CreateRequest(ctx));

            AssertSuccessful(result);
            Assert.Equal(SergioStagePreviewStatus.ReadyForConfirmation, result.Status);
            Assert.Equal(rowCount, result.Summary.TotalRows);
            Assert.Equal(rowCount, result.Summary.ValidRows);

            using var connection = OpenConnection(ctx.DatabasePath);

            var stagedCount = ExecuteScalar<int>(
                connection,
                "SELECT COUNT(*) FROM personas_staging WHERE import_id = $id;",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Equal(rowCount, stagedCount);

            var distinctSourceRows = ExecuteScalar<int>(
                connection,
                "SELECT COUNT(DISTINCT source_row_number) FROM personas_staging WHERE import_id = $id;",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Equal(rowCount, distinctSourceRows);

            var zeroSourceRows = ExecuteScalar<int>(
                connection,
                "SELECT COUNT(*) FROM personas_staging WHERE import_id = $id AND source_row_number = 0;",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Equal(0, zeroSourceRows);

            var runStatus = ExecuteScalar<string>(
                connection,
                "SELECT status FROM import_runs WHERE import_id = $id;",
                new DuckDBParameter("id", result.ImportId!.Value));
            Assert.Equal("ready_for_confirmation", runStatus);
        }
        finally
        {
            ctx.Dispose();
        }
    }

    private static SergioRowRecord Row(int sourceRowNumber, params (string CanonicalField, string? Value)[] values)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonicalField, value) in values)
        {
            dict[canonicalField] = value;
        }

        return new SergioRowRecord(sourceRowNumber, dict);
    }

    private static SergioStagePreviewRequest CreateRequest(TestDbContext ctx)
    {
        return new SergioStagePreviewRequest(ctx.InputFilePath, ctx.DatabasePath, DefaultImportDate);
    }

    private static void AssertSuccessful(SergioStagePreviewResult result)
    {
        Assert.True(
            result.IsSuccess,
            $"Expected success but got status={result.Status}, failure='{result.FailureMessage}', notices='{string.Join(" | ", result.Notices.Select(n => n.Code + ":" + n.Message))}'");
    }

    private sealed class FakeSergioRowSource : ISergioRowSource
    {
        private readonly IReadOnlyList<string> _headers;
        private readonly IReadOnlyList<SergioRowRecord> _rows;
        private readonly int? _throwAfterRows;

        public FakeSergioRowSource(
            IReadOnlyList<string> headers,
            IReadOnlyList<SergioRowRecord> rows,
            int? throwAfterRows = null)
        {
            _headers = headers;
            _rows = rows;
            _throwAfterRows = throwAfterRows;
        }

        public IReadOnlyList<string> ReadHeaders(string filePath) => _headers;

        public IEnumerable<SergioRowRecord> ReadRows(string filePath, HeaderValidationResult headerValidation)
        {
            var index = 0;
            foreach (var row in _rows)
            {
                if (_throwAfterRows.HasValue && index >= _throwAfterRows.Value)
                {
                    throw new InvalidOperationException("Synthetic streaming failure");
                }

                index++;
                yield return row;
            }
        }
    }

    private sealed class LargeSyntheticSergioRowSource : ISergioRowSource
    {
        private readonly int _rowCount;

        public LargeSyntheticSergioRowSource(int rowCount)
        {
            _rowCount = rowCount;
        }

        public IReadOnlyList<string> ReadHeaders(string filePath)
        {
            return ["CUIL", "APELLIDO"];
        }

        public IEnumerable<SergioRowRecord> ReadRows(string filePath, HeaderValidationResult headerValidation)
        {
            for (var i = 0; i < _rowCount; i++)
            {
                var sourceRowNumber = i + 2;
                var cuil = (20000000000L + i).ToString(CultureInfo.InvariantCulture);

                yield return new SergioRowRecord(
                    sourceRowNumber,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["cuil"] = cuil,
                        ["apellido"] = "SYNTH"
                    });
            }
        }
    }

    private sealed class ShapeChangingSergioRowSource : ISergioRowSource
    {
        private readonly IReadOnlyList<string> _firstHeaders;
        private readonly IReadOnlyList<string> _changedHeaders;
        private int _headerReads;

        public ShapeChangingSergioRowSource(IReadOnlyList<string> firstHeaders, IReadOnlyList<string> changedHeaders)
        {
            _firstHeaders = firstHeaders;
            _changedHeaders = changedHeaders;
        }

        public IReadOnlyList<string> ReadHeaders(string filePath) => ++_headerReads == 1 ? _firstHeaders : _changedHeaders;

        public IEnumerable<SergioRowRecord> ReadRows(string filePath, HeaderValidationResult headerValidation) => [];
    }

    private static TestDbContext CreateDbContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "PapaPersonas.duckdb");

        var bootstrapper = new DuckDbBootstrapper();
        var bootstrap = bootstrapper.Initialize(dbPath);
        Assert.True(bootstrap.IsSuccess, bootstrap.Message);

        var inputPath = Path.Combine(root, "synthetic.xlsx");
        File.WriteAllText(inputPath, "placeholder", System.Text.Encoding.UTF8);
        return new TestDbContext(root, dbPath, inputPath);
    }

    private static DuckDBConnection OpenConnection(string databasePath)
    {
        var builder = new DuckDBConnectionStringBuilder { DataSource = databasePath };
        var connection = new DuckDBConnection(builder.ConnectionString);
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

    private static T ExecuteScalar<T>(DuckDBConnection connection, string sql, params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var value = command.ExecuteScalar();
        return (T)Convert.ChangeType(value!, typeof(T), CultureInfo.InvariantCulture);
    }

    private sealed class TestDbContext : IDisposable
    {
        public TestDbContext(string rootDirectory, string databasePath, string inputFilePath)
        {
            RootDirectory = rootDirectory;
            DatabasePath = databasePath;
            InputFilePath = inputFilePath;
        }

        public string RootDirectory { get; }
        public string DatabasePath { get; }
        public string InputFilePath { get; }

        public void Dispose()
        {
            if (!Directory.Exists(RootDirectory))
            {
                return;
            }

            var retries = 5;
            for (var i = 0; i < retries; i++)
            {
                try
                {
                    Directory.Delete(RootDirectory, recursive: true);
                    return;
                }
                catch (IOException) when (i < retries - 1)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException) when (i < retries - 1)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
