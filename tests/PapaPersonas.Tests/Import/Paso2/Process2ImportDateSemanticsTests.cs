using PapaPersonas.Core.Import.Paso2;

namespace PapaPersonas.Tests.Import.Paso2;

public sealed class Process2ImportDateSemanticsTests
{
    [Fact]
    public void TryExtractSingleBoundedFilenameDateToken_ValidSingleToken_ReturnsTrueWithParsedDate()
    {
        var found = Process2ImportDateSemantics.TryExtractSingleBoundedFilenameDateToken(
            @"C:\imports\3 - Devolucion_Sergio_10_08_2026__HERNAN_Procesado.xlsx",
            out var importDate);

        Assert.True(found);
        Assert.Equal(new DateOnly(2026, 8, 10), importDate);
    }

    [Theory]
    [InlineData(@"C:\imports\sergio_sin_fecha.xlsx")]
    [InlineData(@"C:\imports\sergio_10_08_2026_y_11_08_2026.xlsx")]
    [InlineData(@"C:\imports\sergio_99_08_2026.xlsx")]
    [InlineData(@"C:\imports\sergio_10_08_20264.xlsx")]
    [InlineData(@"C:\imports\sergio_210_08_2026.xlsx")]
    public void TryExtractSingleBoundedFilenameDateToken_MissingAmbiguousOrInvalid_ReturnsFalse(string filePath)
    {
        var found = Process2ImportDateSemantics.TryExtractSingleBoundedFilenameDateToken(filePath, out _);

        Assert.False(found);
    }

    [Fact]
    public void TryParseImportDateFromParts_ValidParts_ParsesInvariantDate()
    {
        var parsed = Process2ImportDateSemantics.TryParseImportDateFromParts("10", "08", "2026", out var importDate);

        Assert.True(parsed);
        Assert.Equal(new DateOnly(2026, 8, 10), importDate);
    }

    [Theory]
    [InlineData("", "08", "2026")]
    [InlineData("10", "", "2026")]
    [InlineData("10", "08", "")]
    [InlineData("31", "02", "2026")]
    [InlineData("10", "13", "2026")]
    [InlineData("xx", "08", "2026")]
    public void TryParseImportDateFromParts_IncompleteOrInvalid_ReturnsFalse(string day, string month, string year)
    {
        var parsed = Process2ImportDateSemantics.TryParseImportDateFromParts(day, month, year, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void IsFutureDate_DateGreaterThanToday_ReturnsTrue()
    {
        var isFuture = Process2ImportDateSemantics.IsFutureDate(new DateOnly(2026, 8, 11), new DateOnly(2026, 8, 10));

        Assert.True(isFuture);
    }

    [Fact]
    public void IsFutureDate_DateEqualToToday_ReturnsFalse()
    {
        var isFuture = Process2ImportDateSemantics.IsFutureDate(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10));

        Assert.False(isFuture);
    }

    [Fact]
    public void IsOlderThanLatestApplied_EarlierDate_ReturnsTrue()
    {
        var isOld = Process2ImportDateSemantics.IsOlderThanLatestApplied(new DateOnly(2026, 8, 9), new DateOnly(2026, 8, 10));

        Assert.True(isOld);
        Assert.NotEmpty(Process2ImportDateSemantics.OldDateWarningMessage);
    }
}
