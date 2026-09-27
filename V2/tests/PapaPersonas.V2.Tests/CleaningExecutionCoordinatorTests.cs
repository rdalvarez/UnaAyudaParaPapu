using ClosedXML.Excel;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class CleaningExecutionCoordinatorTests
{
    [Fact]
    public void Real_workbook_is_cleaned_published_with_bom_and_workspace_removed()
    {
        var root = CreateRoot();
        var input = Path.Combine(root, "people.xlsx");
        var rejected = Path.Combine(root, "custom-rejected");
        var workspaceRoot = Path.Combine(root, "workspace");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("People");
                sheet.Cell(1, 1).Value = "Name";
                sheet.Cell(1, 2).Value = "Value";
                sheet.Cell(2, 1).Value = "a"; sheet.Cell(2, 2).Value = " x";
                sheet.Cell(3, 1).Value = "b"; sheet.Cell(3, 2).Value = "X ";
                sheet.Cell(4, 1).Value = "c"; sheet.Cell(4, 2).Value = "";
                sheet.Cell(5, 1).Value = "d"; sheet.Cell(5, 2).Value = "keep";
                workbook.SaveAs(input);
            }

            var result = new CleaningExecutionCoordinator(
                () => new FixedTemporaryWorkspace(workspaceRoot),
                clock: () => new DateTime(2026, 9, 26, 18, 30, 45))
                .Execute(new CleaningExecutionRequest(input, rejected, ["Name", "Value"],
                [new(CleaningAction.RemoveDuplicates, 1, "Value", 1), new(CleaningAction.RemoveEmptyValues, 1, "Value", 2)]));

            Assert.Equal(Path.Combine(root, "people_limpio_20260926_183045.csv"), result.CleanOutputPath);
            Assert.Equal(Path.Combine(rejected, "people_rechazados_20260926_183045.csv"), result.RejectedOutputPath);
            Assert.Equal(4, result.StagedInputRowCount);
            Assert.Equal(2, result.Cycles.Count);
            Assert.Equal(["Name,Value", "d,keep"], File.ReadAllLines(result.CleanOutputPath));
            Assert.Equal(4, File.ReadAllLines(result.RejectedOutputPath).Length);
            Assert.Equal(0xEF, File.ReadAllBytes(result.CleanOutputPath)[0]);
            Assert.False(Directory.Exists(workspaceRoot));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void Existing_pair_uses_shared_suffix_and_preserves_old_content()
    {
        var root = CreateRoot();
        var input = Path.Combine(root, "people.xlsx");
        var rejected = Path.Combine(root, "rech");
        try
        {
            File.WriteAllText(input, "not used");
            Directory.CreateDirectory(rejected);
            var oldClean = Path.Combine(root, "people_limpio_20260926_183045.csv");
            var oldRejected = Path.Combine(rejected, "people_rechazados_20260926_183045.csv");
            File.WriteAllText(oldClean, "old-clean"); File.WriteAllText(oldRejected, "old-rejected");
            var current = Path.Combine(root, "workspace");
            var result = new CleaningExecutionCoordinator(
                () => new FixedTemporaryWorkspace(current),
                cursorFactory: new FakeFactory(["A"], [["ok"]]),
                clock: () => new DateTime(2026, 9, 26, 18, 30, 45))
                .Execute(new CleaningExecutionRequest(input, rejected, ["A"],
                [new(CleaningAction.RemoveEmptyValues, 0, "A", 1)]));

            Assert.EndsWith("_2.csv", result.CleanOutputPath);
            Assert.EndsWith("_2.csv", result.RejectedOutputPath);
            Assert.Equal("old-clean", File.ReadAllText(oldClean));
            Assert.Equal("old-rejected", File.ReadAllText(oldRejected));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void Empty_plan_fails_before_workspace_prepare()
    {
        var root = CreateRoot();
        var workspace = Path.Combine(root, "workspace");
        try
        {
            var exception = Assert.Throws<ArgumentException>(() => new CleaningExecutionCoordinator(
                () => new FixedTemporaryWorkspace(workspace)).Execute(
                new CleaningExecutionRequest("input.xlsx", root, ["A"], [])));
            Assert.Contains("at least one step", exception.Message);
            Assert.False(Directory.Exists(workspace));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void Header_mismatch_and_malformed_rows_leave_no_outputs_or_workspace()
    {
        var root = CreateRoot();
        var input = Path.Combine(root, "people.xlsx");
        var workspace = Path.Combine(root, "workspace");
        try
        {
            File.WriteAllText(input, "not used");
            var mismatch = Assert.Throws<WorkbookStagingException>(() => new CleaningExecutionCoordinator(
                () => new FixedTemporaryWorkspace(workspace), cursorFactory: new FakeFactory(["Changed"], [["x"]]))
                .Execute(new CleaningExecutionRequest(input, root, ["A"],
                [new(CleaningAction.RemoveEmptyValues, 0, "A", 1)])));
            Assert.Contains("Header snapshot mismatch", mismatch.Message);
            Assert.Empty(Directory.GetFiles(root, "*.csv", SearchOption.AllDirectories));
            Assert.False(Directory.Exists(workspace));

            var malformedWorkspace = Path.Combine(root, "workspace-malformed");
            var malformed = Assert.Throws<WorkbookStagingException>(() => new CleaningExecutionCoordinator(
                () => new FixedTemporaryWorkspace(malformedWorkspace), cursorFactory: new FakeFactory(["A"], [["one", "extra"]]))
                .Execute(new CleaningExecutionRequest(input, root, ["A"],
                [new(CleaningAction.RemoveEmptyValues, 0, "A", 1)])));
            Assert.Contains("contains 2 field(s)", malformed.Message);
            Assert.Empty(Directory.GetFiles(root, "*.csv", SearchOption.AllDirectories));
            Assert.False(Directory.Exists(malformedWorkspace));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void Publication_failure_removes_partial_files_and_preserves_existing_outputs()
    {
        var root = CreateRoot();
        var rejected = Path.Combine(root, "rech");
        var workspace = Path.Combine(root, "workspace");
        var clean = Path.Combine(root, "people_limpio_20260926_183045.csv");
        var oldRejected = Path.Combine(rejected, "people_rechazados_20260926_183045.csv");
        try
        {
            Directory.CreateDirectory(rejected);
            File.WriteAllText(clean, "old-clean");
            File.WriteAllText(oldRejected, "old-rejected");
            var stagedClean = Path.Combine(root, "staged-clean.csv");
            var stagedRejected = Path.Combine(root, "staged-rejected.csv");
            File.WriteAllText(stagedClean, "new-clean");

            var exception = Assert.Throws<FileNotFoundException>(() => new SafeCleaningOutputPublisher().Publish(
                Path.Combine(root, "people.xlsx"), rejected, stagedClean, stagedRejected,
                new DateTime(2026, 9, 26, 18, 30, 45)));

            Assert.NotNull(exception);
            Assert.Equal("old-clean", File.ReadAllText(clean));
            Assert.Equal("old-rejected", File.ReadAllText(oldRejected));
            Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
        }
        finally { DeleteRoot(root); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class FakeFactory(IReadOnlyList<string?> headers, IReadOnlyList<IReadOnlyList<object?>> rows) : IWorkbookRowCursorFactory
    {
        public IWorkbookRowCursor Open(string inputPath) => new FakeCursor(headers, rows);
    }

    private sealed class FakeCursor(IReadOnlyList<string?> headers, IReadOnlyList<IReadOnlyList<object?>> rows) : IWorkbookRowCursor
    {
        private int index = -1;
        public IReadOnlyList<string?> Headers { get; } = headers;
        public IReadOnlyList<object?> CurrentRow { get; private set; } = [];
        public bool Read() => ++index < rows.Count && (CurrentRow = rows[index]) is not null;
        public void Dispose() { }
    }
}
