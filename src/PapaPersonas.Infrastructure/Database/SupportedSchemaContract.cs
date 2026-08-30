using DuckDB.NET.Data;
using PapaPersonas.Core.Database;
using System.Text.RegularExpressions;

namespace PapaPersonas.Infrastructure.Database;

internal static class SupportedSchemaContract
{
    public const int SupportedSchemaVersion = 5;

    private static readonly IReadOnlyList<TableDefinition> RequiredTables =
    [
        Table(
            "schema_metadata",
            [
                Column("schema_version", "INTEGER", nullable: false),
                Column("initialized_utc", "TIMESTAMP", nullable: false)
            ],
            primaryKeyColumns: ["schema_version"]),
        Table(
            "personas",
            [
                Column("cuil", "VARCHAR", nullable: false),
                Column("dni", "VARCHAR"),
                Column("fecha_nacimiento", "DATE"),
                Column("sexo", "VARCHAR"),
                Column("tipo_dni", "VARCHAR"),
                Column("apellido", "VARCHAR"),
                Column("nombre", "VARCHAR"),
                Column("direccion", "VARCHAR"),
                Column("codigo_postal", "VARCHAR"),
                Column("localidad", "VARCHAR"),
                Column("partido", "VARCHAR"),
                Column("provincia", "VARCHAR"),
                Column("nacionalidad", "VARCHAR"),
                Column("telefono_fijo_1", "VARCHAR"),
                Column("telefono_fijo_2", "VARCHAR"),
                Column("telefono_fijo_3", "VARCHAR"),
                Column("telefono_fijo_4", "VARCHAR"),
                Column("telefono_fijo_5", "VARCHAR"),
                Column("celular_1", "VARCHAR"),
                Column("celular_2", "VARCHAR"),
                Column("celular_3", "VARCHAR"),
                Column("celular_4", "VARCHAR"),
                Column("celular_5", "VARCHAR"),
                Column("whatsapp_1", "VARCHAR"),
                Column("whatsapp_2", "VARCHAR"),
                Column("whatsapp_3", "VARCHAR"),
                Column("whatsapp_4", "VARCHAR"),
                Column("whatsapp_5", "VARCHAR"),
                Column("email_1", "VARCHAR"),
                Column("email_2", "VARCHAR"),
                Column("email_3", "VARCHAR"),
                Column("email_4", "VARCHAR"),
                Column("email_5", "VARCHAR"),
                Column("codigo_obra_social", "VARCHAR"),
                Column("obra_social", "VARCHAR"),
                Column("cuit_empleador", "VARCHAR"),
                Column("edad", "SMALLINT"),
                Column("anio", "SMALLINT"),
                Column("fecha_actualizacion", "TIMESTAMP", nullable: false),
                Column("fecha_importacion", "DATE"),
                Column("source_import_id", "UUID"),
                Column("source_row_number", "BIGINT")
            ],
            primaryKeyColumns: ["cuil"]),
        Table(
            "import_runs",
            [
                Column("import_id", "UUID", nullable: false),
                Column("stage_type", "VARCHAR", nullable: false),
                Column("source_file_name", "VARCHAR", nullable: false),
                Column("source_file_path", "VARCHAR", nullable: false),
                Column("status", "VARCHAR", nullable: false),
                Column("started_utc", "TIMESTAMP", nullable: false),
                Column("analyzed_utc", "TIMESTAMP"),
                Column("completed_utc", "TIMESTAMP"),
                Column("total_rows", "BIGINT", nullable: false),
                Column("valid_rows", "BIGINT", nullable: false),
                Column("rejected_rows", "BIGINT", nullable: false),
                Column("missing_cuil_rows", "BIGINT", nullable: false),
                Column("duplicate_cuil_rows", "BIGINT", nullable: false),
                Column("malformed_cuil_rows", "BIGINT", nullable: false),
                Column("rows_to_insert", "BIGINT", nullable: false),
                Column("rows_to_update", "BIGINT", nullable: false),
                Column("error_message", "VARCHAR"),
                Column("source_columns_present_json", "VARCHAR"),
                Column("fecha_importacion", "DATE")
            ],
            primaryKeyColumns: ["import_id"],
            checkConstraints:
            [
                "stage_type IN ('hernan_raw', 'sergio_return')",
                "status IN ('pending', 'analyzing', 'ready_for_confirmation', 'applying', 'completed', 'failed')"
            ]),
        Table(
            "personas_staging",
            [
                Column("import_id", "UUID", nullable: false),
                Column("source_row_number", "BIGINT", nullable: false),
                Column("cuil", "VARCHAR"),
                Column("dni", "VARCHAR"),
                Column("fecha_nacimiento", "DATE"),
                Column("sexo", "VARCHAR"),
                Column("tipo_dni", "VARCHAR"),
                Column("apellido", "VARCHAR"),
                Column("nombre", "VARCHAR"),
                Column("direccion", "VARCHAR"),
                Column("codigo_postal", "VARCHAR"),
                Column("localidad", "VARCHAR"),
                Column("partido", "VARCHAR"),
                Column("provincia", "VARCHAR"),
                Column("nacionalidad", "VARCHAR"),
                Column("telefono_fijo_1", "VARCHAR"),
                Column("telefono_fijo_2", "VARCHAR"),
                Column("telefono_fijo_3", "VARCHAR"),
                Column("telefono_fijo_4", "VARCHAR"),
                Column("telefono_fijo_5", "VARCHAR"),
                Column("celular_1", "VARCHAR"),
                Column("celular_2", "VARCHAR"),
                Column("celular_3", "VARCHAR"),
                Column("celular_4", "VARCHAR"),
                Column("celular_5", "VARCHAR"),
                Column("whatsapp_1", "VARCHAR"),
                Column("whatsapp_2", "VARCHAR"),
                Column("whatsapp_3", "VARCHAR"),
                Column("whatsapp_4", "VARCHAR"),
                Column("whatsapp_5", "VARCHAR"),
                Column("email_1", "VARCHAR"),
                Column("email_2", "VARCHAR"),
                Column("email_3", "VARCHAR"),
                Column("email_4", "VARCHAR"),
                Column("email_5", "VARCHAR"),
                Column("codigo_obra_social", "VARCHAR"),
                Column("obra_social", "VARCHAR"),
                Column("cuit_empleador", "VARCHAR"),
                Column("edad", "SMALLINT"),
                Column("anio", "SMALLINT"),
                Column("fecha_actualizacion", "TIMESTAMP"),
                Column("validation_outcome", "VARCHAR", nullable: false),
                Column("validation_error", "VARCHAR")
            ],
            primaryKeyColumns: ["import_id", "source_row_number"],
            foreignKeys: [ForeignKey(["import_id"], "import_runs", ["import_id"])],
            checkConstraints: ["validation_outcome IN ('pending', 'valid', 'rejected')"]),
        Table(
            "stock_headers",
            [
                Column("stock_id", "UUID", nullable: false),
                Column("source_fecha_importacion", "DATE", nullable: false),
                Column("source_import_id", "UUID"),
                Column("generated_utc", "TIMESTAMP", nullable: false),
                Column("pending_extraction_token", "UUID"),
                Column("pending_started_utc", "TIMESTAMP"),
                Column("pending_output_path", "VARCHAR"),
                Column("pending_expected_rows", "BIGINT"),
                Column("pending_selected_columns_json", "VARCHAR")
            ],
            primaryKeyColumns: ["stock_id"],
            checkConstraints:
            [
                "pending_expected_rows IS NULL OR pending_expected_rows >= 0",
                "(pending_extraction_token IS NULL AND pending_started_utc IS NULL AND pending_output_path IS NULL AND pending_expected_rows IS NULL AND pending_selected_columns_json IS NULL) OR (pending_extraction_token IS NOT NULL AND pending_started_utc IS NOT NULL AND pending_output_path IS NOT NULL AND pending_expected_rows IS NOT NULL AND pending_selected_columns_json IS NOT NULL)"
            ]),
        Table(
            "stock_members",
            [
                Column("stock_id", "UUID", nullable: false),
                Column("cuil", "VARCHAR", nullable: false),
                Column("codigo_obra_social", "VARCHAR"),
                Column("obra_social", "VARCHAR"),
                Column("source_order", "BIGINT", nullable: false),
                Column("vendido", "BOOLEAN", nullable: false),
                Column("fecha_venta", "TIMESTAMP"),
                Column("extraction_token", "UUID")
            ],
            primaryKeyColumns: ["stock_id", "cuil"],
            foreignKeys: [ForeignKey(["stock_id"], "stock_headers", ["stock_id"])],
            checkConstraints:
            [
                "(vendido = FALSE AND fecha_venta IS NULL) OR (vendido = TRUE AND fecha_venta IS NOT NULL)"
            ])
    ];

    /// <summary>Valida la estructura completa de la conexión contra el contrato de esquema soportado.</summary>
    public static DatabaseSchemaValidationResult Validate(DuckDBConnection connection)
    {
        var issues = new List<DatabaseSchemaValidationIssue>();

        foreach (var table in RequiredTables)
        {
            ValidateTable(connection, table, issues);
        }

        return issues.Count == 0
            ? DatabaseSchemaValidationResult.Success()
            : DatabaseSchemaValidationResult.Failure(issues);
    }

    public static bool Validate(DuckDBConnection connection, out string message)
    {
        var result = Validate(connection);
        message = result.Message;
        return result.IsSuccess;
    }

    private static bool TableExists(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'main' AND table_name = $tableName;";
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void ValidateTable(DuckDBConnection connection, TableDefinition table, List<DatabaseSchemaValidationIssue> issues)
    {
        if (!TableExists(connection, table.TableName))
        {
            issues.Add(Issue(DatabaseSchemaValidationCode.MissingTable, table.TableName, $"Falta la tabla obligatoria '{table.TableName}'."));
            return;
        }

        var actualColumns = ReadColumns(connection, table.TableName);
        var actualColumnsByName = actualColumns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var expectedColumn in table.Columns)
        {
            if (!actualColumnsByName.TryGetValue(expectedColumn.Name, out var actualColumn))
            {
                issues.Add(Issue(
                    DatabaseSchemaValidationCode.MissingColumn,
                    table.TableName,
                    $"Falta la columna obligatoria '{table.TableName}.{expectedColumn.Name}'.",
                    expectedColumn.Name,
                    expectedColumn.DuckDbType,
                    "missing"));
                continue;
            }

            if (!string.Equals(expectedColumn.DuckDbType, actualColumn.DuckDbType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Issue(
                    DatabaseSchemaValidationCode.ColumnTypeMismatch,
                    table.TableName,
                    $"La columna '{table.TableName}.{expectedColumn.Name}' tiene tipo '{actualColumn.DuckDbType}', se esperaba '{expectedColumn.DuckDbType}'.",
                    expectedColumn.Name,
                    expectedColumn.DuckDbType,
                    actualColumn.DuckDbType));
            }

            if (expectedColumn.IsNullable != actualColumn.IsNullable)
            {
                issues.Add(Issue(
                    DatabaseSchemaValidationCode.ColumnNullabilityMismatch,
                    table.TableName,
                    $"La nulabilidad de '{table.TableName}.{expectedColumn.Name}' es '{DescribeNullability(actualColumn.IsNullable)}'; se esperaba '{DescribeNullability(expectedColumn.IsNullable)}'.",
                    expectedColumn.Name,
                    DescribeNullability(expectedColumn.IsNullable),
                    DescribeNullability(actualColumn.IsNullable)));
            }
        }

        foreach (var unexpectedColumn in actualColumnsByName.Keys.Where(columnName => table.Columns.All(expectedColumn => !string.Equals(expectedColumn.Name, columnName, StringComparison.OrdinalIgnoreCase))))
        {
            issues.Add(Issue(
                DatabaseSchemaValidationCode.UnexpectedColumn,
                table.TableName,
                $"La columna inesperada '{table.TableName}.{unexpectedColumn}' está presente.",
                unexpectedColumn,
                "not present",
                "present"));
        }

        ValidateKey(connection, table.TableName, "PRIMARY KEY", table.PrimaryKeyColumns, issues);

        foreach (var uniqueConstraint in table.UniqueConstraints)
        {
            ValidateKey(connection, table.TableName, "UNIQUE", uniqueConstraint, issues);
        }

        var actualForeignKeys = ReadForeignKeys(connection, table.TableName);
        ValidateForeignKeys(table.TableName, table.ForeignKeys, actualForeignKeys, issues);

        var actualCheckConstraints = ReadCheckConstraints(connection, table.TableName);
        ValidateCheckConstraints(table.TableName, table.CheckConstraints, actualCheckConstraints, issues);

    }

    private static IReadOnlyList<TableColumn> ReadColumns(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_schema = 'main' AND table_name = $tableName ORDER BY ordinal_position;";
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));

        var columns = new List<TableColumn>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(new TableColumn(
                reader.GetString(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)));
        }

        return columns;
    }

    private static void ValidateKey(DuckDBConnection connection, string tableName, string constraintType, IReadOnlyList<string> expectedColumns, List<DatabaseSchemaValidationIssue> issues)
    {
        var actualColumns = ReadKeyColumns(connection, tableName, constraintType);
        if (!actualColumns.SequenceEqual(expectedColumns, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                constraintType == "PRIMARY KEY" ? DatabaseSchemaValidationCode.PrimaryKeyMismatch : DatabaseSchemaValidationCode.UniqueConstraintMismatch,
                tableName,
                $"La tabla '{tableName}' tiene columnas {constraintType} [{string.Join(", ", actualColumns)}]; se esperaban [{string.Join(", ", expectedColumns)}].",
                expected: string.Join(", ", expectedColumns),
                actual: string.Join(", ", actualColumns)));
        }
    }

    private static IReadOnlyList<string> ReadKeyColumns(DuckDBConnection connection, string tableName, string constraintType)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT kcu.column_name
            FROM information_schema.table_constraints tc
            INNER JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
               AND tc.constraint_schema = kcu.constraint_schema
            WHERE tc.table_schema = 'main'
              AND tc.table_name = $tableName
              AND tc.constraint_type = $constraintType
            ORDER BY kcu.ordinal_position;
            """;
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));
        command.Parameters.Add(new DuckDBParameter("constraintType", constraintType));

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static void ValidateForeignKeys(string tableName, IReadOnlyList<ForeignKeyDefinition> expectedForeignKeys, IReadOnlyList<ForeignKeyDefinition> actualForeignKeys, List<DatabaseSchemaValidationIssue> issues)
    {
        var expectedDescriptors = expectedForeignKeys.Select(DescribeForeignKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualDescriptors = actualForeignKeys.Select(DescribeForeignKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expectedForeignKey in expectedDescriptors)
        {
            if (!actualDescriptors.Contains(expectedForeignKey))
            {
                issues.Add(Issue(
                    DatabaseSchemaValidationCode.ForeignKeyMismatch,
                    tableName,
                    $"Falta la FOREIGN KEY {expectedForeignKey} en la tabla '{tableName}'.",
                    expected: expectedForeignKey,
                    actual: "missing"));
            }
        }

        foreach (var actualForeignKey in actualDescriptors)
        {
            if (!expectedDescriptors.Contains(actualForeignKey))
            {
                issues.Add(Issue(
                    DatabaseSchemaValidationCode.ForeignKeyMismatch,
                    tableName,
                    $"La tabla '{tableName}' tiene una FOREIGN KEY inesperada {actualForeignKey}.",
                    expected: "not present",
                    actual: actualForeignKey));
            }
        }
    }

    private static void ValidateCheckConstraints(string tableName, IReadOnlyList<string> expectedChecks, IReadOnlyList<string> actualChecks, List<DatabaseSchemaValidationIssue> issues)
    {
        var expectedDescriptors = expectedChecks.Select(NormalizeConstraintExpression).ToHashSet(StringComparer.Ordinal);
        var actualDescriptors = actualChecks.Select(NormalizeConstraintExpression).ToHashSet(StringComparer.Ordinal);

        foreach (var expectedCheck in expectedChecks)
        {
            var expectedDescriptor = NormalizeConstraintExpression(expectedCheck);
            if (!actualDescriptors.Contains(expectedDescriptor))
            {
                issues.Add(Issue(
                    DatabaseSchemaValidationCode.CheckConstraintMismatch,
                    tableName,
                    $"Falta la restricción CHECK '{expectedCheck}' en la tabla '{tableName}'.",
                    expected: expectedDescriptor,
                    actual: "missing"));
            }
        }

        foreach (var actualCheck in actualChecks)
        {
            var actualDescriptor = NormalizeConstraintExpression(actualCheck);
            if (!expectedDescriptors.Contains(actualDescriptor))
            {
                issues.Add(Issue(
                    DatabaseSchemaValidationCode.CheckConstraintMismatch,
                    tableName,
                    $"La tabla '{tableName}' tiene una restricción CHECK inesperada '{actualCheck}'.",
                    expected: "not present",
                    actual: actualDescriptor));
            }
        }
    }

    private static string DescribeForeignKey(ForeignKeyDefinition foreignKey)
    {
        return $"{string.Join(", ", foreignKey.LocalColumns)} -> {foreignKey.ReferencedTable}({string.Join(", ", foreignKey.ReferencedColumns)})";
    }

    private static IReadOnlyList<ForeignKeyDefinition> ReadForeignKeys(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                kcu.constraint_name,
                kcu.column_name,
                ccu.table_name AS referenced_table,
                ccu.column_name AS referenced_column,
                kcu.ordinal_position
            FROM information_schema.table_constraints tc
            INNER JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
               AND tc.constraint_schema = kcu.constraint_schema
            INNER JOIN information_schema.referential_constraints rc
                ON tc.constraint_name = rc.constraint_name
               AND tc.constraint_schema = rc.constraint_schema
            INNER JOIN information_schema.constraint_column_usage ccu
                ON rc.unique_constraint_name = ccu.constraint_name
               AND rc.unique_constraint_schema = ccu.constraint_schema
            WHERE tc.table_schema = 'main'
              AND tc.table_name = $tableName
              AND tc.constraint_type = 'FOREIGN KEY'
            ORDER BY kcu.constraint_name, kcu.ordinal_position;
            """;
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));

        var foreignKeys = new Dictionary<string, ForeignKeyBuilder>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var constraintName = reader.GetString(0);
            if (!foreignKeys.TryGetValue(constraintName, out var builder))
            {
                builder = new ForeignKeyBuilder(reader.GetString(2));
                foreignKeys.Add(constraintName, builder);
            }

            builder.LocalColumns.Add(reader.GetString(1));
            builder.ReferencedColumns.Add(reader.GetString(3));
        }

        return foreignKeys.Values
            .Select(builder => new ForeignKeyDefinition(builder.LocalColumns, builder.ReferencedTable, builder.ReferencedColumns))
            .ToArray();
    }

    private static IReadOnlyList<string> ReadCheckConstraints(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT expression FROM duckdb_constraints() WHERE schema_name = 'main' AND table_name = $tableName AND constraint_type = 'CHECK' ORDER BY constraint_index;";
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));

        var constraints = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            constraints.Add(reader.GetString(0));
        }

        return constraints;
    }

    private static string NormalizeConstraintExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        // TODO(database-maintenance-hardening): Preserve CHECK grouping semantics here; stripping parentheses can merge distinct predicates. Use a structural/canonical parser before accepting external schemas.
        var normalized = Regex.Replace(
                expression.Trim()
                    .Replace("\"", string.Empty)
                    .Replace("'", string.Empty)
                    .Replace("(", string.Empty)
                    .Replace(")", string.Empty),
                "\\s+",
                string.Empty)
            .ToLowerInvariant();

        return normalized
            .Replace("castfasboolean", "false")
            .Replace("casttasboolean", "true");
    }

    private static DatabaseSchemaValidationIssue Issue(DatabaseSchemaValidationCode code, string tableName, string message, string? columnName = null, string? expected = null, string? actual = null)
    {
        return new DatabaseSchemaValidationIssue(code, tableName, message, columnName, expected, actual);
    }

    private static string DescribeNullability(bool isNullable) => isNullable ? "NULL" : "NOT NULL";

    private static TableDefinition Table(
        string tableName,
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<string>? primaryKeyColumns = null,
        IReadOnlyList<IReadOnlyList<string>>? uniqueConstraints = null,
        IReadOnlyList<ForeignKeyDefinition>? foreignKeys = null,
        IReadOnlyList<string>? checkConstraints = null)
    {
        return new TableDefinition(
            tableName,
            columns,
            primaryKeyColumns ?? Array.Empty<string>(),
            uniqueConstraints ?? Array.Empty<IReadOnlyList<string>>(),
            foreignKeys ?? Array.Empty<ForeignKeyDefinition>(),
            checkConstraints ?? Array.Empty<string>());
    }

    private static TableColumn Column(string name, string duckDbType, bool nullable = true) => new(name, duckDbType, nullable);

    private static ForeignKeyDefinition ForeignKey(IReadOnlyList<string> localColumns, string referencedTable, IReadOnlyList<string> referencedColumns) =>
        new(localColumns, referencedTable, referencedColumns);

    private sealed record TableDefinition(
        string TableName,
        IReadOnlyList<TableColumn> Columns,
        IReadOnlyList<string> PrimaryKeyColumns,
        IReadOnlyList<IReadOnlyList<string>> UniqueConstraints,
        IReadOnlyList<ForeignKeyDefinition> ForeignKeys,
        IReadOnlyList<string> CheckConstraints);

    private sealed record TableColumn(string Name, string DuckDbType, bool IsNullable);

    private sealed record ForeignKeyDefinition(IReadOnlyList<string> LocalColumns, string ReferencedTable, IReadOnlyList<string> ReferencedColumns);

    private sealed class ForeignKeyBuilder
    {
        public ForeignKeyBuilder(string referencedTable)
        {
            ReferencedTable = referencedTable;
        }

        public string ReferencedTable { get; }

        public List<string> LocalColumns { get; } = [];

        public List<string> ReferencedColumns { get; } = [];
    }
}
