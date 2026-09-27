using ClosedXML.Excel;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class WorkbookStagingTests
{
    [Fact]
    public void Matching_headers_stage_rows_incrementally_and_pad_missing_cells()
    {
        var cursor = new FakeCursor(["A", "B"], [[1, "two"], [3]]);
        var result = Stage(cursor, ["A", "B"]);

        Assert.Equal(2, result.DataRowCount);
        Assert.Equal(["A,B", "1,two", "3,"], ReadRows(result.StagedPath));
    }

    [Fact]
    public void Header_mismatch_creates_no_current_file()
    {
        var cursor = new FakeCursor(["A", "changed"], [[1, 2]]);
        var exception = Assert.Throws<WorkbookStagingException>(() => Stage(cursor, ["A", "B"]));

        Assert.Contains("Header snapshot mismatch", exception.Message);
        Assert.False(File.Exists(cursor.OutputPath));
    }

    [Fact]
    public void Invalid_step_creates_no_current_file()
    {
        var cursor = new FakeCursor(["A"], [[1]]);
        var step = new CleaningPlanStep(CleaningAction.RemoveDuplicates, 2, "missing", 1);
        var exception = Assert.Throws<WorkbookStagingException>(() => Stage(cursor, ["A"], [step]));

        Assert.Contains("position 2", exception.Message);
        Assert.False(File.Exists(cursor.OutputPath));
    }

    [Fact]
    public void Extra_data_columns_fail_and_remove_partial_output()
    {
        var cursor = new FakeCursor(["A"], [[1, 2]]);
        var exception = Assert.Throws<WorkbookStagingException>(() => Stage(cursor, ["A"]));

        Assert.Contains("contains 2 field(s)", exception.Message);
        Assert.False(File.Exists(cursor.OutputPath));
    }

    [Fact]
    public void Plan_and_header_snapshots_are_not_mutated()
    {
        var headers = new List<string?> { "A" };
        var plan = new CleaningPlan();
        plan.Add(CleaningAction.RemoveDuplicates, new DetectedColumn(0, "A"));
        var originalStep = plan.Steps.Single();
        var cursor = new FakeCursor(["A"], [[1]]);

        _ = Stage(cursor, headers, plan.Steps);

        Assert.Equal(["A"], headers);
        Assert.Same(originalStep, plan.Steps.Single());
    }

    [Fact]
    public void Excel_cursor_reads_first_sheet_and_formats_unicode_date_and_number_values()
    {
        var root = CreateRoot();
        var workbookPath = Path.Combine(root, "input.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("Primera");
                sheet.Cell(1, 1).Value = "Nombre";
                sheet.Cell(1, 2).Value = "Fecha";
                sheet.Cell(1, 3).Value = "Número";
                sheet.Cell(2, 1).Value = "José 测试";
                sheet.Cell(2, 2).Value = new DateTime(2026, 9, 26);
                sheet.Cell(2, 3).Value = 12.5;
                workbook.Worksheets.Add("Segunda").Cell(1, 1).Value = "ignored";
                workbook.SaveAs(workbookPath);
            }

            var workspace = new FixedTemporaryWorkspace(Path.Combine(root, "workspace"));
            workspace.Prepare();
            var result = new WorkbookStager().Stage(
                workbookPath, ["Nombre", "Fecha", "Número"], [], workspace,
                new ExcelDataReaderWorkbookRowCursorFactory());

            Assert.Equal(1, result.DataRowCount);
            Assert.Equal(
                ["Nombre,Fecha,Número", "José 测试,2026-09-26,12.5"],
                ReadRows(result.StagedPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static WorkbookStagingResult Stage(
        FakeCursor cursor,
        IReadOnlyList<string?> planningHeaders,
        IReadOnlyList<CleaningPlanStep>? steps = null)
    {
        var workspace = new FixedTemporaryWorkspace(cursor.WorkspacePath);
        workspace.Prepare();
        return new WorkbookStager().Stage("fake.xlsx", planningHeaders, steps ?? [], workspace, new FakeFactory(cursor));
    }

    private static string[] ReadRows(string path) => File.ReadAllLines(path);

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeFactory(FakeCursor cursor) : IWorkbookRowCursorFactory
    {
        public IWorkbookRowCursor Open(string inputPath) => cursor;
    }

    private sealed class FakeCursor(IReadOnlyList<string?> headers, IReadOnlyList<IReadOnlyList<object?>> rows) : IWorkbookRowCursor
    {
        private int index = -1;
        public string WorkspacePath { get; } = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public string OutputPath => Path.Combine(WorkspacePath, "current.csv");
        public IReadOnlyList<string?> Headers { get; } = headers;
        public IReadOnlyList<object?> CurrentRow { get; private set; } = [];
        public bool Read() => ++index < rows.Count && (CurrentRow = rows[index]) is not null;
        public void Dispose() { }
    }
}
