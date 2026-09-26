using System.Collections.ObjectModel;

namespace PapaPersonas.Infrastructure.Stock.Paso4;

public static class Paso4ColumnCatalog
{
    public static readonly IReadOnlyList<string> MandatoryColumns = ["CUIL", "CODIGOOS", "OBRASOCIAL"];

    public static readonly IReadOnlyList<string> AvailableColumns =
    [
        "CUIL",
        "DNI",
        "FECNANAC",
        "SEXO",
        "TIPODOC",
        "APELLIDO",
        "NOMBRE",
        "DIRECCION",
        "CP",
        "LOCALIDAD",
        "PARTIDO",
        "PROVINCIA",
        "NACIONALIDAD",
        "TELPART1",
        "TELPART2",
        "TELPART3",
        "TELPART4",
        "TELPART5",
        "CELULAR1",
        "CELULAR2",
        "CELULAR3",
        "CELULAR4",
        "CELULAR5",
        "WSP1",
        "WSP2",
        "WSP3",
        "WSP4",
        "WSP5",
        "EMAIL1",
        "EMAIL2",
        "EMAIL3",
        "EMAIL4",
        "EMAIL5",
        "CODIGOOS",
        "OBRASOCIAL",
        "CUITEMPLEADOR",
        "EDAD",
        "ANIO"
    ];

    public static readonly IReadOnlyList<string> DefaultColumns =
    [
        "CUIL",
        "APELLIDO",
        "NOMBRE",
        "CODIGOOS",
        "OBRASOCIAL",
        "CP",
        "LOCALIDAD",
        "PARTIDO",
        "PROVINCIA",
        "NACIONALIDAD",
        "CELULAR1",
        "CELULAR2",
        "CELULAR3",
        "CELULAR4",
        "CELULAR5",
        "FECNANAC",
        "EDAD"
    ];

    public static readonly IReadOnlyDictionary<string, string> SelectExpressions = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CUIL"] = "sm.cuil",
            ["DNI"] = "p.dni",
            ["FECNANAC"] = "p.fecha_nacimiento",
            ["SEXO"] = "p.sexo",
            ["TIPODOC"] = "p.tipo_dni",
            ["DIRECCION"] = "p.direccion",
            ["TELPART1"] = "p.telefono_fijo_1",
            ["TELPART2"] = "p.telefono_fijo_2",
            ["TELPART3"] = "p.telefono_fijo_3",
            ["TELPART4"] = "p.telefono_fijo_4",
            ["TELPART5"] = "p.telefono_fijo_5",
            ["WSP1"] = "p.whatsapp_1",
            ["WSP2"] = "p.whatsapp_2",
            ["WSP3"] = "p.whatsapp_3",
            ["WSP4"] = "p.whatsapp_4",
            ["WSP5"] = "p.whatsapp_5",
            ["EMAIL1"] = "p.email_1",
            ["EMAIL2"] = "p.email_2",
            ["EMAIL3"] = "p.email_3",
            ["EMAIL4"] = "p.email_4",
            ["EMAIL5"] = "p.email_5",
            ["CODIGOOS"] = "sm.codigo_obra_social",
            ["OBRASOCIAL"] = "sm.obra_social",
            ["APELLIDO"] = "p.apellido",
            ["NOMBRE"] = "p.nombre",
            ["CP"] = "p.codigo_postal",
            ["LOCALIDAD"] = "p.localidad",
            ["PARTIDO"] = "p.partido",
            ["PROVINCIA"] = "p.provincia",
            ["NACIONALIDAD"] = "p.nacionalidad",
            ["CELULAR1"] = "p.celular_1",
            ["CELULAR2"] = "p.celular_2",
            ["CELULAR3"] = "p.celular_3",
            ["CELULAR4"] = "p.celular_4",
            ["CELULAR5"] = "p.celular_5",
            ["EDAD"] = "p.edad",
            ["CUITEMPLEADOR"] = "p.cuit_empleador",
            ["ANIO"] = "p.anio",
            ["VENDIDO"] = "CASE WHEN sm.vendido THEN 1 ELSE 0 END"
        });

    /// <summary>Normaliza la selección contra la lista permitida y agrega siempre las columnas obligatorias.</summary>
    public static IReadOnlyList<string> NormalizeSelectedColumns(IReadOnlyList<string>? selectedColumns)
    {
        if (selectedColumns is null || selectedColumns.Count == 0)
        {
            return DefaultColumns.ToArray();
        }

        var requested = selectedColumns;

        var whitelist = new HashSet<string>(AvailableColumns, StringComparer.OrdinalIgnoreCase);

        var cleaned = requested
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(x => whitelist.Contains(x))
            .Where(x => !x.Equals("VENDIDO", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var mandatory in MandatoryColumns)
        {
            if (!cleaned.Contains(mandatory, StringComparer.OrdinalIgnoreCase))
            {
                cleaned.Add(mandatory);
            }
        }

        var order = AvailableColumns
            .Select((column, index) => new { column, index })
            .ToDictionary(x => x.column, x => x.index, StringComparer.OrdinalIgnoreCase);

        return cleaned
            .OrderBy(x => order[x])
            .ToList();
    }
}
