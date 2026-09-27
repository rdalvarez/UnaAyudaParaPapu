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
    public void DefinesTheAuthorizedTabsInOrderWithPasoOneAndSergioEnabled()
    {
        Assert.Equal(
            [
                "Preparar archivo de Hernán",
                "Ingresar archivo de Sergio",
                "Generación y consulta de stock",
                "Consultas (opcional)"
            ],
            TabCatalog.All.Select(tab => tab.Title));
        Assert.Equal([true, true, false, false], TabCatalog.All.Select(tab => tab.IsEnabled));
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

        Assert.Equal(["Name", "Columna sin nombre 1", "name", "Columna sin nombre 2", "Age"], columns.Select(column => column.DisplayName));
        Assert.Equal([0, 1, 2, 3, 4], columns.Select(column => column.Position));
    }

    [Fact]
    public void HeaderPresentationNumbersExactOrdinalDuplicatesFromOne()
    {
        var columns = HeaderPresentation.Normalize(["Nombre", "Nombre", "Nombre", "Nombre"]);

        Assert.Equal(["Nombre", "Nombre (1)", "Nombre (2)", "Nombre (3)"], columns.Select(column => column.DisplayName));
    }

    [Fact]
    public void HeaderPresentationAvoidsCollisionsWithSyntheticSuffixesDeterministically()
    {
        var columns = HeaderPresentation.Normalize(["Nombre", "Nombre (1)", "Nombre", "Nombre (1)"]);

        Assert.Equal(["Nombre", "Nombre (1)", "Nombre (1) (1)", "Nombre (1) (1) (1)"], columns.Select(column => column.DisplayName));
        Assert.Equal(columns.Select(column => column.DisplayName).Distinct(StringComparer.Ordinal).Count(), columns.Count);
    }

    [Fact]
    public void ActivityLogFormatsTimestampAndIsNewestFirst()
    {
        var log = new ActivityLog(() => new DateTime(2026, 9, 27, 14, 5, 6));
        log.Add("Paso 1", "first");
        log.Add("Paso 1", "second");

        Assert.Equal(
            ["[Paso 1][27/09/2026 14:05:06]second", "[Paso 1][27/09/2026 14:05:06]first"],
            log.Entries);
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

    [Fact]
    public void CleaningExecutionPreflightAcceptsIdenticalRawHeaders()
    {
        var plan = new CleaningPlan();
        plan.Add(CleaningAction.RemoveDuplicates, new DetectedColumn(0, "CUIT"));

        var result = CleaningExecutionPreflight.Prepare(["CUIT", null], ["CUIT", null], plan.Steps);

        Assert.True(result.IsValid);
        Assert.Null(result.FailureReason);
        Assert.Single(result.ExecutableSteps);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("order")]
    [InlineData("value")]
    public void CleaningExecutionPreflightRejectsHeaderChanges(string change)
    {
        var planning = new string?[] { "CUIT", "Name" };
        var current = change switch
        {
            "count" => new string?[] { "CUIT" },
            "order" => new string?[] { "Name", "CUIT" },
            "value" => new string?[] { "CUIT", "NAME" },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        var result = CleaningExecutionPreflight.Prepare(planning, current, []);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
        Assert.Empty(result.ExecutableSteps);
    }

    [Fact]
    public void CleaningExecutionPreflightTreatsNullAndValueAsDifferentHeaders()
    {
        var result = CleaningExecutionPreflight.Prepare([null], [""], []);

        Assert.False(result.IsValid);
        Assert.Contains("<null>", result.FailureReason);
    }

    [Fact]
    public void CleaningExecutionPreflightDeduplicatesOnlyTheSameActionAndPosition()
    {
        var plan = new CleaningPlan();
        var column = new DetectedColumn(2, "CUIT");
        plan.Add(CleaningAction.RemoveDuplicates, column);
        plan.Add(CleaningAction.RemoveDuplicates, column);
        plan.Add(CleaningAction.RemoveEmptyValues, column);

        var result = CleaningExecutionPreflight.Prepare(["A", "B", "CUIT"], ["A", "B", "CUIT"], plan.Steps);

        Assert.True(result.IsValid);
        Assert.Equal(
            [CleaningAction.RemoveDuplicates, CleaningAction.RemoveEmptyValues],
            result.ExecutableSteps.Select(step => step.Action));
        Assert.Equal([1, 2], result.ExecutableSteps.Select(step => step.StepNumber));
        var omitted = Assert.Single(result.OmittedSteps);
        Assert.Equal(2, omitted.Step.StepNumber);
        Assert.Contains("Repeated", omitted.Reason);
    }

    [Fact]
    public void CleaningExecutionPreflightRejectsOutOfRangeColumnPosition()
    {
        var steps = new[] { new CleaningPlanStep(CleaningAction.RemoveDuplicates, 2, "Missing", 1) };

        var result = CleaningExecutionPreflight.Prepare(["CUIT", "Name"], ["CUIT", "Name"], steps);

        Assert.False(result.IsValid);
        Assert.Contains("position 2", result.FailureReason);
        Assert.Empty(result.ExecutableSteps);
    }

    [Fact]
    public void CleaningExecutionPreflightDoesNotMutateInputs()
    {
        var planning = new List<string?> { "CUIT", null };
        var current = new List<string?> { "CUIT", null };
        var plan = new CleaningPlan();
        plan.Add(CleaningAction.RemoveDuplicates, new DetectedColumn(0, "CUIT"));
        plan.Add(CleaningAction.RemoveDuplicates, new DetectedColumn(0, "CUIT"));
        var originalSteps = plan.Steps.ToArray();

        _ = CleaningExecutionPreflight.Prepare(planning, current, plan.Steps);

        Assert.Equal(["CUIT", null], planning);
        Assert.Equal(["CUIT", null], current);
        Assert.Equal(originalSteps, plan.Steps);
    }
}
