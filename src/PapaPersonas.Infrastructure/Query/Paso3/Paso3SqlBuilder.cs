using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Query.Paso3;

namespace PapaPersonas.Infrastructure.Query.Paso3;

internal static class Paso3SqlBuilder
{
    internal static readonly IReadOnlyDictionary<string, string> ColumnExpressions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cuil"] = "cuil",
            ["dni"] = "dni",
            ["fecha_nacimiento"] = "fecha_nacimiento",
            ["sexo"] = "sexo",
            ["tipo_dni"] = "tipo_dni",
            ["apellido"] = "apellido",
            ["nombre"] = "nombre",
            ["direccion"] = "direccion",
            ["codigo_postal"] = "codigo_postal",
            ["localidad"] = "localidad",
            ["partido"] = "partido",
            ["provincia"] = "provincia",
            ["nacionalidad"] = "nacionalidad",
            ["telefono_fijo_1"] = "telefono_fijo_1",
            ["telefono_fijo_2"] = "telefono_fijo_2",
            ["telefono_fijo_3"] = "telefono_fijo_3",
            ["telefono_fijo_4"] = "telefono_fijo_4",
            ["telefono_fijo_5"] = "telefono_fijo_5",
            ["celular_1"] = "celular_1",
            ["celular_2"] = "celular_2",
            ["celular_3"] = "celular_3",
            ["celular_4"] = "celular_4",
            ["celular_5"] = "celular_5",
            ["whatsapp_1"] = "whatsapp_1",
            ["whatsapp_2"] = "whatsapp_2",
            ["whatsapp_3"] = "whatsapp_3",
            ["whatsapp_4"] = "whatsapp_4",
            ["whatsapp_5"] = "whatsapp_5",
            ["email_1"] = "email_1",
            ["email_2"] = "email_2",
            ["email_3"] = "email_3",
            ["email_4"] = "email_4",
            ["email_5"] = "email_5",
            ["codigo_obra_social"] = "codigo_obra_social",
            ["obra_social"] = "obra_social",
            ["cuit_empleador"] = "cuit_empleador",
            ["edad"] = "edad",
            ["anio"] = "anio",
            ["fecha_importacion"] = "fecha_importacion",
            ["fecha_actualizacion"] = "fecha_actualizacion"
        };

    private static readonly HashSet<string> DateColumns =
    [
        "fecha_importacion",
        "fecha_actualizacion",
        "fecha_nacimiento"
    ];

    private static readonly HashSet<string> TextOperators = ["eq", "contains", "starts_with"];
    private static readonly HashSet<string> DateOperators = ["eq", "gte", "lte", "between"];

    /// <summary>Construye SQL parametrizado con filtros, columnas y paginación deterministas.</summary>
    public static Paso3BuiltQuery Build(Paso3PreviewRequest request, bool includePagination)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selectedColumns = NormalizeSelectedColumns(request.SelectedColumns);
        var whereClauses = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        if (!string.IsNullOrWhiteSpace(request.ExactCuil))
        {
            var validation = CuilValidator.Validate(request.ExactCuil);
            if (!validation.IsValid)
            {
                whereClauses.Add("1 = 0");
            }
            else
            {
                var parameterName = NextParameter(parameters.Count);
                whereClauses.Add($"cuil = ${parameterName}");
                parameters.Add((parameterName, validation.NormalizedCuil));
            }
        }

        foreach (var filter in request.Filters ?? [])
        {
            var column = NormalizeColumn(filter.Column);
            if (!ColumnExpressions.ContainsKey(column))
            {
                throw new ArgumentException($"La columna de filtro '{filter.Column}' no está permitida.", nameof(request));
            }

            var op = NormalizeOperator(filter.Operator);
            if (DateColumns.Contains(column))
            {
                if (!DateOperators.Contains(op))
                {
                    throw new ArgumentException($"El operador de fecha '{filter.Operator}' no está permitido para la columna '{filter.Column}'.", nameof(request));
                }

                AppendDateClause(column, op, filter.Value, whereClauses, parameters);
            }
            else
            {
                if (!TextOperators.Contains(op))
                {
                    throw new ArgumentException($"El operador de texto '{filter.Operator}' no está permitido para la columna '{filter.Column}'.", nameof(request));
                }

                AppendTextClause(column, op, filter.Value, whereClauses, parameters);
            }
        }

        if (request.ImportDateFrom.HasValue)
        {
            var name = NextParameter(parameters.Count);
            whereClauses.Add($"fecha_importacion >= ${name}");
            parameters.Add((name, request.ImportDateFrom.Value));
        }

        if (request.ImportDateTo.HasValue)
        {
            var name = NextParameter(parameters.Count);
            whereClauses.Add($"fecha_importacion <= ${name}");
            parameters.Add((name, request.ImportDateTo.Value));
        }

        var whereSql = whereClauses.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", whereClauses)}";

        const string orderBy = "ORDER BY fecha_importacion DESC NULLS LAST, cuil ASC";

        var projection = string.Join(", ", selectedColumns.Select(column => $"{ColumnExpressions[column]} AS {column}"));
        var previewSql = $"SELECT {projection} FROM personas {whereSql} {orderBy}";

        if (includePagination)
        {
            var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
            var pageSize = request.PageSize <= 0
                ? Paso3PreviewDefaults.DefaultPageSize
                : Math.Min(request.PageSize, Paso3PreviewDefaults.MaxPageSize);

            var limitName = NextParameter(parameters.Count);
            parameters.Add((limitName, pageSize));

            var offsetName = NextParameter(parameters.Count);
            parameters.Add((offsetName, (pageNumber - 1) * pageSize));

            previewSql += $" LIMIT ${limitName} OFFSET ${offsetName}";
        }

        return new Paso3BuiltQuery(
            SelectedColumns: selectedColumns,
            PreviewSql: previewSql,
            CountSql: $"SELECT COUNT(*) FROM personas {whereSql}",
            LatestImportDateSql: $"SELECT MAX(fecha_importacion) FROM personas {whereSql}",
            Parameters: parameters);
    }

    private static void AppendTextClause(
        string column,
        string op,
        string value,
        List<string> whereClauses,
        List<(string Name, object? Value)> parameters)
    {
        var name = NextParameter(parameters.Count);
        switch (op)
        {
            case "eq":
                whereClauses.Add($"LOWER({column}) = LOWER(${name})");
                parameters.Add((name, value));
                break;
            case "contains":
                whereClauses.Add($"LOWER({column}) LIKE LOWER(${name})");
                parameters.Add((name, $"%{value}%"));
                break;
            case "starts_with":
                whereClauses.Add($"LOWER({column}) LIKE LOWER(${name})");
                parameters.Add((name, $"{value}%"));
                break;
        }
    }

    private static void AppendDateClause(
        string column,
        string op,
        string value,
        List<string> whereClauses,
        List<(string Name, object? Value)> parameters)
    {
        switch (op)
        {
            case "eq":
            {
                var date = ParseDateValue(value, allowRange: false).Single();
                var name = NextParameter(parameters.Count);
                whereClauses.Add($"CAST({column} AS DATE) = ${name}");
                parameters.Add((name, date));
                break;
            }
            case "gte":
            {
                var date = ParseDateValue(value, allowRange: false).Single();
                var name = NextParameter(parameters.Count);
                whereClauses.Add($"CAST({column} AS DATE) >= ${name}");
                parameters.Add((name, date));
                break;
            }
            case "lte":
            {
                var date = ParseDateValue(value, allowRange: false).Single();
                var name = NextParameter(parameters.Count);
                whereClauses.Add($"CAST({column} AS DATE) <= ${name}");
                parameters.Add((name, date));
                break;
            }
            case "between":
            {
                var range = ParseDateValue(value, allowRange: true);
                var startName = NextParameter(parameters.Count);
                parameters.Add((startName, range[0]));
                var endName = NextParameter(parameters.Count);
                parameters.Add((endName, range[1]));
                whereClauses.Add($"CAST({column} AS DATE) BETWEEN ${startName} AND ${endName}");
                break;
            }
        }
    }

    private static IReadOnlyList<DateOnly> ParseDateValue(string value, bool allowRange)
    {
        if (!allowRange)
        {
            if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new ArgumentException($"El valor de fecha '{value}' no es válido.");
            }

            return [date];
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !DateOnly.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateOnly.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            throw new ArgumentException($"El rango de fechas entre '{value}' no es válido.");
        }

        if (start > end)
        {
            throw new ArgumentException("El inicio del rango debe ser <= al final.");
        }

        return [start, end];
    }

    private static IReadOnlyList<string> NormalizeSelectedColumns(IReadOnlyList<string> selectedColumns)
    {
        var normalized = (selectedColumns ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeColumn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.Add("cuil");
            normalized.Add("apellido");
            normalized.Add("nombre");
            normalized.Add("fecha_importacion");
        }

        foreach (var column in normalized)
        {
            if (!ColumnExpressions.ContainsKey(column))
            {
                throw new ArgumentException($"La columna seleccionada '{column}' no está permitida.");
            }
        }

        return normalized;
    }

    private static string NormalizeColumn(string value) => value.Trim().ToLowerInvariant();

    private static string NormalizeOperator(string value) => value.Trim().ToLowerInvariant();

    private static string NextParameter(int index) => $"p{index}";
}

internal sealed record Paso3BuiltQuery(
    IReadOnlyList<string> SelectedColumns,
    string PreviewSql,
    string CountSql,
    string LatestImportDateSql,
    IReadOnlyList<(string Name, object? Value)> Parameters);
