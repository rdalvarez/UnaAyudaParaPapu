using DuckDB.NET.Data;

namespace PapaPersonas.V2.Shell;

public sealed record SergioPreview(
    IReadOnlyList<string> Aliases,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    long TotalRows,
    string SelectedPrimaryKeyAlias,
    string SourceFileName,
    bool PreviousOfficialExists,
    SergioLoadMetadata? PreviousMetadata,
    long ValidRows = 0)
{
    public long ImportedRows => TotalRows;
    public long RejectedRows => TotalRows - ValidRows;
}

public static class SergioPreviewExtensions
{
    public static long ImportedRows(this SergioPreview preview) => preview.ImportedRows;
    public static long ValidRows(this SergioPreview preview) => preview.ValidRows;
    public static long RejectedRows(this SergioPreview preview) => preview.RejectedRows;
}

public sealed record SergioLoadMetadata(
    string SourceFileName,
    DateTime LoadedAtUtc,
    long RowCount,
    string SelectedPrimaryKeyAlias,
    string? SchemaVersion);

public sealed class SergioLoadPromoter
{
    public const string OfficialTableName = "sergio_current";
    public const string MetadataTableName = "sergio_load_metadata";
    public const string LastImportTableName = "sergio_last_import";
    private readonly SergioStagingAnalyzer analyzer;
    private readonly Action? afterOfficialReplacement;

    public SergioLoadPromoter(SergioStagingAnalyzer analyzer, Action? afterOfficialReplacement = null)
    {
        this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        this.afterOfficialReplacement = afterOfficialReplacement;
    }

    public SergioPreview Preview(string candidateTableName, string selectedPrimaryKeyAlias, string sourceFilePath)
    {
        using var operation = analyzer.AcquirePromotionLock();
        using var connection = Open(analyzer.DatabasePath);
        EnsureOwnedCandidate(connection, candidateTableName, selectedPrimaryKeyAlias, out var aliases, out var validRows);
        var rows = new List<IReadOnlyList<string?>>();
        var businessProjection = string.Join(", ", aliases.Select(SergioSql.Quote));
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {businessProjection} FROM {SergioSql.Quote(candidateTableName)} WHERE {SergioSql.Quote(SergioSchemaNames.RejectionReason)} IS NULL LIMIT 20";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetString(i);
                rows.Add(row);
            }
        }
        var total = Scalar(connection, $"SELECT COUNT(*) FROM {SergioSql.Quote(candidateTableName)}");
        var metadata = ReadMetadata(connection);
        return new SergioPreview(aliases, rows, total, selectedPrimaryKeyAlias, Path.GetFileName(sourceFilePath),
            TableExists(connection, OfficialTableName), metadata, validRows);
    }

    public void Promote(string candidateTableName, string selectedPrimaryKeyAlias, string sourceFilePath, string? schemaVersion = null)
    {
        using var operation = analyzer.AcquirePromotionLock();
        using var connection = Open(analyzer.DatabasePath);
        EnsureOwnedCandidate(connection, candidateTableName, selectedPrimaryKeyAlias, out _, out _);
        using var ownership = analyzer.PrepareLastImportTransition(candidateTableName, selectedPrimaryKeyAlias);
        using var transaction = connection.BeginTransaction();
        try
        {
            EnsureOwnedCandidate(connection, candidateTableName, selectedPrimaryKeyAlias, out var aliases, out var validRows, transaction);
            var publication = $"sergio_publication_{Guid.NewGuid():N}";
            var columns = string.Join(", ", aliases.Select(alias => $"{SergioSql.Quote(alias)} VARCHAR"));
            Execute(connection, transaction, $"CREATE TABLE {SergioSql.Quote(publication)} ({columns}, PRIMARY KEY ({SergioSql.Quote(selectedPrimaryKeyAlias)}))");
            var businessProjection = string.Join(", ", aliases.Select(SergioSql.Quote));
            Execute(connection, transaction, $"INSERT INTO {SergioSql.Quote(publication)} ({businessProjection}) SELECT {businessProjection} FROM {SergioSql.Quote(candidateTableName)} WHERE {SergioSql.Quote(SergioSchemaNames.RejectionReason)} IS NULL");
            if (Scalar(connection, $"SELECT COUNT(*) FROM {SergioSql.Quote(publication)}", transaction) != validRows)
                throw new InvalidOperationException("The Sergio publication row count does not match the validated candidate.");
            var oldOfficial = RenameAside(connection, transaction, OfficialTableName, "official");
            Execute(connection, transaction, $"ALTER TABLE {SergioSql.Quote(publication)} RENAME TO {SergioSql.Quote(OfficialTableName)}");
            var oldImport = RenameAside(connection, transaction, LastImportTableName, "import");
            Execute(connection, transaction, $"ALTER TABLE {SergioSql.Quote(candidateTableName)} RENAME TO {SergioSql.Quote(LastImportTableName)}");
            afterOfficialReplacement?.Invoke();
            Execute(connection, transaction, $"CREATE TABLE IF NOT EXISTS {SergioSql.Quote(MetadataTableName)} (source_file_name VARCHAR, loaded_at_utc TIMESTAMP, row_count BIGINT, selected_primary_key_alias VARCHAR, schema_version VARCHAR)");
            Execute(connection, transaction, $"DELETE FROM {SergioSql.Quote(MetadataTableName)}");
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {SergioSql.Quote(MetadataTableName)} VALUES (?, ?, ?, ?, ?)";
            insert.Parameters.Add(new DuckDBParameter { Value = Path.GetFileName(sourceFilePath) });
            insert.Parameters.Add(new DuckDBParameter { Value = DateTime.UtcNow });
            insert.Parameters.Add(new DuckDBParameter { Value = validRows });
            insert.Parameters.Add(new DuckDBParameter { Value = selectedPrimaryKeyAlias });
            insert.Parameters.Add(new DuckDBParameter { Value = (object?)schemaVersion ?? DBNull.Value });
            insert.ExecuteNonQuery();
            if (oldOfficial is not null) Execute(connection, transaction, $"DROP TABLE {SergioSql.Quote(oldOfficial)}");
            if (oldImport is not null) Execute(connection, transaction, $"DROP TABLE {SergioSql.Quote(oldImport)}");
            transaction.Commit();
            // The transaction is now authoritative. This is intentionally the only post-commit
            // ownership operation and cannot fail or allocate; Dispose must not undo it.
            ownership.Complete();
        }
        catch { try { transaction.Rollback(); } catch { } throw; }
    }

    private void EnsureOwnedCandidate(DuckDBConnection connection, string candidate, string key, out IReadOnlyList<string> aliases, out long validRows, DuckDBTransaction? transaction = null)
    {
        if (!analyzer.OwnsCandidate(candidate, key) || !TableExists(connection, candidate, transaction))
            throw new InvalidOperationException("The Sergio candidate is unknown, not owned by this analyzer, or was staged with a different primary key.");
        var descriptor = analyzer.GetOwnedCandidateForKey(candidate, key);
        aliases = ReadColumns(connection, candidate, transaction);
        var expectedColumns = descriptor.AcceptedAliases.Contains(descriptor.ReasonColumn, StringComparer.Ordinal)
            ? descriptor.AcceptedAliases
            : descriptor.AcceptedAliases.Append(descriptor.ReasonColumn).ToArray();
        if (!aliases.SequenceEqual(expectedColumns, StringComparer.Ordinal))
            throw new InvalidOperationException("The Sergio candidate schema no longer matches its analyzer-owned descriptor.");
        if (!aliases.Contains(key, StringComparer.Ordinal)) throw new InvalidOperationException("The selected Sergio primary key is not in the candidate schema.");
        var technical = aliases.FirstOrDefault(alias => string.Equals(alias, SergioSchemaNames.RejectionReason, StringComparison.Ordinal));
        if (technical is null) throw new InvalidOperationException("The Sergio candidate has no rejection classification column.");
        validRows = Scalar(connection, $"SELECT COUNT(*) FROM {SergioSql.Quote(candidate)} WHERE {SergioSql.Quote(technical)} IS NULL", transaction);
        if (validRows == 0) throw new InvalidOperationException("The Sergio candidate contains no valid rows and cannot be promoted.");
        aliases = aliases.Where(alias => !string.Equals(alias, SergioSchemaNames.RejectionReason, StringComparison.Ordinal)).ToArray();
    }

    private static string? RenameAside(DuckDBConnection connection, DuckDBTransaction transaction, string table, string kind)
    {
        if (!TableExists(connection, table, transaction)) return null;
        var backup = $"sergio_previous_{kind}_{Guid.NewGuid():N}";
        Execute(connection, transaction, $"ALTER TABLE {SergioSql.Quote(table)} RENAME TO {SergioSql.Quote(backup)}");
        return backup;
    }

    private static IReadOnlyList<string> ReadColumns(DuckDBConnection c, string table, DuckDBTransaction? tx)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name = ? ORDER BY ordinal_position";
        cmd.Parameters.Add(new DuckDBParameter { Value = table }); using var r = cmd.ExecuteReader();
        var result = new List<string>(); while (r.Read()) result.Add(r.GetString(0)); return result;
    }
    private static SergioLoadMetadata? ReadMetadata(DuckDBConnection c)
    {
        if (!TableExists(c, MetadataTableName)) return null;
        using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT source_file_name, loaded_at_utc, row_count, selected_primary_key_alias, schema_version FROM {SergioSql.Quote(MetadataTableName)} LIMIT 1";
        using var r = cmd.ExecuteReader(); if (!r.Read()) return null;
        return new SergioLoadMetadata(r.GetString(0), r.GetDateTime(1), r.GetInt64(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4));
    }
    private static DuckDBConnection Open(string path) { var c = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString); c.Open(); return c; }
    private static long Scalar(DuckDBConnection c, string sql, DuckDBTransaction? tx = null) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar()); }
    private static bool TableExists(DuckDBConnection c, string table, DuckDBTransaction? tx = null) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = ?"; cmd.Parameters.Add(new DuckDBParameter { Value = table }); return Scalar(cmd) != 0; }
    private static long Scalar(DuckDBCommand cmd) => Convert.ToInt64(cmd.ExecuteScalar());
    private static void Execute(DuckDBConnection c, DuckDBTransaction tx, string sql) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
}
