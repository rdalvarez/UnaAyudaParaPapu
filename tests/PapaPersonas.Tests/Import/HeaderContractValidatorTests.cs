using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso2;

namespace PapaPersonas.Tests.Import;

public sealed class HeaderContractValidatorTests
{
    [Fact]
    public void Validate_HernanRaw_RequiredPreparationHeadersOnly_IsAccepted()
    {
        var headers = new[]
        {
            "CUIL",
            "CUIL_APENOM",
            "CUIL_CODOS",
            "CUIL_DESCRIPOS",
            "CUIL_FECHANAC",
            "CUIL_EDAD"
        };

        var result = HeaderContractValidator.Validate(ImportStage.HernanRaw, headers);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void Validate_HernanRaw_UnknownExtraHeader_DoesNotInvalidate()
    {
        var headers = new[]
        {
            "CUIL",
            "CUIL_APENOM",
            "CUIL_CODOS",
            "CUIL_DESCRIPOS",
            "CUIL_FECHANAC",
            "CUIL_EDAD",
            "EXTRA_COLUMN_FROM_HERNAN"
        };

        var result = HeaderContractValidator.Validate(ImportStage.HernanRaw, headers);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Contains(result.Notices, n => n.Code == ValidationErrorCode.UnknownHeader);
    }

    [Fact]
    public void Validate_HernanRaw_MissingRequiredPreparationHeader_IsRejected()
    {
        var headers = new[]
        {
            "CUIL",
            "CUIL_APENOM",
            "CUIL_CODOS",
            "CUIL_DESCRIPOS",
            "CUIL_FECHANAC"
        };

        var result = HeaderContractValidator.Validate(ImportStage.HernanRaw, headers);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i =>
            i.Code == ValidationErrorCode.MissingRequiredHeader
            && i.Header == "CUIL_EDAD");
    }

    [Fact]
    public void Validate_SergioReturn_Template36HeadersAccepted()
    {
        var result = HeaderContractValidator.Validate(ImportStage.SergioReturn, HeaderContracts.SergioTemplateHeaders36);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Equal("anio", result.SourceToCanonical["ANIO"]);
    }

    [Fact]
    public void Validate_SergioReturn_Actual28AliasesAccepted()
    {
        var result = HeaderContractValidator.Validate(ImportStage.SergioReturn, HeaderContracts.SergioActualHeaders28);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Equal("fecha_nacimiento", result.SourceToCanonical["FECNANAC"]);
        Assert.Equal("telefono_fijo_1", result.SourceToCanonical["TELPART1"]);
        Assert.Equal("codigo_obra_social", result.SourceToCanonical["CODIGOOS"]);
        Assert.Equal("nacionalidad", result.SourceToCanonical["NACIONALIDAD"]);
    }

    [Fact]
    public void Validate_KnownOptionalMissing_IsAccepted()
    {
        var headers = HeaderContracts.SergioTemplateHeaders36
            .Where(h => !h.Equals("ANIO", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var result = HeaderContractValidator.Validate(ImportStage.SergioReturn, headers);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.DoesNotContain("anio", result.PresentCanonicalFields);
    }

    [Fact]
    public void Validate_SergioReturn_UnknownColumn_IsReportedInSourceOrder()
    {
        var headers = HeaderContracts.SergioTemplateHeaders36.Concat(["UNEXPECTED_NEW_COLUMN"]).ToArray();

        var result = HeaderContractValidator.Validate(ImportStage.SergioReturn, headers);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == ValidationErrorCode.UnknownHeader);
        Assert.Equal(["UNEXPECTED_NEW_COLUMN"], result.UnknownSourceHeaders);
    }

    [Fact]
    public void Validate_SergioReturn_UnknownColumnsCanBeAllowedWithoutMappingThem()
    {
        var headers = HeaderContracts.SergioActualHeaders28.Concat([" ZETA ", "ALFA"]).ToArray();

        var result = HeaderContractValidator.Validate(ImportStage.SergioReturn, headers, allowUnknownHeaders: true);

        Assert.True(result.IsValid);
        Assert.Equal([" ZETA ", "ALFA"], result.UnknownSourceHeaders);
        Assert.DoesNotContain("zeta", result.PresentCanonicalFields, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("alfa", result.PresentCanonicalFields, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MissingCuilHeader_IsRejectedForBothStages()
    {
        var sergioHeaders = HeaderContracts.SergioTemplateHeaders36
            .Where(h => !h.Equals("CUIL", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var hernanHeaders = new[]
        {
            "CUIL_APENOM",
            "CUIL_CODOS",
            "CUIL_DESCRIPOS",
            "CUIL_FECHANAC",
            "CUIL_EDAD"
        };

        var sergioResult = HeaderContractValidator.Validate(ImportStage.SergioReturn, sergioHeaders);
        var hernanResult = HeaderContractValidator.Validate(ImportStage.HernanRaw, hernanHeaders);

        Assert.False(sergioResult.IsValid);
        Assert.False(hernanResult.IsValid);
        Assert.Contains(sergioResult.Issues, i => i.Code == ValidationErrorCode.MissingRequiredHeader && i.Header == "CUIL");
        Assert.Contains(hernanResult.Issues, i => i.Code == ValidationErrorCode.MissingRequiredHeader && i.Header == "CUIL");
    }

    [Fact]
    public void Validate_DuplicateSourceHeader_IsRejected()
    {
        var headers = HeaderContracts.SergioTemplateHeaders36.Concat(["CUIL"]).ToArray();

        var result = HeaderContractValidator.Validate(ImportStage.SergioReturn, headers);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == ValidationErrorCode.DuplicateSourceHeader);
    }

    [Fact]
    public void Validate_CanonicalAliasCollision_IsRejected()
    {
        var headers = HeaderContracts.SergioTemplateHeaders36.Concat(["FECNANAC"]).ToArray();

        var result = HeaderContractValidator.Validate(ImportStage.SergioReturn, headers);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == ValidationErrorCode.CanonicalHeaderCollision);
    }

    [Fact]
    public void Validate_Normalization_TrimAndCaseInsensitive_IsAccepted()
    {
        var headers = new[] { "  cuil  ", " nombre ", " fecha de nac " };

        var result = HeaderContractValidator.Validate(ImportStage.SergioReturn, headers);

        Assert.True(result.IsValid);
        Assert.Equal("cuil", result.SourceToCanonical["  cuil  "]);
        Assert.Equal("nombre", result.SourceToCanonical[" nombre "]);
    }

    [Fact]
    public void UnknownHeadersConfirmationMessage_ListsHeadersInSourceOrder()
    {
        var message = SergioUnknownHeadersConfirmation.BuildMessage(["ZETA", "ALFA"]);

        Assert.Contains("Continuar", message, StringComparison.Ordinal);
        Assert.True(message.IndexOf("ZETA", StringComparison.Ordinal) < message.IndexOf("ALFA", StringComparison.Ordinal));
        Assert.Contains("no se guardarán", message, StringComparison.OrdinalIgnoreCase);
    }
}
