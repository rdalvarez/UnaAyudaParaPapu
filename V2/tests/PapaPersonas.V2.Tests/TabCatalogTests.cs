using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class TabCatalogTests
{
    [Fact]
    public void DefinesTheAuthorizedTabsInOrderWithOnlyPasoOneEnabled()
    {
        Assert.Equal(
            [
                "Preparar archivo de Hernán",
                "Ingresar archivo de Sergio",
                "Generación y consulta de stock",
                "Consultas (opcional)"
            ],
            TabCatalog.All.Select(tab => tab.Title));
        Assert.Equal([true, false, false, false], TabCatalog.All.Select(tab => tab.IsEnabled));
    }

    [Fact]
    public void DefaultRejectedFolderUsesInputDirectory()
    {
        Assert.Equal(Path.Combine("C:\\input", "rech"), RejectedFolderPolicy.GetDefault("C:\\input\\workbook.xlsx"));
    }

    [Fact]
    public void HeaderPresentationPreservesOrderAndCreatesUniqueNames()
    {
        var columns = HeaderPresentation.Normalize([" Name ", "", "name", "", "Age"]);

        Assert.Equal(["Name", "Columna sin nombre 1", "name (2)", "Columna sin nombre 2", "Age"], columns.Select(column => column.DisplayName));
        Assert.Equal([0, 1, 2, 3, 4], columns.Select(column => column.Position));
    }

    [Fact]
    public void ActivityLogIsNewestFirst()
    {
        var log = new ActivityLog();
        log.Add("first");
        log.Add("second");

        Assert.Equal(["second", "first"], log.Entries);
    }
}
