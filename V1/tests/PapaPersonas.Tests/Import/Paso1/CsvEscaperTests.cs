using PapaPersonas.Infrastructure.Import.Paso1;

namespace PapaPersonas.Tests.Import.Paso1;

public sealed class CsvEscaperTests
{
    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("with,comma", "\"with,comma\"")]
    [InlineData("with\"quote", "\"with\"\"quote\"")]
    [InlineData("line\r\nbreak", "\"line\r\nbreak\"")]
    public void Escape_QuotesCorrectly(string input, string expected)
    {
        var escaped = CsvEscaper.Escape(input);
        Assert.Equal(expected, escaped);
    }

    [Fact]
    public void FormatCellValue_FormatsDateAndNumericInvariant()
    {
        var dt = new DateTime(2025, 01, 31, 13, 14, 15, DateTimeKind.Utc);
        var formattedDate = CsvEscaper.FormatCellValue(dt);
        var formattedNumber = CsvEscaper.FormatCellValue(1234.5m);

        Assert.Equal("2025-01-31", formattedDate);
        Assert.Equal("1234.5", formattedNumber);
    }
}
