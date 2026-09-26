namespace PapaPersonas.Core.Import;

public static class HeaderContracts
{
    public static IReadOnlySet<string> HernanPreparationRequiredHeaders { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CUIL",
            "CUIL_APENOM",
            "CUIL_CODOS",
            "CUIL_DESCRIPOS",
            "CUIL_FECHANAC",
            "CUIL_EDAD"
        };

    private static readonly Lazy<IReadOnlyDictionary<string, string>> HernanRawMap =
        new(BuildHernanRawMap);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> SergioReturnMap =
        new(BuildSergioReturnMap);

    public static IReadOnlyList<string> HernanRawHeaders33 { get; } =
    [
        "CUIL",
        "CUIL_APENOM",
        "CUIL_DNI",
        "CUIL_FECHANAC",
        "CUIL_EDAD",
        "CUIL_SEXO",
        "CUIL_TIPODOC",
        "CUIL_DIRECCION",
        "CUIL_CP",
        "CUIL_LOCALIDAD",
        "CUIL_PROVINCIA",
        "CUIL_NACIONALIDAD",
        "CUIL_TELFIJO1",
        "CUIL_TELFIJO2",
        "CUIL_TELFIJO3",
        "CUIL_TELFIJO4",
        "CUIL_TELFIJO5",
        "CUIL_CEL1",
        "CUIL_CEL2",
        "CUIL_CEL3",
        "CUIL_CEL4",
        "CUIL_CEL5",
        "CUIL_WSP1",
        "CUIL_WSP2",
        "CUIL_WSP3",
        "CUIL_WSP4",
        "CUIL_WSP5",
        "CUIL_EMAIL1",
        "CUIL_EMAIL2",
        "CUIL_EMAIL3",
        "CUIL_CODOS",
        "CUIL_DESCRIPOS",
        "CUIL_CUITEMPL"
    ];

    public static IReadOnlyList<string> SergioTemplateHeaders36 { get; } =
    [
        "CUIL",
        "DNI",
        "TIPO DNI",
        "APELLIDO",
        "NOMBRE",
        "SEXO",
        "FECHA DE NAC",
        "EDAD",
        "DIRECCION",
        "CP",
        "LOCALIDAD",
        "PARTIDO",
        "PROVINCIA",
        "NACIONALIDAD",
        "TEL FIJO 1",
        "TEL FIJO 2",
        "TEL FIJO 3",
        "TEL FIJO 4",
        "TEL FIJO 5",
        "CEL1",
        "CEL2",
        "CEL3",
        "CEL4",
        "CEL5",
        "WSP1",
        "WSP2",
        "WSP3",
        "WSP4",
        "WSP5",
        "EMAIL1",
        "EMAIL2",
        "EMAIL3",
        "CODIGO",
        "OBRA SOCIAL",
        "CUIT",
        "ANIO"
    ];

    // Real workbook 3 sample currently used by operations.
    // Keep this list synchronized with observed source evidence.
    public static IReadOnlyList<string> SergioActualHeaders28 { get; } =
    [
        "CUIL",
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
        "CELULAR1",
        "WSP1",
        "CELULAR2",
        "WSP2",
        "CELULAR3",
        "WSP3",
        "CELULAR4",
        "WSP4",
        "CELULAR5",
        "WSP5",
        "EMAIL1",
        "EMAIL2",
        "EMAIL3",
        "CODIGOOS",
        "OBRASOCIAL",
        "FECNANAC",
        "EDAD"
    ];

    public static IReadOnlyDictionary<string, string> GetHeaderToCanonicalMap(ImportStage stage)
    {
        return stage switch
        {
            ImportStage.HernanRaw => HernanRawMap.Value,
            ImportStage.SergioReturn => SergioReturnMap.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "La etapa de importación no es compatible.")
        };
    }

    public static bool IsRequiredPreparationHeader(ImportStage stage, string normalizedHeader)
    {
        return stage == ImportStage.HernanRaw
            && HernanPreparationRequiredHeaders.Contains(normalizedHeader);
    }

    private static IReadOnlyDictionary<string, string> BuildHernanRawMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddRange(map, HernanRawHeaders33, header => header switch
        {
            "CUIL" => "cuil",
            "CUIL_APENOM" => "apellido_nombre",
            "CUIL_DNI" => "dni",
            "CUIL_FECHANAC" => "fecha_nacimiento",
            "CUIL_EDAD" => "edad",
            "CUIL_SEXO" => "sexo",
            "CUIL_TIPODOC" => "tipo_dni",
            "CUIL_DIRECCION" => "direccion",
            "CUIL_CP" => "codigo_postal",
            "CUIL_LOCALIDAD" => "localidad",
            "CUIL_PROVINCIA" => "provincia",
            "CUIL_NACIONALIDAD" => "nacionalidad",
            "CUIL_TELFIJO1" => "telefono_fijo_1",
            "CUIL_TELFIJO2" => "telefono_fijo_2",
            "CUIL_TELFIJO3" => "telefono_fijo_3",
            "CUIL_TELFIJO4" => "telefono_fijo_4",
            "CUIL_TELFIJO5" => "telefono_fijo_5",
            "CUIL_CEL1" => "celular_1",
            "CUIL_CEL2" => "celular_2",
            "CUIL_CEL3" => "celular_3",
            "CUIL_CEL4" => "celular_4",
            "CUIL_CEL5" => "celular_5",
            "CUIL_WSP1" => "whatsapp_1",
            "CUIL_WSP2" => "whatsapp_2",
            "CUIL_WSP3" => "whatsapp_3",
            "CUIL_WSP4" => "whatsapp_4",
            "CUIL_WSP5" => "whatsapp_5",
            "CUIL_EMAIL1" => "email_1",
            "CUIL_EMAIL2" => "email_2",
            "CUIL_EMAIL3" => "email_3",
            "CUIL_CODOS" => "codigo_obra_social",
            "CUIL_DESCRIPOS" => "obra_social",
            "CUIL_CUITEMPL" => "cuit_empleador",
            _ => throw new InvalidOperationException($"El encabezado de origen de Hernán '{header}' es desconocido.")
        });

        return map;
    }

    private static IReadOnlyDictionary<string, string> BuildSergioReturnMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddRange(map, SergioTemplateHeaders36, header => header switch
        {
            "CUIL" => "cuil",
            "DNI" => "dni",
            "TIPO DNI" => "tipo_dni",
            "APELLIDO" => "apellido",
            "NOMBRE" => "nombre",
            "SEXO" => "sexo",
            "FECHA DE NAC" => "fecha_nacimiento",
            "EDAD" => "edad",
            "DIRECCION" => "direccion",
            "CP" => "codigo_postal",
            "LOCALIDAD" => "localidad",
            "PARTIDO" => "partido",
            "PROVINCIA" => "provincia",
            "NACIONALIDAD" => "nacionalidad",
            "TEL FIJO 1" => "telefono_fijo_1",
            "TEL FIJO 2" => "telefono_fijo_2",
            "TEL FIJO 3" => "telefono_fijo_3",
            "TEL FIJO 4" => "telefono_fijo_4",
            "TEL FIJO 5" => "telefono_fijo_5",
            "CEL1" => "celular_1",
            "CEL2" => "celular_2",
            "CEL3" => "celular_3",
            "CEL4" => "celular_4",
            "CEL5" => "celular_5",
            "WSP1" => "whatsapp_1",
            "WSP2" => "whatsapp_2",
            "WSP3" => "whatsapp_3",
            "WSP4" => "whatsapp_4",
            "WSP5" => "whatsapp_5",
            "EMAIL1" => "email_1",
            "EMAIL2" => "email_2",
            "EMAIL3" => "email_3",
            "CODIGO" => "codigo_obra_social",
            "OBRA SOCIAL" => "obra_social",
            "CUIT" => "cuit_empleador",
            "ANIO" => "anio",
            _ => throw new InvalidOperationException($"El encabezado de plantilla de Sergio '{header}' es desconocido.")
        });

        map["FECNANAC"] = "fecha_nacimiento";
        map["TIPODOC"] = "tipo_dni";
        map["TELPART1"] = "telefono_fijo_1";
        map["TELPART2"] = "telefono_fijo_2";
        map["TELPART3"] = "telefono_fijo_3";
        map["TELPART4"] = "telefono_fijo_4";
        map["TELPART5"] = "telefono_fijo_5";
        map["CELULAR1"] = "celular_1";
        map["CELULAR2"] = "celular_2";
        map["CELULAR3"] = "celular_3";
        map["CELULAR4"] = "celular_4";
        map["CELULAR5"] = "celular_5";
        map["CODIGOOS"] = "codigo_obra_social";
        map["OBRASOCIAL"] = "obra_social";
        map["CUITEMPLEADOR"] = "cuit_empleador";

        return map;
    }

    private static void AddRange(Dictionary<string, string> map, IEnumerable<string> headers, Func<string, string> mapSelector)
    {
        foreach (var header in headers)
        {
            map[header] = mapSelector(header);
        }
    }
}
