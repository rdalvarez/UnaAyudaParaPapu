namespace PapaPersonas.Core.Query.Paso3;

public static class Paso3FilterCatalog
{
    public static readonly IReadOnlyList<Paso3FilterOption> Columns =
    [
        new("dni", "DNI"),
        new("apellido", "Apellido"),
        new("nombre", "Nombre"),
        new("sexo", "Sexo"),
        new("obra_social", "Obra social"),
        new("codigo_obra_social", "Código de obra social"),
        new("codigo_postal", "Código postal"),
        new("localidad", "Localidad"),
        new("partido", "Partido"),
        new("provincia", "Provincia"),
        new("nacionalidad", "Nacionalidad"),
        new("cuit_empleador", "CUIT del empleador"),
        new("edad", "Edad"),
        new("fecha_nacimiento", "Fecha de nacimiento"),
        new("fecha_importacion", "Fecha de importación"),
        new("fecha_actualizacion", "Fecha de actualización")
    ];

    public static readonly IReadOnlyList<Paso3FilterOption> Operators =
    [
        new("eq", "Igual a"),
        new("contains", "Contiene"),
        new("starts_with", "Comienza con"),
        new("gte", "Mayor o igual que"),
        new("lte", "Menor o igual que"),
        new("between", "Entre")
    ];

    public static IReadOnlyList<string> FilterColumns { get; } =
        Columns.Select(static option => option.Code).ToArray();

    public static IReadOnlyList<string> FilterOperators { get; } =
        Operators.Select(static option => option.Code).ToArray();

    public static string ColumnLabel(string code) => LabelFor(Columns, code);

    public static string OperatorLabel(string code) => LabelFor(Operators, code);

    public static string FormatActiveFilter(Paso3Filter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return $"{ColumnLabel(filter.Column)} {OperatorLabel(filter.Operator)} {filter.Value}";
    }

    private static string LabelFor(IReadOnlyList<Paso3FilterOption> options, string code)
    {
        foreach (var option in options)
        {
            if (string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return option.Label;
            }
        }

        return code;
    }
}
