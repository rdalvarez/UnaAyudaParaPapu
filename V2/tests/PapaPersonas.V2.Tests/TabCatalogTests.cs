using ExcelDataReader;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class TabCatalogTests
{
    [Fact]
    public void HeaderReaderUsesSinglePassConfiguration()
    {
        var configuration = HeaderReaderConfiguration.Create();

        Assert.True(configuration.SinglePassMode);
    }

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

    [Fact]
    public void CleaningPlanAddsOrderedStepsWithColumnIdentity()
    {
        var plan = new CleaningPlan();
        var cuit = new DetectedColumn(2, "CUIT");
        var codigoOs = new DetectedColumn(5, "CODIGO OS");

        plan.Add(CleaningAction.RemoveDuplicates, cuit);
        plan.Add(CleaningAction.RemoveEmptyValues, codigoOs);

        Assert.Collection(
            plan.Steps,
            first =>
            {
                Assert.Equal(CleaningAction.RemoveDuplicates, first.Action);
                Assert.Equal(2, first.ColumnPosition);
                Assert.Equal("CUIT", first.ColumnName);
                Assert.Equal(1, first.StepNumber);
            },
            second =>
            {
                Assert.Equal(CleaningAction.RemoveEmptyValues, second.Action);
                Assert.Equal(5, second.ColumnPosition);
                Assert.Equal("CODIGO OS", second.ColumnName);
                Assert.Equal(2, second.StepNumber);
            });
    }

    [Fact]
    public void CleaningPlanClearReturnsRemovedCountAndAllowsRepeatedSteps()
    {
        var plan = new CleaningPlan();
        var column = new DetectedColumn(0, "CUIT");

        plan.Add(CleaningAction.RemoveDuplicates, column);
        plan.Add(CleaningAction.RemoveDuplicates, column);

        Assert.Equal(2, plan.Clear());
        Assert.Empty(plan.Steps);
        Assert.Equal(0, plan.Clear());
    }

    [Fact]
    public void CleaningPlanVisibleRepresentationIsDeterministic()
    {
        var plan = new CleaningPlan();
        var column = new DetectedColumn(0, "CUIT");

        plan.Add(CleaningAction.RemoveDuplicates, column);
        plan.Add(CleaningAction.RemoveEmptyValues, column);

        Assert.Equal(
            ["1. Quitar duplicados por CUIT", "2. Quitar valores vacíos por CUIT"],
            plan.Steps.Select(step => step.VisibleDescription));
    }
}
