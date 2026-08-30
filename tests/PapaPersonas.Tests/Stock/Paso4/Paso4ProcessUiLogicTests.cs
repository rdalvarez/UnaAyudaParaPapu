using PapaPersonas.Core.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class Paso4ProcessUiLogicTests
{
    [Theory]
    [InlineData(null, "(Vacío)")]
    [InlineData("", "(Vacío)")]
    [InlineData("   ", "(Vacío)")]
    [InlineData("OSDE", "OSDE")]
    public void EmptyDisplay_UsesVacioPlaceholder(string? value, string expected)
    {
        Assert.Equal(expected, Paso4ProcessUiLogic.ToDisplayValue(value));
    }

    [Fact]
    public void RequestedGroups_ParsesPositiveRowsAndRejectsInvalidOrNegative()
    {
        var ok = Paso4ProcessUiLogic.BuildGroupRequests(
            [
                new Paso4QuantityInput("OS1", "PLAN A", "2"),
                new Paso4QuantityInput("OS2", "PLAN B", "0"),
                new Paso4QuantityInput("OS3", "PLAN C", "")
            ],
            out var message);

        Assert.Null(message);
        Assert.Single(ok);
        Assert.Equal(2, ok[0].Quantity);

        var invalid = Paso4ProcessUiLogic.BuildGroupRequests(
            [new Paso4QuantityInput("OS1", "PLAN A", "-1")],
            out var invalidMessage);

        Assert.Empty(invalid);
        Assert.Equal("Las cantidades deben ser números enteros no negativos.", invalidMessage);
    }

    [Fact]
    public void FooterTotals_ComputesSelectedGroupsAndPeople()
    {
        var totals = Paso4ProcessUiLogic.ComputeRequestTotals(
            [
                new Paso4QuantityInput("OS1", "PLAN A", "3"),
                new Paso4QuantityInput("OS2", "PLAN B", "0"),
                new Paso4QuantityInput("OS3", "PLAN C", "1")
            ]);

        Assert.Equal(2, totals.SelectedGroups);
        Assert.Equal(4, totals.TotalPeopleRequested);
    }

    [Fact]
    public void ClearQuantities_ResetsAllRowsToEmpty()
    {
        var cleared = Paso4ProcessUiLogic.ClearQuantities(
            [
                new Paso4QuantityInput("OS1", "PLAN A", "3"),
                new Paso4QuantityInput("OS2", "PLAN B", "1")
            ]);

        Assert.All(cleared, row => Assert.Equal(string.Empty, row.QuantityText));
    }

    [Fact]
    public void ControlState_ReflectsBusyPendingAndStockPresence()
    {
        var pending = Paso4ProcessUiLogic.EvaluateControlState(isBusy: false, hasPending: true, hasStock: true);
        Assert.False(pending.CanGenerateOrRegenerate);
        Assert.False(pending.CanExtract);

        var empty = Paso4ProcessUiLogic.EvaluateControlState(isBusy: false, hasPending: false, hasStock: false);
        Assert.True(empty.CanGenerateOrRegenerate);
        Assert.False(empty.CanExtract);

        var active = Paso4ProcessUiLogic.EvaluateControlState(isBusy: false, hasPending: false, hasStock: true);
        Assert.True(active.CanGenerateOrRegenerate);
        Assert.True(active.CanExtract);
    }

    [Fact]
    public void SuggestedFileNames_ContainDateAndTimestamp()
    {
        var sourceDate = new DateOnly(2026, 8, 10);
        var now = new DateTime(2026, 8, 15, 14, 30, 0, DateTimeKind.Local);

        var summary = Paso4ProcessUiLogic.BuildSuggestedFileName("resumen_stock", sourceDate, now);
        var extraction = Paso4ProcessUiLogic.BuildSuggestedFileName("extraccion_stock", sourceDate, now, includeTimestamp: true);

        Assert.Contains("20260810", summary);
        Assert.EndsWith(".csv", summary, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("20260810", extraction);
        Assert.Contains("143000", extraction);
        Assert.EndsWith(".csv", extraction, StringComparison.OrdinalIgnoreCase);
    }
}
