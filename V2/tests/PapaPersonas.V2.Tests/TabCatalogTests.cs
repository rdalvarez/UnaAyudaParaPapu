using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class TabCatalogTests
{
    [Fact]
    public void DefinesTheAuthorizedTabsInOrderWithOnlyPasoOneEnabled()
    {
        Assert.Equal(
            [
                "Preparar archivo de Hernán",
                "Ingresar archivo de Sergio",
                "Generación y consulta de stock",
                "Consultas (opcional)"
            ],
            TabCatalog.All.Select(tab => tab.Title));
        Assert.Equal([true, false, false, false], TabCatalog.All.Select(tab => tab.IsEnabled));
    }
}
