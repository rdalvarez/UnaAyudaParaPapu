namespace PapaPersonas.V2.Shell;

internal static class SergioLoadMetadataSchema
{
    internal const string SourceFileNameColumn = "source_file_name";
    internal const string LoadedAtUtcColumn = "loaded_at_utc";
    internal const string RowCountColumn = "row_count";
    internal const string SelectedPrimaryKeyAliasColumn = "selected_primary_key_alias";
    internal const string SchemaVersionColumn = "schema_version";

    internal static IReadOnlyList<(string Name, string Type)> Columns { get; } =
    [
        (SourceFileNameColumn, "VARCHAR"),
        (LoadedAtUtcColumn, "TIMESTAMP"),
        (RowCountColumn, "BIGINT"),
        (SelectedPrimaryKeyAliasColumn, "VARCHAR"),
        (SchemaVersionColumn, "VARCHAR")
    ];

    internal static string CreateTableSql(string quotedTableName) =>
        $"CREATE TABLE IF NOT EXISTS {quotedTableName} (" +
        $"{SourceFileNameColumn} VARCHAR, " +
        $"{LoadedAtUtcColumn} TIMESTAMP, " +
        $"{RowCountColumn} BIGINT, " +
        $"{SelectedPrimaryKeyAliasColumn} VARCHAR, " +
        $"{SchemaVersionColumn} VARCHAR)";
}
