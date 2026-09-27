using ClosedXML.Excel;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class SergioRejectedExporterTests
{
    [Fact]
    public void ExportsOnlyRejectedRowsWithPublicHeaderAndCsvEscaping()
    {
        using var workspace = new ExportWorkspace();
        var workbook = workspace.Workbook(["CUIL", "Nombre", "Nota"],
            [["20-1", "Ana, \"A\"", "línea\r\nuno"], ["20-1", "Beto", null!], ["20-2", "Ok", "no"]]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var staged = analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL", "Nombre", "Nota"]), "CUIL"));

        var result = new SergioRejectedExporter(analyzer, clock: () => new DateTime(2026, 9, 27, 12, 30, 45)).Export(staged.CandidateTableName, workbook);

        Assert.True(result.HasOutput);
        Assert.Equal(2, result.RejectedCount);
        var bytes = File.ReadAllBytes(result.OutputPath!);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = File.ReadAllText(result.OutputPath!);
        Assert.Contains("PP_MOTIVO,CUIL,Nombre,Nota", text);
        Assert.DoesNotContain(SergioSchemaNames.RejectionReason, text.Split('\n')[0]);
        Assert.Contains("\"Ana, \"\"A\"\"\"", text);
        Assert.Contains("línea", text);
        Assert.DoesNotContain("Ok", text);
        Assert.True(Directory.Exists(workspace.RejectedDirectory));
        analyzer.Cleanup(staged.CandidateTableName);
    }

    [Fact]
    public void NoRejectedRowsProducesNoFileOrFolderAndCollisionGetsSuffix()
    {
        using var workspace = new ExportWorkspace();
        var workbook = workspace.Workbook(["CUIL"], [["20-1"]]);
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath));
        var staged = analyzer.Analyze(new SergioStagingRequest(workbook, SergioHeaderPreflight.Analyze(["CUIL"]), "CUIL"));
        var exporter = new SergioRejectedExporter(analyzer, clock: () => new DateTime(2026, 9, 27, 12, 30, 45));

        var empty = exporter.Export(staged.CandidateTableName, workbook);
        Assert.False(empty.HasOutput);
        Assert.False(Directory.Exists(workspace.RejectedDirectory));
        analyzer.Cleanup(staged.CandidateTableName);

        var rejected = analyzer.Analyze(new SergioStagingRequest(workbook = workspace.Workbook(["CUIL"], [["20-1"], ["20-1"]]), SergioHeaderPreflight.Analyze(["CUIL"]), "CUIL"));
        var first = exporter.Export(rejected.CandidateTableName, workbook);
        var second = exporter.Export(rejected.CandidateTableName, workbook);
        Assert.EndsWith(".csv", first.OutputPath!);
        Assert.EndsWith("_2.csv", second.OutputPath!);
        Assert.NotEqual(first.OutputPath, second.OutputPath);
        analyzer.Cleanup(rejected.CandidateTableName);
    }

    private sealed class ExportWorkspace : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public ExportWorkspace() => Directory.CreateDirectory(directory);
        public string DatabasePath => Path.Combine(directory, "test.duckdb");
        public string RejectedDirectory => Path.Combine(directory, "rech");
        public string Workbook(string[] headers, string[][] rows)
        {
            var path = Path.Combine(directory, "sergio.xlsx");
            using var book = new XLWorkbook();
            var sheet = book.AddWorksheet("Sergio");
            for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
            for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = rows[r][c];
            book.SaveAs(path); return path;
        }
        public void Dispose() { try { Directory.Delete(directory, true); } catch { } }
    }
}
