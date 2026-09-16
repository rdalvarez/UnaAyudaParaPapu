using PapaPersonas.Core.Query.Paso3;

namespace PapaPersonas.Tests.Query.Paso3;

public sealed class Paso3FilterCatalogTests
{
    [Fact]
    public void FilterColumns_ExposeStoredSimpleFieldsInUserOrder()
    {
        Assert.Equal(
            [
                "dni",
                "apellido",
                "nombre",
                "sexo",
                "obra_social",
                "codigo_obra_social",
                "codigo_postal",
                "localidad",
                "partido",
                "provincia",
                "nacionalidad",
                "cuit_empleador",
                "edad",
                "fecha_nacimiento",
                "fecha_importacion",
                "fecha_actualizacion"
            ],
            Paso3FilterCatalog.FilterColumns);
    }

    [Fact]
    public void Columns_MapEveryVisibleFilterToUniqueSpanishLabel()
    {
        (string Code, string Label)[] expected =
        [
            ("dni", "DNI"),
            ("apellido", "Apellido"),
            ("nombre", "Nombre"),
            ("sexo", "Sexo"),
            ("obra_social", "Obra social"),
            ("codigo_obra_social", "Código de obra social"),
            ("codigo_postal", "Código postal"),
            ("localidad", "Localidad"),
            ("partido", "Partido"),
            ("provincia", "Provincia"),
            ("nacionalidad", "Nacionalidad"),
            ("cuit_empleador", "CUIT del empleador"),
            ("edad", "Edad"),
            ("fecha_nacimiento", "Fecha de nacimiento"),
            ("fecha_importacion", "Fecha de importación"),
            ("fecha_actualizacion", "Fecha de actualización")
        ];

        Assert.Equal(expected, Paso3FilterCatalog.Columns.Select(static option => (option.Code, option.Label)));
        Assert.Equal(expected.Length, Paso3FilterCatalog.Columns.Select(static option => option.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(expected.Length, Paso3FilterCatalog.Columns.Select(static option => option.Label).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Paso3FilterCatalog.FilterColumns, Paso3FilterCatalog.Columns.Select(static option => option.Code));
    }

    [Fact]
    public void Operators_MapEveryCodeToUniqueSpanishLabel()
    {
        (string Code, string Label)[] expected =
        [
            ("eq", "Igual a"),
            ("contains", "Contiene"),
            ("starts_with", "Comienza con"),
            ("gte", "Mayor o igual que"),
            ("lte", "Menor o igual que"),
            ("between", "Entre")
        ];

        Assert.Equal(expected, Paso3FilterCatalog.Operators.Select(static option => (option.Code, option.Label)));
        Assert.Equal(expected.Length, Paso3FilterCatalog.Operators.Select(static option => option.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(expected.Length, Paso3FilterCatalog.Operators.Select(static option => option.Label).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ["eq", "contains", "starts_with", "gte", "lte", "between"],
            Paso3FilterCatalog.FilterOperators);
    }

    [Fact]
    public void FormatActiveFilter_UsesSpanishLabelsAndPreservesValueVerbatim()
    {
        var filter = new Paso3Filter("fecha_nacimiento", "between", "1980-01-01,1990-12-31");

        Assert.Equal("Fecha de nacimiento Entre 1980-01-01,1990-12-31", Paso3FilterCatalog.FormatActiveFilter(filter));
        Assert.Equal("fecha_nacimiento", filter.Column);
        Assert.Equal("between", filter.Operator);
        Assert.Equal("1980-01-01,1990-12-31", filter.Value);
        Assert.Equal("CUIT del empleador Contiene 30-123", Paso3FilterCatalog.FormatActiveFilter(new Paso3Filter("cuit_empleador", "contains", "30-123")));
        Assert.Equal("Edad Mayor o igual que 40", Paso3FilterCatalog.FormatActiveFilter(new Paso3Filter("edad", "gte", "40")));
    }
}
