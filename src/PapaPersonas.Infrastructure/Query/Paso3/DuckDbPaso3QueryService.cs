using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.Core.Query.Paso3;

namespace PapaPersonas.Infrastructure.Query.Paso3;

public sealed class DuckDbPaso3QueryService : IPaso3QueryService
{
    /// <summary>Ejecuta la vista previa paginada y devuelve filas, total y última fecha de importación.</summary>
    public Paso3PreviewResult QueryPreview(Paso3PreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.DatabasePath) || !File.Exists(request.DatabasePath))
        {
            throw new ArgumentException("No se encontró el archivo de base de datos.", nameof(request));
        }

        var built = Paso3SqlBuilder.Build(request, includePagination: true);

        using var connection = OpenConnection(request.DatabasePath);

        var rows = QueryRows(connection, built);
        var totalCount = QueryCount(connection, built);
        var latestImportDate = QueryLatestImportDate(connection, built);

        return new Paso3PreviewResult(rows, totalCount, latestImportDate);
    }

    internal static DuckDBConnection OpenConnection(string databasePath)
    {
        var builder = new DuckDBConnectionStringBuilder { DataSource = databasePath };
        var connection = new DuckDBConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    internal static List<Paso3PreviewRow> QueryRows(DuckDBConnection connection, Paso3BuiltQuery built)
    {
        using var command = connection.CreateCommand();
        command.CommandText = built.PreviewSql;
        AddParameters(command, built.Parameters);

        using var reader = command.ExecuteReader();
        var rows = new List<Paso3PreviewRow>();
        while (reader.Read())
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var key = reader.GetName(i);
                values[key] = reader.IsDBNull(i) ? null : ConvertReaderValue(reader, i);
            }

            rows.Add(new Paso3PreviewRow(values));
        }

        return rows;
    }

    internal static int QueryCount(DuckDBConnection connection, Paso3BuiltQuery built)
    {
        using var command = connection.CreateCommand();
        command.CommandText = built.CountSql;
        AddParameters(command, built.Parameters);
        var value = command.ExecuteScalar();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    internal static DateOnly? QueryLatestImportDate(DuckDBConnection connection, Paso3BuiltQuery built)
    {
        using var command = connection.CreateCommand();
        command.CommandText = built.LatestImportDateSql;
        AddParameters(command, built.Parameters);

        var value = command.ExecuteScalar();
        if (value is null || value is DBNull)
        {
            return null;
        }

        return value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.Parse(value.ToString()!, CultureInfo.InvariantCulture)
        };
    }

    internal static void AddParameters(DuckDBCommand command, IReadOnlyList<(string Name, object? Value)> parameters)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            if (!used.Add(name))
            {
                continue;
            }

            command.Parameters.Add(new DuckDBParameter(name, value ?? DBNull.Value));
        }
    }

    private static object ConvertReaderValue(DuckDBDataReader reader, int ordinal)
    {
        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => raw
        };
    }
}
