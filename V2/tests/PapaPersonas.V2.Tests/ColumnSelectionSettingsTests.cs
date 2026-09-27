using System.Text.Json;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class ColumnSelectionSettingsTests
{
    [Fact]
    public void MissingSettingsUseDefaultsWithoutWriting()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var result = new ColumnSelectionSettingsStore(path).Load();

            Assert.False(result.WasReset);
            Assert.Empty(result.Document.ConfiguracionPaso1.DeselectedColumnNames);
            Assert.False(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void SelectionMemoryUsesExactVisualNamesAndIgnoresUnknownNames()
    {
        var columns = HeaderPresentation.Normalize(["Email", "email", "Nombre", "Nombre"]);
        var selected = columns.ToDictionary(column => column.DisplayName, _ => true, StringComparer.Ordinal);
        var applied = ColumnSelectionMemory.ApplyDeselected(columns, ["Nombre (1)", "Email", "Missing", "email"], (column, value) => selected[column.DisplayName] = value);

        Assert.Equal(3, applied);
        Assert.False(selected["Email"]);
        Assert.False(selected["email"]);
        Assert.True(selected["Nombre"]);
        Assert.False(selected["Nombre (1)"]);
    }

    [Fact]
    public void CaptureReturnsDistinctDeselectedNamesInVisualOrder()
    {
        var columns = HeaderPresentation.Normalize(["A", "B", "C"]);

        var result = ColumnSelectionMemory.CaptureDeselected(columns, column => column.DisplayName != "B");

        Assert.Equal(["B"], result);
    }

    [Fact]
    public void SaveUsesExactKeysAndDeterministicOrder()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "settings.json");
            new ColumnSelectionSettingsStore(path).Save(new ColumnSelectionSettingsDocument
            {
                ConfiguracionPaso1 = new Paso1ColumnSelectionSettings
                {
                    DeselectedColumnNames = ["Nombre (1)", "Email", "Nombre (1)", "email"]
                }
            });

            Assert.Equal("{\n  \"configuracionPaso1\": {\n    \"columnasDeseleccionadas\": [\n      \"Nombre (1)\",\n      \"Email\",\n      \"email\"\n    ]\n  }\n}", File.ReadAllText(path).Replace("\r\n", "\n"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void CorruptSettingsAreReplacedAndReportedAsReset()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "settings.json");
            File.WriteAllText(path, "{ not json");

            var result = new ColumnSelectionSettingsStore(path).Load();

            Assert.True(result.WasReset);
            Assert.Empty(result.Document.ConfiguracionPaso1.DeselectedColumnNames);
            Assert.Equal([], JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("configuracionPaso1").GetProperty("columnasDeseleccionadas").EnumerateArray().Select(item => item.GetString()));
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
}
