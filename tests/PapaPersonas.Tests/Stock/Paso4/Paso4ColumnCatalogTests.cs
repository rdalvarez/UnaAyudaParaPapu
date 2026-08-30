using PapaPersonas.Infrastructure.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class Paso4ColumnCatalogTests
{
    [Fact]
    public void AvailableColumns_ContainsAllBusinessAliases_InDeterministicOrder()
    {
        var expected = new[]
        {
            "CUIL", "DNI", "FECNANAC", "SEXO", "TIPODOC", "APELLIDO", "NOMBRE", "DIRECCION", "CP", "LOCALIDAD", "PARTIDO", "PROVINCIA", "NACIONALIDAD",
            "TELPART1", "TELPART2", "TELPART3", "TELPART4", "TELPART5",
            "CELULAR1", "CELULAR2", "CELULAR3", "CELULAR4", "CELULAR5",
            "WSP1", "WSP2", "WSP3", "WSP4", "WSP5",
            "EMAIL1", "EMAIL2", "EMAIL3", "EMAIL4", "EMAIL5",
            "CODIGOOS", "OBRASOCIAL", "CUITEMPLEADOR", "EDAD", "ANIO"
        };

        Assert.Equal(expected, Paso4ColumnCatalog.AvailableColumns);
    }

    [Fact]
    public void DefaultColumns_RemainsSample6OrderOf17Columns()
    {
        Assert.Equal(17, Paso4ColumnCatalog.DefaultColumns.Count);
        Assert.Equal(
            new[]
            {
                "CUIL", "APELLIDO", "NOMBRE", "CODIGOOS", "OBRASOCIAL", "CP", "LOCALIDAD", "PARTIDO", "PROVINCIA", "NACIONALIDAD",
                "CELULAR1", "CELULAR2", "CELULAR3", "CELULAR4", "CELULAR5", "FECNANAC", "EDAD"
            },
            Paso4ColumnCatalog.DefaultColumns);
    }

    [Fact]
    public void NormalizeSelectedColumns_IgnoresUnknown_UsesAvailableOrder_AndForcesMandatory()
    {
        var selected = Paso4ColumnCatalog.NormalizeSelectedColumns(["ANIO", "UNKNOWN", "WSP3"]);

        Assert.Equal(new[] { "CUIL", "WSP3", "CODIGOOS", "OBRASOCIAL", "ANIO" }, selected);
        Assert.DoesNotContain("UNKNOWN", selected);
    }

    [Fact]
    public void SelectExpressions_MapsAllAvailableAliases_WithUniqueExpressions()
    {
        foreach (var alias in Paso4ColumnCatalog.AvailableColumns)
        {
            Assert.True(Paso4ColumnCatalog.SelectExpressions.ContainsKey(alias), $"Missing SQL mapping for alias '{alias}'.");
        }

        var expressions = Paso4ColumnCatalog.AvailableColumns
            .Select(alias => Paso4ColumnCatalog.SelectExpressions[alias])
            .ToArray();

        Assert.Equal(expressions.Length, expressions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
