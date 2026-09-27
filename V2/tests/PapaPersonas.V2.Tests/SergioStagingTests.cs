using ClosedXML.Excel;
using DuckDB.NET.Data;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class SergioStagingTests
{
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
    public void DuplicateTrimmedKeyLeavesNoCandidateAndPreservesUnrelatedTable()
    {
        using var workspace = new TestWorkspace();
        using (var connection = Open(workspace.DatabasePath))
        using (var command = connection.CreateCommand()) { command.CommandText = "CREATE TABLE unrelated(value VARCHAR)"; command.ExecuteNonQuery(); }
        var workbook = workspace.CreateWorkbook(["CUIL"], [" 20-1 ", "20-1"]);
        var headers = SergioHeaderPreflight.Analyze(["CUIL"]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));

        var result = analyzer.Analyze(new SergioStagingRequest(workbook, headers, "CUIL"));

        Assert.False(result.IsValid);
        Assert.Equal(string.Empty, result.CandidateTableName);
        using var verify = Open(workspace.DatabasePath);
        Assert.True(TableExists(verify, "unrelated"));
    }

    private static DuckDBConnection Open(string path) { var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString); connection.Open(); return connection; }
    private static bool TableExists(DuckDBConnection connection, string name) { using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = ?"; command.Parameters.Add(new DuckDBParameter { Value = name }); return Convert.ToInt64(command.ExecuteScalar()) > 0; }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(directory, "test.duckdb");
        public TestWorkspace() => Directory.CreateDirectory(directory);
        public string CreateWorkbook(string[] headers, string[][] rows)
        {
            var path = Path.Combine(directory, "sergio.xlsx");
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
