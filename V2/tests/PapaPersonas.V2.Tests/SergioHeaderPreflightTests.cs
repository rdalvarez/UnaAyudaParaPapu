using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class SergioHeaderPreflightTests
{
    [Fact]
    public void CleanHeadersRemainUnchangedAndHaveNoIssues()
    {
        var result = SergioHeaderPreflight.Analyze(["CUIL", "Email", "email"]);

        Assert.False(result.HasIssues);
        Assert.Equal(["CUIL", "Email", "email"], result.Columns.Select(column => column.ProposedAlias));
    }

    [Fact]
    public void ExactDuplicatesReceiveOrdinalAliases()
    {
        var result = SergioHeaderPreflight.Analyze(["Nombre", "Nombre", "Nombre"]);

        Assert.Equal(["Nombre", "Nombre (1)", "Nombre (2)"], result.Columns.Select(column => column.ProposedAlias));
        Assert.Single(result.DuplicateGroups);
        Assert.Equal(2, result.DuplicateOccurrenceCount);
    }

    [Fact]
    public void EmptyHeadersReceiveSpanishAliases()
    {
        var result = SergioHeaderPreflight.Analyze([null, " ", "Email"]);

        Assert.Equal(["Columna Vacia", "Columna Vacia (1)", "Email"], result.Columns.Select(column => column.ProposedAlias));
        Assert.Equal(2, result.EmptyHeaderCount);
    }

    [Fact]
    public void CaseSensitiveNamesAreDistinct()
    {
        var result = SergioHeaderPreflight.Analyze(["Email", "email"]);

        Assert.False(result.HasIssues);
        Assert.Equal(["Email", "email"], result.Columns.Select(column => column.ProposedAlias));
    }

    [Fact]
    public void RawSuffixLikeNamesCannotCollideWithGeneratedAliases()
    {
        var result = SergioHeaderPreflight.Analyze(["Nombre", "Nombre (1)", "Nombre", "Nombre (1)"]);

        Assert.Equal(["Nombre", "Nombre (1)", "Nombre (1) (1)", "Nombre (1) (1) (1)"], result.Columns.Select(column => column.ProposedAlias));
        Assert.Equal(result.Columns.Count, result.Columns.Select(column => column.ProposedAlias).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PreservesPositionsAndRawInput()
    {
        var input = new List<string?> { " Name ", null };
        var result = SergioHeaderPreflight.Analyze(input);

        Assert.Equal([0, 1], result.Columns.Select(column => column.Position));
        Assert.Equal([" Name ", null], input);
        Assert.Equal(" Name ", result.Columns[0].RawName);
    }

    [Fact]
    public void DefaultsOnlyToExactCuilAlias()
    {
        var result = SergioHeaderPreflight.Analyze([" CUIL ", "cuil"]);

        Assert.Equal("CUIL", SergioPrimaryKeySelection.DefaultAlias(result.Columns));
        Assert.Null(SergioPrimaryKeySelection.DefaultAlias(SergioHeaderPreflight.Analyze(["CUIL (1)"]).Columns));
    }
}
