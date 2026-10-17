using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class StockPresentationTests
{
    [Fact]
    public void MissingSourceDisablesGenerationWithoutDisablingExistingStock()
    {
        var state = StockPresentation.Evaluate(false, false, true, false, true);

        Assert.False(state.CanGenerate);
        Assert.True(state.CanExport);
        Assert.True(state.CanExtract);
        Assert.True(state.CanReexport);
    }

    [Fact]
    public void PendingBlocksGenerationAndExtractionButKeepsRecovery()
    {
        var state = StockPresentation.Evaluate(false, true, true, true, false);

        Assert.False(state.CanGenerate);
        Assert.False(state.CanExtract);
        Assert.True(state.CanRetryPending);
        Assert.True(state.CanCancelPending);
    }

    [Fact]
    public void BusyBlocksEveryMutatingAction()
    {
        var state = StockPresentation.Evaluate(true, true, true, true, true);

        Assert.False(state.CanGenerate);
        Assert.False(state.CanExport);
        Assert.False(state.CanExtract);
        Assert.False(state.CanRetryPending);
        Assert.False(state.CanCancelPending);
        Assert.False(state.CanReexport);
        Assert.False(state.CanSaveNames);
    }

    [Fact]
    public void GenerationRequiresBothAliasesAndDoesNotRememberThem()
    {
        Assert.NotNull(StockPresentation.ValidateGeneration(null, "NOMBRE"));
        Assert.NotNull(StockPresentation.ValidateGeneration("CODIGO", " "));
        Assert.Null(StockPresentation.ValidateGeneration("CODIGO", "NOMBRE"));
    }

    [Fact]
    public void QuantityParserIgnoresBlankAndZeroAndRejectsInvalidText()
    {
        var parsed = StockPresentation.ParseQuantities([("OSDE", "2"), ("", "0"), ("PAMI", "  ")]);

        Assert.Equal([new StockGroupQuantity("OSDE", 2)], parsed);
        Assert.Throws<InvalidOperationException>(() => StockPresentation.ParseQuantities([("OSDE", "dos")]));
    }

    [Fact]
    public void ResultMessageNeverTreatsNullOrFailureAsSuccess()
    {
        Assert.Equal("Listo.", StockPresentation.ResultMessage(StockFileResult.Completed(1, "out.csv"), "Listo.", "Error."));
        Assert.Equal("No se pudo.", StockPresentation.ResultMessage(null, "Listo.", "No se pudo."));
        Assert.Equal("Validación.", StockPresentation.ResultMessage(StockFileResult.ValidationFailed("Validación."), "Listo.", "Error."));
    }

    [Fact]
    public void ExportColumnsRebuildOnSchemaChangeAndKeepMandatorySelection()
    {
        var first = StockPresentation.BuildExportColumns(["CUIL", "CODIGO", "EXTRA"], ["CUIL", "CODIGO"], null);
        var same = StockPresentation.BuildExportColumns(["CUIL", "CODIGO", "EXTRA"], ["CUIL", "CODIGO"], [("CUIL", true), ("CODIGO", true), ("EXTRA", false)]);
        var changed = StockPresentation.BuildExportColumns(["CUIL", "CODIGO", "OTRA"], ["CUIL", "CODIGO"], [("CUIL", true), ("CODIGO", true), ("EXTRA", false)]);

        Assert.False(same.Single(column => column.Name == "EXTRA").IsSelected);
        Assert.True(changed.Single(column => column.Name == "OTRA").IsSelected);
        Assert.True(first.Single(column => column.Name == "CUIL").IsMandatory);
    }

    [Fact]
    public void ConfirmationsDoNotClaimDestructionForTheFirstStock()
    {
        Assert.Contains("reemplazará", StockPresentation.DestructiveConfirmation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("catálogo", StockPresentation.DestructiveConfirmation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no reemplaza ventas", StockPresentation.CreateConfirmation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateOriginMissingBothReportsNoStockAndOfficialAbsent()
    {
        var model = StockPresentation.EvaluateOrigin(null, null);

        Assert.Equal(StockPresentation.StatusNoStock, model.StatusText);
        Assert.Equal(StockPresentation.StockAbsentText, model.StockSourceText);
        Assert.Equal(StockPresentation.OfficialAbsentText, model.OfficialSourceText);
        Assert.Null(model.GeneratedText);
        Assert.Null(model.ExplanatoryText);
        Assert.False(model.HasStock);
        Assert.False(model.HasExplanatoryText);
    }

    [Fact]
    public void EvaluateOriginNoStockOfficialPresentReportsNoStockAndExposesOfficialFile()
    {
        var loadedAt = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var official = new StockSourceIdentity("sergio.xlsx", loadedAt, 100, "CUIL");

        var model = StockPresentation.EvaluateOrigin(null, official);

        Assert.Equal(StockPresentation.StatusNoStock, model.StatusText);
        Assert.Equal(StockPresentation.StockAbsentText, model.StockSourceText);
        Assert.Contains("sergio.xlsx", model.OfficialSourceText);
        Assert.Contains("Carga:", model.OfficialSourceText);
        Assert.Null(model.GeneratedText);
        Assert.False(model.HasStock);
    }

    [Fact]
    public void EvaluateOriginMatchingReportsCurrentLoad()
    {
        var loadedAt = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var generatedAt = new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc);
        var header = new StockHeader("sergio.xlsx", loadedAt, 100, "CUIL", "COD", "NOM", generatedAt);
        var official = new StockSourceIdentity("sergio.xlsx", loadedAt, 100, "CUIL");

        var model = StockPresentation.EvaluateOrigin(header, official);

        Assert.Equal(StockPresentation.StatusCurrentLoad, model.StatusText);
        Assert.Contains("sergio.xlsx", model.StockSourceText);
        Assert.Contains("sergio.xlsx", model.OfficialSourceText);
        Assert.NotNull(model.GeneratedText);
        Assert.Contains("Generación del stock:", model.GeneratedText);
        Assert.Null(model.ExplanatoryText);
        Assert.True(model.HasStock);
        Assert.False(model.HasExplanatoryText);
    }

    [Fact]
    public void EvaluateOriginSameFileNameDifferentTimestampReportsAnteriorLoadWithAmberNote()
    {
        var loadedAt1 = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var loadedAt2 = new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc);
        var header = new StockHeader("sergio.xlsx", loadedAt1, 100, "CUIL", "COD", "NOM", DateTime.UtcNow);
        var official = new StockSourceIdentity("sergio.xlsx", loadedAt2, 100, "CUIL");

        var model = StockPresentation.EvaluateOrigin(header, official);

        Assert.Equal(StockPresentation.StatusPreviousLoad, model.StatusText);
        Assert.Contains("sergio.xlsx", model.StockSourceText);
        Assert.Contains("sergio.xlsx", model.OfficialSourceText);
        Assert.Equal(StockPresentation.AnteriorLoadExplanatoryText, model.ExplanatoryText);
        Assert.True(model.HasExplanatoryText);
        Assert.DoesNotContain("error", model.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desactualizado", model.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateOriginDifferentFileNameReportsAnteriorLoad()
    {
        var loadedAt = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var header = new StockHeader("sergio_viejo.xlsx", loadedAt, 100, "CUIL", "COD", "NOM", DateTime.UtcNow);
        var official = new StockSourceIdentity("sergio_nuevo.xlsx", loadedAt, 100, "CUIL");

        var model = StockPresentation.EvaluateOrigin(header, official);

        Assert.Equal(StockPresentation.StatusPreviousLoad, model.StatusText);
        Assert.Contains("sergio_viejo.xlsx", model.StockSourceText);
        Assert.Contains("sergio_nuevo.xlsx", model.OfficialSourceText);
        Assert.True(model.HasExplanatoryText);
    }

    [Fact]
    public void EvaluateOriginRowCountOrKeyDifferenceReportsAnteriorLoad()
    {
        var loadedAt = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var header = new StockHeader("sergio.xlsx", loadedAt, 100, "CUIL", "COD", "NOM", DateTime.UtcNow);
        var official = new StockSourceIdentity("sergio.xlsx", loadedAt, 150, "CUIL");

        var model = StockPresentation.EvaluateOrigin(header, official);

        Assert.Equal(StockPresentation.StatusPreviousLoad, model.StatusText);
        Assert.True(model.HasExplanatoryText);
    }

    [Fact]
    public void EvaluateOriginOfficialAbsentStockRemainsUsable()
    {
        var loadedAt = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var header = new StockHeader("sergio.xlsx", loadedAt, 100, "CUIL", "COD", "NOM", DateTime.UtcNow);

        var model = StockPresentation.EvaluateOrigin(header, null);

        Assert.Equal(StockPresentation.StatusOfficialAbsent, model.StatusText);
        Assert.Contains("sergio.xlsx", model.StockSourceText);
        Assert.Equal(StockPresentation.OfficialAbsentText, model.OfficialSourceText);
        Assert.True(model.HasStock);
        Assert.NotNull(model.ExplanatoryText);
        Assert.Contains("sigue operativo", model.ExplanatoryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateOriginOfficialQueryFailedReportsUnavailableWithoutClaimingAbsent()
    {
        var loadedAt = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var header = new StockHeader("sergio.xlsx", loadedAt, 100, "CUIL", "COD", "NOM", DateTime.UtcNow);

        var model = StockPresentation.EvaluateOrigin(header, null, officialUnavailable: true);

        Assert.Equal(StockPresentation.StatusOfficialUnavailable, model.StatusText);
        Assert.Equal(StockPresentation.OfficialUnavailableText, model.OfficialSourceText);
        Assert.Contains("sergio.xlsx", model.StockSourceText);
        Assert.True(model.HasStock);
        Assert.Equal("No se pudo consultar la carga oficial de Sergio. El stock existente sigue operativo.", model.ExplanatoryText);
        Assert.True(model.HasExplanatoryText);
    }

    [Fact]
    public void SafeFileNameStripsDirectoryPaths()
    {
        Assert.Equal("sergio.xlsx", StockPresentation.SafeFileName(@"C:\Users\Sergio\Documents\sergio.xlsx"));
        Assert.Equal("sergio.xlsx", StockPresentation.SafeFileName("/var/data/sergio.xlsx"));
        Assert.Equal("sergio.xlsx", StockPresentation.SafeFileName("sergio.xlsx"));
        Assert.Equal(string.Empty, StockPresentation.SafeFileName(null));
        Assert.Equal(string.Empty, StockPresentation.SafeFileName("   "));
    }

    [Fact]
    public void EvaluateOperationalStateLeavesTransientStatusTextNull()
    {
        var normal = StockPresentation.Evaluate(false, true, true, false, false);
        Assert.Null(normal.StatusText);

        var busy = StockPresentation.Evaluate(true, true, true, false, false);
        Assert.Equal("Hay una operación en curso.", busy.StatusText);

        var pending = StockPresentation.Evaluate(false, true, true, true, false);
        Assert.Equal("Hay una extracción pendiente. Resuélvala antes de generar o extraer.", pending.StatusText);
    }
}
