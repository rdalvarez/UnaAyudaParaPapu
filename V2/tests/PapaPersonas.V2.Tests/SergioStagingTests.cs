using ClosedXML.Excel;
using DuckDB.NET.Data;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class SergioStagingTests
{
    [Fact]
    public void PreviewTextDistinguishesFirstLoadAndReplacement()
    {
        var first = new SergioPreview(["CUIL"], [], 2, "CUIL", "first.xlsx", false, null);
        var replacement = first with { PreviousOfficialExists = true, PreviousMetadata = new SergioLoadMetadata("old.xlsx", DateTime.UtcNow, 1, "CUIL", null) };

        Assert.Equal("Cargar", SergioLoadPreviewText.Action(first));
        Assert.Equal("Reemplazar", SergioLoadPreviewText.Action(replacement));
        Assert.Contains("sergio_current", SergioLoadPreviewText.Warning(replacement));
        Assert.DoesNotContain("reemplazará", SergioLoadPreviewText.Warning(first));
    }

    [Fact]
    public void FormatterPreservesStringsAndUsesDeterministicText()
    {
        Assert.Equal("1", SergioCellTextFormatter.Format(1.0));
        Assert.Equal("1.25", SergioCellTextFormatter.Format(1.25m));
        Assert.Equal("a,b", SergioCellTextFormatter.Format("a,b"));
        Assert.Null(SergioCellTextFormatter.Format(null));
        Assert.Equal("2026-01-02", SergioCellTextFormatter.Format(new DateTime(2026, 1, 2)));
        Assert.Equal("2026-01-02 03:04:05.1234567", SergioCellTextFormatter.Format(new DateTime(2026, 1, 2, 3, 4, 5, 123).AddTicks(4567)));
        Assert.Equal("03:04:05", SergioCellTextFormatter.Format(new TimeSpan(3, 4, 5)));
        Assert.Equal("true", SergioCellTextFormatter.Format(true));
    }

    [Fact]
    public void ValidWorkbookRetainsIsolatedAllVarcharCandidate()
    {
        using var workspace = new TestWorkspace();
        var workbook = workspace.CreateWorkbook(["CUIL", "Nombre"], [[" 20-1 ", "Ana"], ["20-2", "Beto"]]);
        var headers = SergioHeaderPreflight.Analyze(["CUIL", "Nombre"]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));

        var result = analyzer.Analyze(new SergioStagingRequest(workbook, headers, "CUIL"));

        Assert.True(result.IsValid);
        Assert.StartsWith("sergio_staging_", result.CandidateTableName);
        using var connection = Open(workspace.DatabasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT column_name, data_type FROM information_schema.columns WHERE table_name = '{result.CandidateTableName}' ORDER BY ordinal_position";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("CUIL", reader.GetString(0));
        Assert.Equal("VARCHAR", reader.GetString(1));
        analyzer.Cleanup(result.CandidateTableName);
        Assert.False(TableExists(connection, result.CandidateTableName));
    }

    [Fact]
    public void AcceptedRawDuplicateAndEmptyHeadersStageWithChosenValidAlias()
    {
        using var workspace = new TestWorkspace();
        var workbook = workspace.CreateWorkbook(["CUIL", null!, "Nombre", "Nombre"], [["20-1", "x", "Ana", "Alias"]]);
        var headers = SergioHeaderPreflight.Analyze(["CUIL", null, "Nombre", "Nombre"]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));

        var result = analyzer.Analyze(new SergioStagingRequest(workbook, headers, "CUIL"));

        Assert.True(result.IsValid);
        Assert.Equal(["CUIL", "Columna Vacia", "Nombre", "Nombre (1)"], result.Aliases);
        Assert.True(TableExists(Open(workspace.DatabasePath), result.CandidateTableName));
        analyzer.Cleanup(result.CandidateTableName);
    }

    [Fact]
    public void ExactReservedSourceColumnIsReusedAndItsValuesAreIgnored()
    {
        using var workspace = new TestWorkspace();
        var workbook = workspace.CreateWorkbook(["CUIL", SergioSchemaNames.RejectionReason, "__pp_motivo_rechazo_1"], [["20-1", "source", "business"]]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));

        var result = analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL", SergioSchemaNames.RejectionReason, "__pp_motivo_rechazo_1"]), "CUIL"));

        Assert.Equal(["CUIL", "__pp_motivo_rechazo_1"], result.Aliases);
        Assert.Equal(SergioSchemaNames.RejectionReason, result.TechnicalReasonColumnName);
        using var verify = Open(workspace.DatabasePath);
        Assert.Equal(1, CountColumns(verify, result.CandidateTableName, SergioSchemaNames.RejectionReason));
        Assert.Null(ReadNullableValue(verify, result.CandidateTableName, SergioSchemaNames.RejectionReason));
        Assert.Equal("business", ReadValue(verify, result.CandidateTableName, "__pp_motivo_rechazo_1"));
        analyzer.Cleanup(result.CandidateTableName);
    }

    [Fact]
    public void ReservedColumnInTheMiddleDoesNotShiftFollowingBusinessValues()
    {
        using var workspace = new TestWorkspace();
        var workbook = workspace.CreateWorkbook(["CUIL", SergioSchemaNames.RejectionReason, "Nombre"], [["20-1", "hostile source reason", "Ana"]]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));

        var result = analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL", SergioSchemaNames.RejectionReason, "Nombre"]), "CUIL"));

        using var verify = Open(workspace.DatabasePath);
        Assert.Equal(["CUIL", "Nombre"], result.Aliases);
        Assert.Equal("20-1", ReadValue(verify, result.CandidateTableName, "CUIL"));
        Assert.Equal("Ana", ReadValue(verify, result.CandidateTableName, "Nombre"));
        Assert.Null(ReadNullableValue(verify, result.CandidateTableName, SergioSchemaNames.RejectionReason));
        analyzer.Cleanup(result.CandidateTableName);
    }

    [Fact]
    public void ReservedPrimaryKeyRequestIsRejected()
    {
        using var workspace = new TestWorkspace();
        var workbook = workspace.CreateWorkbook(["CUIL", SergioSchemaNames.RejectionReason], [["20-1", "source"]]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));

        Assert.Throws<SergioStagingException>(() => analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL", SergioSchemaNames.RejectionReason]), SergioSchemaNames.RejectionReason)));
    }

    [Fact]
    public void DuplicateAliasCanBeSelectedAsPrimaryKeyIndependently()
    {
        using var workspace = new TestWorkspace();
        var workbook = workspace.CreateWorkbook(["CUIL", "CUIL"], [["20-1", "20-2"]]);
        var headers = SergioHeaderPreflight.Analyze(["CUIL", "CUIL"]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));

        var result = analyzer.Analyze(new SergioStagingRequest(workbook, headers, "CUIL (1)"));

        Assert.True(result.IsValid);
        Assert.Equal("CUIL (1)", result.SelectedPrimaryKeyAlias);
        analyzer.Cleanup(result.CandidateTableName);
    }

    [Fact]
    public void CleanupDoesNotDropUnknownReservedLookingTable()
    {
        using var workspace = new TestWorkspace();
        var unknown = "sergio_staging_0123456789abcdef0123456789abcdef";
        using (var connection = Open(workspace.DatabasePath))
        using (var command = connection.CreateCommand()) { command.CommandText = $"CREATE TABLE \"{unknown}\"(value VARCHAR)"; command.ExecuteNonQuery(); }
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));

        analyzer.Cleanup(unknown);

        using var verify = Open(workspace.DatabasePath);
        Assert.True(TableExists(verify, unknown));
    }

    [Fact]
    public void FirstAnalyzeRemovesExactStaleCandidatesButPreservesNearMatches()
    {
        using var workspace = new TestWorkspace();
        var stale = "sergio_staging_0123456789abcdef0123456789abcdef";
        var nearMatch = "sergio_staging_0123456789abcdef0123456789abcde";
        var unrelated = "sergio_staging_extra";
        using (var connection = Open(workspace.DatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"CREATE TABLE \"{stale}\"(value VARCHAR); CREATE TABLE \"{nearMatch}\"(value VARCHAR); CREATE TABLE \"{unrelated}\"(value VARCHAR);";
            command.ExecuteNonQuery();
        }
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var workbook = workspace.CreateWorkbook(["CUIL"], [["20-1"]]);

        var result = analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL"]), "CUIL"));

        using var verify = Open(workspace.DatabasePath);
        Assert.False(TableExists(verify, stale));
        Assert.True(TableExists(verify, nearMatch));
        Assert.True(TableExists(verify, unrelated));
        analyzer.Cleanup(result.CandidateTableName);
    }

    [Fact]
    public void MixedKeysRetainEveryRowAndClassifyRejections()
    {
        using var workspace = new TestWorkspace();
        using (var connection = Open(workspace.DatabasePath))
        using (var command = connection.CreateCommand()) { command.CommandText = "CREATE TABLE unrelated(value VARCHAR)"; command.ExecuteNonQuery(); }
        var workbook = workspace.CreateWorkbook(["CUIL"], [" 20-1 ", "20-1", "", "  ", "20-2"]);
        var headers = SergioHeaderPreflight.Analyze(["CUIL"]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));

        var result = analyzer.Analyze(new SergioStagingRequest(workbook, headers, "CUIL"));

        Assert.False(result.IsValid);
        Assert.Equal(1, result.ValidRows);
        Assert.Equal(2, result.EmptyPrimaryKeyRows);
        Assert.Equal(1, result.DuplicateGroups);
        Assert.Equal(2, result.DuplicateRows);
        Assert.Equal(5, result.TotalRows);
        using var verify = Open(workspace.DatabasePath);
        Assert.True(TableExists(verify, "unrelated"));
        Assert.Equal(5, CountRows(verify, result.CandidateTableName));
        Assert.Equal(2, CountReason(verify, result.CandidateTableName, result.TechnicalReasonColumnName, "CLAVE_VACIA"));
        Assert.Equal(2, CountReason(verify, result.CandidateTableName, result.TechnicalReasonColumnName, "CLAVE_DUPLICADA"));
        analyzer.Cleanup(result.CandidateTableName);
    }

    [Fact]
    public void PreviewIsBoundedAndPreservesVarcharValuesAndNulls()
    {
        using var workspace = new TestWorkspace();
        var workbook = workspace.CreateWorkbook(["CUIL", "Nombre"], Enumerable.Range(1, 25).Select(i => new[] { $"{i}", i == 1 ? null! : $"N{i}" }).ToArray());
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var staged = analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL", "Nombre"]), "CUIL"));

        var preview = new SergioLoadPromoter(analyzer).Preview(staged.CandidateTableName, "CUIL", workbook);

        Assert.Equal(25, preview.TotalRows);
        Assert.Equal(20, preview.Rows.Count);
        Assert.Equal(["CUIL", "Nombre"], preview.Aliases);
        Assert.Null(preview.Rows[0][1]);
        Assert.Equal("sergio.xlsx", preview.SourceFileName);
        analyzer.Cleanup(staged.CandidateTableName);
    }

    [Fact]
    public void MixedPreviewAndPromotionKeepRejectedRowsOnlyInLastImportWithRealPrimaryKey()
    {
        using var workspace = new TestWorkspace();
        var workbook = workspace.CreateWorkbook(["CUIL", "Nombre"], [["20-1", "Ana"], ["20-1", "Beto"], ["20-2", "Ok"], ["", "Empty"]]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var staged = analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL", "Nombre"]), "CUIL"));
        var promoter = new SergioLoadPromoter(analyzer);

        var preview = promoter.Preview(staged.CandidateTableName, "CUIL", workbook);
        Assert.Equal(4, preview.ImportedRows);
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(3, preview.RejectedRows);
        Assert.Equal("20-2", preview.Rows.Single()[0]);
        promoter.Promote(staged.CandidateTableName, "CUIL", workbook);

        using var connection = Open(workspace.DatabasePath);
        Assert.Equal(1, CountRows(connection, SergioLoadPromoter.OfficialTableName));
        Assert.Equal(4, CountRows(connection, SergioLoadPromoter.LastImportTableName));
        Assert.Equal(0, CountColumns(connection, SergioLoadPromoter.OfficialTableName, SergioSchemaNames.RejectionReason));
        Assert.Equal(1, CountPrimaryKeys(connection, SergioLoadPromoter.OfficialTableName, "CUIL"));
        Assert.ThrowsAny<Exception>(() => Execute(connection, "INSERT INTO sergio_current VALUES ('20-2', 'Again')"));
        Assert.ThrowsAny<Exception>(() => Execute(connection, "INSERT INTO sergio_current VALUES (NULL, 'Null')"));
        analyzer.CleanupAll();
        Assert.True(TableExists(connection, SergioLoadPromoter.OfficialTableName));
        Assert.True(TableExists(connection, SergioLoadPromoter.LastImportTableName));
    }

    [Fact]
    public void ExporterCanUseRetainedLastImportAfterPromotion()
    {
        using var workspace = new TestWorkspace();
        var workbook = workspace.CreateWorkbook(["CUIL"], [["20-1"], ["20-1"], ["20-2"]]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var staged = analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL"]), "CUIL"));
        new SergioLoadPromoter(analyzer).Promote(staged.CandidateTableName, "CUIL", workbook);

        var result = new SergioRejectedExporter(analyzer).Export(SergioLoadPromoter.LastImportTableName, workbook);
        Assert.Equal(2, result.RejectedCount);
        Assert.Contains("CLAVE_DUPLICADA", File.ReadAllText(result.OutputPath!));
    }

    [Fact]
    public void PromotionReplacesOfficialAndWritesOneMetadataRow()
    {
        using var workspace = new TestWorkspace();
        using var connection = Open(workspace.DatabasePath);
        using (var command = connection.CreateCommand()) { command.CommandText = "CREATE TABLE unrelated(value VARCHAR)"; command.ExecuteNonQuery(); }
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var first = Stage(workspace, analyzer, "old.xlsx", "old");
        var promoter = new SergioLoadPromoter(analyzer);
        promoter.Promote(first.CandidateTableName, "CUIL", first.Workbook);
        var second = Stage(workspace, analyzer, "new.xlsx", "new");
        promoter.Promote(second.CandidateTableName, "CUIL", second.Workbook);

        using var verify = Open(workspace.DatabasePath);
        Assert.Equal("new", ReadValue(verify, "sergio_current", "CUIL"));
        Assert.Equal("new.xlsx", ReadValue(verify, "sergio_load_metadata", "source_file_name"));
        Assert.Equal(1, CountRows(verify, "sergio_load_metadata"));
        Assert.True(TableExists(verify, "unrelated"));
        Assert.False(TableExists(verify, first.CandidateTableName));
        Assert.False(TableExists(verify, second.CandidateTableName));
        Assert.ThrowsAny<Exception>(() => new SergioRejectedExporter(analyzer).Export(first.CandidateTableName, first.Workbook));
        Assert.Equal("new", ReadValue(verify, SergioLoadPromoter.LastImportTableName, "CUIL"));
    }

    [Fact]
    public void FailureAfterReplacementRollsBackAndCandidateRemainsRetryable()
    {
        using var workspace = new TestWorkspace();
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var old = Stage(workspace, analyzer, "old.xlsx", "old");
        var stable = new SergioLoadPromoter(analyzer);
        stable.Promote(old.CandidateTableName, "CUIL", old.Workbook);
        var previousExport = new SergioRejectedExporter(analyzer).Export(SergioLoadPromoter.LastImportTableName, old.Workbook);
        Assert.False(previousExport.HasOutput);
        var next = Stage(workspace, analyzer, "new.xlsx", "new");
        var failing = new SergioLoadPromoter(analyzer, () => throw new InvalidOperationException("forced test failure"));

        Assert.Throws<InvalidOperationException>(() => failing.Promote(next.CandidateTableName, "CUIL", next.Workbook));
        using var verify = Open(workspace.DatabasePath);
        Assert.Equal("old", ReadValue(verify, "sergio_current", "CUIL"));
        Assert.Equal("old.xlsx", ReadValue(verify, "sergio_load_metadata", "source_file_name"));
        Assert.True(TableExists(verify, next.CandidateTableName));
    }

    [Fact]
    public void RealMetadataSchemaFailureRollsBackReplacementAndAllowsRetryAfterRepair()
    {
        using var workspace = new TestWorkspace();
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var previous = Stage(workspace, analyzer, "old.xlsx", "old");
        using (var connection = Open(workspace.DatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE sergio_current(CUIL VARCHAR); INSERT INTO sergio_current VALUES ('old'); CREATE TABLE sergio_load_metadata(source_file_name VARCHAR); INSERT INTO sergio_load_metadata VALUES ('old.xlsx'); CREATE TABLE unrelated(value VARCHAR); INSERT INTO unrelated VALUES ('untouched');";
            command.ExecuteNonQuery();
        }

        var next = Stage(workspace, analyzer, "new.xlsx", "new");
        var promoter = new SergioLoadPromoter(analyzer);

        Assert.ThrowsAny<Exception>(() => promoter.Promote(next.CandidateTableName, "CUIL", next.Workbook));

        using (var verify = Open(workspace.DatabasePath))
        {
        Assert.Equal("old", ReadValue(verify, SergioLoadPromoter.OfficialTableName, "CUIL"));
        Assert.Equal("old.xlsx", ReadValue(verify, SergioLoadPromoter.MetadataTableName, "source_file_name"));
        if (TableExists(verify, SergioLoadPromoter.LastImportTableName))
            Assert.Equal("old", ReadValue(verify, SergioLoadPromoter.LastImportTableName, "CUIL"));
        Assert.True(TableExists(verify, next.CandidateTableName));
        Assert.DoesNotContain(ListTableNames(verify), name => name.StartsWith("sergio_publication_", StringComparison.Ordinal));
        Assert.DoesNotContain(ListTableNames(verify), name => name.StartsWith("sergio_previous_", StringComparison.Ordinal));
        var retryableExport = new SergioRejectedExporter(analyzer).Export(next.CandidateTableName, next.Workbook);
        Assert.False(retryableExport.HasOutput);
            Assert.Equal("untouched", ReadValue(verify, "unrelated"));
        }

        using (var repair = Open(workspace.DatabasePath))
        using (var command = repair.CreateCommand())
        {
            command.CommandText = "DROP TABLE sergio_load_metadata; CREATE TABLE sergio_load_metadata(source_file_name VARCHAR, loaded_at_utc TIMESTAMP, row_count BIGINT, selected_primary_key_alias VARCHAR, schema_version VARCHAR);";
            command.ExecuteNonQuery();
        }

        promoter.Promote(next.CandidateTableName, "CUIL", next.Workbook);

        using var final = Open(workspace.DatabasePath);
        Assert.Equal("new", ReadValue(final, SergioLoadPromoter.OfficialTableName, "CUIL"));
        Assert.Equal("new.xlsx", ReadValue(final, SergioLoadPromoter.MetadataTableName, "source_file_name"));
        Assert.Equal("untouched", ReadValue(final, "unrelated"));
        Assert.False(TableExists(final, next.CandidateTableName));
    }

    [Fact]
    public void UnknownCandidateIsRejectedWithoutMutation()
    {
        using var workspace = new TestWorkspace();
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        Assert.Throws<InvalidOperationException>(() => new SergioLoadPromoter(analyzer).Preview("sergio_staging_unknown", "CUIL", "C:\\secret\\sergio.xlsx"));
        using var verify = Open(workspace.DatabasePath);
        Assert.False(TableExists(verify, "sergio_current"));
        Assert.False(TableExists(verify, "sergio_load_metadata"));
    }

    [Fact]
    public void ForgedDifferentSelectedKeyIsRejectedForPreviewAndPromotionWithoutMutation()
    {
        using var workspace = new TestWorkspace();
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var staged = analyzer.Analyze(new SergioStagingRequest(workspace.CreateWorkbook(["CUIL", "Nombre"], [["20-1", "Ana"]]), SergioHeaderPreflight.Analyze(["CUIL", "Nombre"]), "CUIL"));
        var promoter = new SergioLoadPromoter(analyzer);

        Assert.Throws<InvalidOperationException>(() => promoter.Preview(staged.CandidateTableName, "Nombre", staged.CandidateTableName));
        Assert.Throws<InvalidOperationException>(() => promoter.Promote(staged.CandidateTableName, "Nombre", staged.CandidateTableName));
        using var verify = Open(workspace.DatabasePath);
        Assert.True(TableExists(verify, staged.CandidateTableName));
        Assert.False(TableExists(verify, SergioLoadPromoter.OfficialTableName));
        analyzer.Cleanup(staged.CandidateTableName);
    }

    [Fact]
    public void ZeroRowCandidateIsRejectedWithoutTouchingOfficialOrMetadata()
    {
        using var workspace = new TestWorkspace();
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var old = Stage(workspace, analyzer, "old.xlsx", "old");
        var promoter = new SergioLoadPromoter(analyzer);
        promoter.Promote(old.CandidateTableName, "CUIL", old.Workbook);
        var empty = Stage(workspace, analyzer, "empty.xlsx", "empty");
        using (var connection = Open(workspace.DatabasePath))
        using (var command = connection.CreateCommand()) { command.CommandText = $"DELETE FROM \"{empty.CandidateTableName}\""; command.ExecuteNonQuery(); }

        Assert.Throws<InvalidOperationException>(() => promoter.Preview(empty.CandidateTableName, "CUIL", empty.Workbook));
        Assert.Throws<InvalidOperationException>(() => promoter.Promote(empty.CandidateTableName, "CUIL", empty.Workbook));
        using var verify = Open(workspace.DatabasePath);
        Assert.Equal("old", ReadValue(verify, SergioLoadPromoter.OfficialTableName, "CUIL"));
        Assert.Equal("old.xlsx", ReadValue(verify, SergioLoadPromoter.MetadataTableName, "source_file_name"));
        Assert.True(TableExists(verify, empty.CandidateTableName));
        analyzer.Cleanup(empty.CandidateTableName);
    }

    private static DuckDBConnection Open(string path) { var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString); connection.Open(); return connection; }
    private static bool TableExists(DuckDBConnection connection, string name) { using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = ?"; command.Parameters.Add(new DuckDBParameter { Value = name }); return Convert.ToInt64(command.ExecuteScalar()) > 0; }
    private static long CountRows(DuckDBConnection connection, string table) { using var command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM \"{table}\""; return Convert.ToInt64(command.ExecuteScalar()); }
    private static long CountReason(DuckDBConnection connection, string table, string column, string reason) { using var command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE \"{column}\" = ?"; command.Parameters.Add(new DuckDBParameter { Value = reason }); return Convert.ToInt64(command.ExecuteScalar()); }
    private static long CountColumns(DuckDBConnection connection, string table, string column) { using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = ? AND column_name = ?"; command.Parameters.Add(new DuckDBParameter { Value = table }); command.Parameters.Add(new DuckDBParameter { Value = column }); return Convert.ToInt64(command.ExecuteScalar()); }
    private static long CountPrimaryKeys(DuckDBConnection connection, string table, string column) { using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.table_constraints tc JOIN information_schema.key_column_usage ku ON tc.constraint_name = ku.constraint_name AND tc.table_name = ku.table_name WHERE tc.table_name = ? AND tc.constraint_type = 'PRIMARY KEY' AND ku.column_name = ?"; command.Parameters.Add(new DuckDBParameter { Value = table }); command.Parameters.Add(new DuckDBParameter { Value = column }); return Convert.ToInt64(command.ExecuteScalar()); }
    private static void Execute(DuckDBConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private static string ReadValue(DuckDBConnection connection, string table, string column = "value") { using var command = connection.CreateCommand(); command.CommandText = $"SELECT \"{column}\" FROM \"{table}\" LIMIT 1"; return (string)command.ExecuteScalar()!; }
    private static string? ReadNullableValue(DuckDBConnection connection, string table, string column) { using var command = connection.CreateCommand(); command.CommandText = $"SELECT \"{column}\" FROM \"{table}\" LIMIT 1"; return command.ExecuteScalar() as string; }
    private static IReadOnlyList<string> ListTableNames(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }
    private static (string CandidateTableName, string Workbook) Stage(TestWorkspace workspace, SergioStagingAnalyzer analyzer, string fileName, string value)
    {
        var workbook = workspace.CreateWorkbook(fileName, ["CUIL"], [[value]]);
        var result = analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL"]), "CUIL"));
        return (result.CandidateTableName, workbook);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(directory, "test.duckdb");
        public TestWorkspace() => Directory.CreateDirectory(directory);
        public string CreateWorkbook(string[] headers, string[][] rows) => CreateWorkbook("sergio.xlsx", headers, rows);
        public string CreateWorkbook(string fileName, string[] headers, string[][] rows)
        {
            var path = Path.Combine(directory, fileName);
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Sergio");
            for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
            for (var row = 0; row < rows.Length; row++) for (var column = 0; column < rows[row].Length; column++) sheet.Cell(row + 2, column + 1).Value = rows[row][column];
            workbook.SaveAs(path); return path;
        }
        public string CreateWorkbook(string[] headers, string[] rows)
        {
            return CreateWorkbook(headers, rows.Select(row => new[] { row }).ToArray());
        }
        public void Dispose() { try { Directory.Delete(directory, true); } catch { } }
    }
}
