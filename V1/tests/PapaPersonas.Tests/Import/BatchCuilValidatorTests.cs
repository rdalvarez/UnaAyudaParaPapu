using PapaPersonas.Core.Import;

namespace PapaPersonas.Tests.Import;

public sealed class BatchCuilValidatorTests
{
    [Fact]
    public void Validate_AllRowsWithDuplicateCuil_AreRejectedAsDuplicates()
    {
        var rows = new[]
        {
            new BatchCuilRowInput(2, "20-12345678-9"),
            new BatchCuilRowInput(3, "20123456789"),
            new BatchCuilRowInput(4, " 20 12345678 9 ")
        };

        var result = BatchCuilValidator.Validate(rows);

        Assert.All(result.Rows, r =>
        {
            Assert.False(r.IsValid);
            Assert.True(r.IsDuplicate);
            Assert.Equal(ValidationErrorCode.DuplicateCuilInBatch, r.Issue?.Code);
        });

        Assert.Equal(3, result.Summary.DuplicateCuilRows);
        Assert.Equal(3, result.Summary.RejectedRows);
    }

    [Fact]
    public void Validate_InvalidRows_AreNotGroupedAsDuplicates()
    {
        var rows = new[]
        {
            new BatchCuilRowInput(10, null),
            new BatchCuilRowInput(11, "20-12345678-A"),
            new BatchCuilRowInput(12, "20123456789"),
            new BatchCuilRowInput(13, "20999999999")
        };

        var result = BatchCuilValidator.Validate(rows);

        var row10 = result.Rows.Single(r => r.SourceRowNumber == 10);
        var row11 = result.Rows.Single(r => r.SourceRowNumber == 11);
        var row12 = result.Rows.Single(r => r.SourceRowNumber == 12);
        var row13 = result.Rows.Single(r => r.SourceRowNumber == 13);

        Assert.Equal(ValidationErrorCode.MissingCuil, row10.Issue?.Code);
        Assert.Equal(ValidationErrorCode.InvalidCuilCharacters, row11.Issue?.Code);
        Assert.True(row12.IsValid);
        Assert.True(row13.IsValid);
        Assert.False(row12.IsDuplicate);
        Assert.False(row13.IsDuplicate);
        Assert.Equal(0, result.Summary.DuplicateCuilRows);
    }

    [Fact]
    public void Validate_AggregateCounts_AreDeterministic()
    {
        var rows = new[]
        {
            new BatchCuilRowInput(1, "20123456789"),
            new BatchCuilRowInput(2, "20-12345678-9"),
            new BatchCuilRowInput(3, null),
            new BatchCuilRowInput(4, "20-12345678-A"),
            new BatchCuilRowInput(5, "20999999999"),
            new BatchCuilRowInput(6, "2012345678")
        };

        var result = BatchCuilValidator.Validate(rows);

        Assert.Equal(6, result.Summary.TotalRows);
        Assert.Equal(1, result.Summary.ValidRows);
        Assert.Equal(5, result.Summary.RejectedRows);
        Assert.Equal(1, result.Summary.MissingCuilRows);
        Assert.Equal(2, result.Summary.MalformedCuilRows);
        Assert.Equal(2, result.Summary.DuplicateCuilRows);
    }
}
