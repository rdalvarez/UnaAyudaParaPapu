using PapaPersonas.Core.Import;

namespace PapaPersonas.Tests.Import;

public sealed class CuilValidationTests
{
    [Theory]
    [InlineData("20123456789", "20123456789")]
    [InlineData("20-12345678-9", "20123456789")]
    [InlineData(" 20 12345678 9 ", "20123456789")]
    public void Validate_PlainOrFormattedCuil_Normalizes(string raw, string expected)
    {
        var result = CuilValidator.Validate(raw);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.NormalizedCuil);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyCuil_IsRejected(string? raw)
    {
        var result = CuilValidator.Validate(raw);

        Assert.False(result.IsValid);
        Assert.Equal(ValidationErrorCode.MissingCuil, result.Issue?.Code);
    }

    [Theory]
    [InlineData("20-12345678-A")]
    [InlineData("20_12345678_9")]
    [InlineData("abc")]
    [InlineData("٢٠-١٢٣٤٥٦٧٨-٩")]
    public void Validate_InvalidCharacters_IsRejected(string raw)
    {
        var result = CuilValidator.Validate(raw);

        Assert.False(result.IsValid);
        Assert.Equal(ValidationErrorCode.InvalidCuilCharacters, result.Issue?.Code);
    }

    [Theory]
    [InlineData("2012345678")]
    [InlineData("201234567890")]
    public void Validate_WrongLength_IsRejected(string raw)
    {
        var result = CuilValidator.Validate(raw);

        Assert.False(result.IsValid);
        Assert.Equal(ValidationErrorCode.InvalidCuilLength, result.Issue?.Code);
    }
}
