using PapaPersonas.Core.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class Paso4ObraSocialPresentationTests
{
    [Fact]
    public void ResolvePresentationName_PrefersCatalog_ThenCandidate()
    {
        Assert.Equal("Catalogada", Paso4ObraSocialPresentation.ResolvePresentationName(" Catalogada ", "Plan A"));
        Assert.Equal("Plan A", Paso4ObraSocialPresentation.ResolvePresentationName(null, " Plan A "));
        Assert.Equal(string.Empty, Paso4ObraSocialPresentation.ResolvePresentationName("  ", "   "));
        Assert.Equal(string.Empty, Paso4ObraSocialPresentation.ResolvePresentationName(null, null));
    }

    [Fact]
    public void MergeCatalog_PersistsVisibleNames_AndRemovesBlankDisplay()
    {
        var existing = new Dictionary<string, string>
        {
            ["OS1"] = "Vieja",
            ["OS2"] = "Conservada"
        };

        var merged = Paso4ObraSocialPresentation.MergeCatalog(
            existing,
            [
                (" os1 ", " Nueva "),
                ("", "(Vacío)"),
                ("OS3", "  ")
            ]);

        Assert.Equal("Nueva", merged["OS1"]);
        Assert.Equal("Conservada", merged["OS2"]);
        Assert.False(merged.ContainsKey(""));
        Assert.False(merged.ContainsKey("OS3"));
    }
}
