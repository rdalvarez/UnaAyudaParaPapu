using System.Text;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class TemporaryTransportTests
{
    [Fact]
    public void Workspace_prepares_deterministic_paths_and_cleans_idempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var workspace = new FixedTemporaryWorkspace(root);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "stale.txt"), "stale");
            workspace.Prepare();

            Assert.False(File.Exists(Path.Combine(root, "stale.txt")));
            Assert.Equal(Path.Combine(root, "rows.csv"), workspace.GetPath("rows.csv"));
            Assert.Throws<ArgumentException>(() => workspace.GetPath(Path.Combine(root, "outside.csv")));
            Assert.Throws<ArgumentException>(() => workspace.GetPath(".." + Path.DirectorySeparatorChar + "outside.csv"));
            workspace.Cleanup();
            workspace.Cleanup();
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Csv_round_trip_preserves_values_and_writes_utf8_bom()
    {
        var root = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "roundtrip.csv");
        var expected = new[] { "simple", "", " surrounding ", "comma,value", "quote\"value", "line\r\nvalue", "á", "ü", "ñ", "Ü" };
        try
        {
            using (var writer = StreamingCsvWriter.Open(path))
            {
                writer.WriteRow(expected);
            }
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);

            using (var reader = StreamingCsvReader.Open(path))
            {
                Assert.True(reader.Read());
                Assert.Equal(expected, reader.CurrentRow);
                Assert.False(reader.Read());
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
