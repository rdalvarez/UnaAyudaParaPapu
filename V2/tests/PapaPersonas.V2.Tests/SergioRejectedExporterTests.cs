using ClosedXML.Excel;
using PapaPersonas.V2.Shell;
using System.Text;
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

    [Fact]
    public async Task ConcurrentPublishersUseDifferentFinalsWithoutPartials()
    {
        using var workspace = new ExportWorkspace();
        var barrier = new Barrier(2);
        var timestamp = new DateTime(2026, 9, 27, 12, 30, 45);
        var publisher = new SergioRejectedFilePublisher();

        var paths = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(() =>
            publisher.Publish(workspace.WorkbookPath, timestamp, stream =>
            {
                barrier.SignalAndWait();
                using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
                writer.Write($"publisher-{index}\r\n");
                writer.Flush();
            }))));

        Assert.Equal(2, paths.Distinct(StringComparer.Ordinal).Count());
        Assert.All(paths, path => Assert.Contains($"publisher-", File.ReadAllText(path)));
        Assert.Empty(Directory.EnumerateFiles(workspace.RejectedDirectory, "*.partial"));
    }

    [Fact]
    public void PublisherFailureCleansItsPartialWithoutCreatingFinal()
    {
        using var workspace = new ExportWorkspace();
        var publisher = new SergioRejectedFilePublisher();

        Assert.Throws<InvalidOperationException>(() => publisher.Publish(workspace.WorkbookPath,
            new DateTime(2026, 9, 27, 12, 30, 45), _ => throw new InvalidOperationException("writer fault")));

        Assert.False(Directory.Exists(workspace.RejectedDirectory) &&
            Directory.EnumerateFileSystemEntries(workspace.RejectedDirectory).Any());
    }

    private sealed class ExportWorkspace : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public ExportWorkspace() => Directory.CreateDirectory(directory);
        public string DatabasePath => Path.Combine(directory, "test.duckdb");
        public string RejectedDirectory => Path.Combine(directory, "rech");
        public string WorkbookPath => Path.Combine(directory, "sergio.xlsx");
        public string Workbook(string[] headers, string[][] rows)
        {
            var path = WorkbookPath;
            using var book = new XLWorkbook();
            var sheet = book.AddWorksheet("Sergio");
            for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
            for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = rows[r][c];
            book.SaveAs(path); return path;
        }
        public void Dispose() { try { Directory.Delete(directory, true); } catch { } }
    }
}
