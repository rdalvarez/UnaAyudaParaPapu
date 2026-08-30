using PapaPersonas.Core.App;
using PapaPersonas.Core.Import.Paso2;
using PapaPersonas.Core.Query.Paso3;

namespace PapaPersonas.Tests.App;

public sealed class StatusDisplayNamesTests
{
    [Fact]
    public void SharedStatusNames_AreSpanishAndDoNotExposeEnumNames()
    {
        Assert.Equal("Completada", StatusDisplayNames.For(SergioApplyStatus.Completed));
        Assert.Equal("Validación fallida", StatusDisplayNames.For(SergioStagePreviewStatus.ValidationFailed));
        Assert.Equal("Cancelada", StatusDisplayNames.For(Paso3ExportStatus.Canceled));
    }
}
