using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class CleaningCycleProcessorTests
{
    [Fact]
    public void Projector_uses_original_positions_and_preserves_csv_values()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        var workspace = new FixedTemporaryWorkspace(root);
        try
        {
            workspace.Prepare();
            var current = workspace.GetPath("current.csv");
            using (var writer = StreamingCsvWriter.Open(current))
            {
                writer.WriteRow(["", "Name", "Name", "Unicode"]);
                writer.WriteRow(["zero", "a,b", "quoted \"value\"", "á\r\nβ"]);
            }

            var projected = new CleanOutputProjector().Project(workspace, current,
                ["", "Name", "Name", "Unicode"], [3, 0, 2]);

            using var reader = StreamingCsvReader.Open(projected);
            Assert.True(reader.Read());
            Assert.Equal(["", "Name", "Unicode"], reader.CurrentRow);
            Assert.True(reader.Read());
            Assert.Equal(["zero", "quoted \"value\"", "á\r\nβ"], reader.CurrentRow);
            Assert.False(reader.Read());
            Assert.Equal(0xEF, File.ReadAllBytes(projected)[0]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(new[] { 0, 0 })]
    [InlineData(new[] { -1 })]
    [InlineData(new[] { 2 })]
    [InlineData(new int[0])]
    public void Projector_rejects_invalid_selection_before_processing(int[] positions)
    {
        Assert.Throws<ArgumentException>(() => CleanOutputProjector.ValidateAndNormalize(2, positions));
    }

    [Fact]
    public void Empty_values_reject_whitespace_and_preserve_full_rows()
    {
        using var fixture = CreateFixture(["Name", "Value"], [["a", "  "], ["b", " keep "]]);
        var result = fixture.Process(new CleaningPlanStep(CleaningAction.RemoveEmptyValues, 1, "Value", 7));
        Assert.Equal((2L, 1L, 1L), Counts(result));
        Assert.Equal(["PP_MOTIVO,PP_PASO,PP_COLUMNA,PP_VALOR,Name,Value", "Valor vacío,7,Value,\"  \",a,\"  \""], File.ReadAllLines(result.RejectedPath));
        Assert.Equal(["Name,Value", "b,\" keep \""], File.ReadAllLines(result.CurrentPath));
    }

    [Fact]
    public void Duplicates_trim_case_insensitively_and_keep_empty_and_unique_values()
    {
        using var fixture = CreateFixture(["Name", "Value"], [["a", " x"], ["b", "X "], ["c", ""], ["d", "unique"]]);
        var result = fixture.Process(new CleaningPlanStep(CleaningAction.RemoveDuplicates, 1, "Value", 1));
        Assert.Equal((4L, 2L, 2L), Counts(result));
        Assert.Equal(["Name,Value", "c,\"\"", "d,unique"], File.ReadAllLines(result.CurrentPath));
    }

    [Fact]
    public void Cycles_are_sequential_and_rejections_are_consolidated_once()
    {
        using var fixture = CreateFixture(["A", "B"], [["x", ""], ["x", "ok"], ["y", "ok"]]);
        var result = fixture.Process(new CleaningPlanStep(CleaningAction.RemoveDuplicates, 0, "A", 1), new CleaningPlanStep(CleaningAction.RemoveEmptyValues, 1, "B", 2));
        Assert.Equal(2, result.Cycles.Count);
        Assert.Equal((3L, 2L, 1L), Counts(result.Cycles[0]));
        Assert.Equal((1L, 0L, 1L), Counts(result.Cycles[1]));
        Assert.Equal(3, File.ReadAllLines(result.RejectedPath).Length);
    }

    [Fact]
    public void Zero_rejections_write_header_only_and_technical_collisions_are_unique()
    {
        using var fixture = CreateFixture(["PP_MOTIVO", "pp_motivo", "PP_MOTIVO__SOURCE", "A", "A"], [["one", "two", "three", "v", "v2"]]);
        var result = fixture.Process();
        Assert.Equal("PP_MOTIVO,PP_PASO,PP_COLUMNA,PP_VALOR,PP_MOTIVO__SOURCE_2,pp_motivo__SOURCE_3,PP_MOTIVO__SOURCE,A,A", File.ReadAllLines(result.RejectedPath).Single());
    }

    [Fact]
    public void Malformed_rows_remove_partial_outputs_and_throw_domain_exception()
    {
        using var fixture = CreateFixture(["A", "B"], [["one", "two", "extra"]]);
        var exception = Assert.Throws<CleaningCycleException>(() => fixture.Process(new CleaningPlanStep(CleaningAction.RemoveEmptyValues, 0, "A", 1)));
        Assert.Contains("Malformed CSV", exception.Message);
        Assert.False(File.Exists(fixture.Workspace.GetPath("next.csv")));
        Assert.False(File.Exists(fixture.Workspace.GetPath("rejected.csv")));
    }

    [Fact]
    public void Single_column_empty_row_is_transportable_and_rejected()
    {
        using var fixture = CreateFixture(["A"], [[""]]);
        var result = fixture.Process(new CleaningPlanStep(CleaningAction.RemoveEmptyValues, 0, "A", 1));
        Assert.Equal(2, File.ReadAllLines(result.RejectedPath).Length);
    }

    private static (long Input, long Rejected, long Remaining) Counts(CleaningProcessingResult result) => Counts(result.Cycles.Single());
    private static (long Input, long Rejected, long Remaining) Counts(CleaningCycleResult result) => (result.InputRows, result.RejectedRows, result.RemainingRows);

    private static Fixture CreateFixture(IReadOnlyList<string?> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        var workspace = new FixedTemporaryWorkspace(root);
        workspace.Prepare();
        var current = workspace.GetPath("current.csv");
        using (var writer = StreamingCsvWriter.Open(current))
        {
            writer.WriteRow(headers);
            foreach (var row in rows) writer.WriteRow(row);
        }
        return new Fixture(root, workspace, headers, current);
    }

    private sealed class Fixture(string root, FixedTemporaryWorkspace workspace, IReadOnlyList<string?> headers, string current) : IDisposable
    {
        public FixedTemporaryWorkspace Workspace { get; } = workspace;
        public CleaningProcessingResult Process(params CleaningPlanStep[] steps) => new CleaningCycleProcessor().Process(Workspace, headers, steps, current);
        public void Dispose()
        {
            try { Workspace.Cleanup(); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
