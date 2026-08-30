using PapaPersonas.Core.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class Paso4ProcessErrorTextTests
{
    [Fact]
    public void PreflightMessage_InsufficientStock_IsSpanishAndNeverLeaksObservedEnglish()
    {
        var result = Paso4ExtractionPreflightResult.InsufficientStock("OS1", "PLAN A", requestedQuantity: 1, availableQuantity: 0);
        var text = Paso4ProcessUiLogic.BuildPreflightValidationMessage(result);

        Assert.Contains("disponibles", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Requested group does not have enough available members.", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Paso4GenerateStockStatus.ValidationFailed)]
    [InlineData(Paso4GenerateStockStatus.Failed)]
    public void GenerateErrorMessage_IsSpanish(Paso4GenerateStockStatus status)
    {
        var text = Paso4ProcessUiLogic.BuildGenerateStockErrorMessage(status);
        Assert.StartsWith("No se pudo", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Paso4RecoveryStatus.ValidationFailed)]
    [InlineData(Paso4RecoveryStatus.Blocked)]
    [InlineData(Paso4RecoveryStatus.Failed)]
    public void RecoveryMessages_AreSpanish(Paso4RecoveryStatus status)
    {
        Assert.StartsWith("No se pudo", Paso4ProcessUiLogic.BuildRetryPendingErrorMessage(status), StringComparison.Ordinal);
        Assert.StartsWith("No se pudo", Paso4ProcessUiLogic.BuildCancelPendingErrorMessage(status), StringComparison.Ordinal);
        Assert.StartsWith("No se pudo", Paso4ProcessUiLogic.BuildReExportErrorMessage(status), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportMessages_AreSpanish(bool summary)
    {
        var text = summary
            ? Paso4ProcessUiLogic.BuildExportSummaryErrorMessage()
            : Paso4ProcessUiLogic.BuildExportFullErrorMessage();

        Assert.StartsWith("No se pudo", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Paso4BeginExtractionStatus.ValidationFailed)]
    [InlineData(Paso4BeginExtractionStatus.Failed)]
    public void BeginExtractionMessage_IsSpanish(Paso4BeginExtractionStatus status)
    {
        var text = Paso4ProcessUiLogic.BuildBeginExtractionErrorMessage(status);
        Assert.StartsWith("No se pudo", text, StringComparison.Ordinal);
    }
}
