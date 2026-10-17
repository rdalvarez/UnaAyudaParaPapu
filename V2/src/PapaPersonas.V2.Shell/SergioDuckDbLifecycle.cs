using System.Globalization;
using DuckDB.NET.Data;

namespace PapaPersonas.V2.Shell;

public enum SergioDuckDbLifecycleKind
{
    ReadyWithoutOfficialLoad,
    ReadyWithOfficialLoad,
    Inconsistent,
    Unavailable
}

public sealed record SergioDuckDbLifecycleStatus(
    SergioDuckDbLifecycleKind Kind,
    string TechnicalReason,
    string DatabasePath,
    long MetadataRowCount,
    long? OfficialRowCount = null,
    long? LastImportRowCount = null,
    long? LastImportValidRowCount = null,
    string? SourceFileName = null,
    bool HasTransientSergioTablesWarning = false,
    string? Warning = null);

public enum SergioDuckDbResetFailureKind
{
    PreviousDatabasePreserved,
    PreviousDataIrreversiblyDeleted,
    NoPreviousDatabase
}

public sealed class SergioDuckDbResetException : IOException
{
    public SergioDuckDbResetException(SergioDuckDbResetFailureKind kind, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public SergioDuckDbResetFailureKind Kind { get; }
}

public sealed record SergioDuckDbResetFailurePresentation(string ActivityText, string AlertText);

public static class SergioDuckDbResetFailurePresenter
{
    public static SergioDuckDbResetFailurePresentation Present(SergioDuckDbResetFailureKind? kind) => kind switch
    {
        SergioDuckDbResetFailureKind.PreviousDataIrreversiblyDeleted => new(
            "La base anterior se eliminó, pero no se pudo crear la base nueva. No hay backup.",
            "La base anterior se eliminó de forma permanente, pero no se pudo crear la base nueva. No existe un backup y los datos eliminados no se pueden recuperar."),
        SergioDuckDbResetFailureKind.NoPreviousDatabase => new(
            "No había una base anterior y no se pudo crear la base nueva. No se eliminó ningún dato previo.",
            "No había una base anterior, pero no se pudo crear la base nueva. No se eliminó ningún dato previo."),
        SergioDuckDbResetFailureKind.PreviousDatabasePreserved => new(
            "No se pudo reiniciar la base local. La base anterior no se eliminó.",
            "No se pudo eliminar la base local. La base anterior se conservó. Verifique permisos de archivo y que no haya procesos usando DuckDB."),
        _ => new(
            "No se pudo reiniciar la base local. No se pudo determinar si la base anterior se conservó.",
            "No se pudo reiniciar la base local. No se pudo determinar si la base anterior se conservó.")
    };
}

public sealed record SergioDuckDbResetResult(
    string DatabasePath,
    bool PreviousDatabaseDeleted,
    SergioDuckDbLifecycleStatus Status);

public sealed class SergioDuckDbLifecycleService
{
    private readonly SergioStagingAnalyzer analyzer;
    private readonly Action? beforeFreshInitializationForReset;
    private readonly Action<string>? beforeDeleteForReset;

    public SergioDuckDbLifecycleService(
        SergioStagingAnalyzer analyzer,
        Action? beforeFreshInitializationForReset = null,
        Action<string>? beforeDeleteForReset = null)
    {
        this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        this.beforeFreshInitializationForReset = beforeFreshInitializationForReset;
        this.beforeDeleteForReset = beforeDeleteForReset;
    }

    public string DatabasePath => analyzer.DatabasePath;

    public void Initialize()
    {
        using var operation = analyzer.AcquirePromotionLock();
        InitializeCore(DatabasePath);
    }

    public SergioDuckDbLifecycleStatus Inspect()
    {
        using var operation = analyzer.AcquirePromotionLock();
        return InspectCore(DatabasePath);
    }

    public SergioDuckDbResetResult Reset(bool confirmedByCaller)
    {
        if (!confirmedByCaller)
            throw new InvalidOperationException("DuckDB reset requires explicit caller confirmation.");

        using var operation = analyzer.AcquirePromotionLock();
        var databasePath = DatabasePath;
        EnsureDirectory(databasePath);

        var hadPreviousDatabase = File.Exists(databasePath) || File.Exists(WalPath(databasePath));
        try
        {
            DeleteConfiguredDatabase(databasePath);
        }
        catch (Exception deleteException)
        {
            if (File.Exists(databasePath) || !hadPreviousDatabase)
            {
                throw new SergioDuckDbResetException(
                    SergioDuckDbResetFailureKind.PreviousDatabasePreserved,
                    "No se pudo eliminar la base local. La base anterior se conservó.",
                    deleteException);
            }

            throw new SergioDuckDbResetException(
                SergioDuckDbResetFailureKind.PreviousDataIrreversiblyDeleted,
                "La base anterior se eliminó, pero no se pudo quitar un archivo acompañante. No existe un backup y los datos eliminados no se pueden recuperar.",
                deleteException);
        }

        try
        {
            beforeFreshInitializationForReset?.Invoke();
            InitializeCore(databasePath);
            var status = InspectCore(databasePath);
            return new SergioDuckDbResetResult(databasePath, hadPreviousDatabase, status);
        }
        catch (Exception initializationException)
        {
            if (!hadPreviousDatabase)
            {
                throw new SergioDuckDbResetException(
                    SergioDuckDbResetFailureKind.NoPreviousDatabase,
                    "No había una base anterior, pero no se pudo crear la base nueva. No se eliminó ningún dato previo.",
                    initializationException);
            }

            throw new SergioDuckDbResetException(
                SergioDuckDbResetFailureKind.PreviousDataIrreversiblyDeleted,
                "La base anterior se eliminó de forma permanente, pero no se pudo crear la base nueva. No existe un backup y los datos eliminados no se pueden recuperar.",
                initializationException);
        }
    }

    private void DeleteConfiguredDatabase(string databasePath)
    {
        beforeDeleteForReset?.Invoke(databasePath);
        DeleteExactFile(databasePath);
        DeleteExactFile(WalPath(databasePath));

        // La creación y restauración de backups fechados quedaron deshabilitadas por pedido explícito.
        // El reinicio confirmado elimina de forma permanente la base configurada y su archivo .wal.
        // No se usa AllocateBackupPath ni File.Move hacia un backup.
    }

    private static string WalPath(string databasePath) => databasePath + ".wal";

    private static void DeleteExactFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return;

        try
        {
            File.Delete(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"No se pudo eliminar '{Path.GetFileName(fullPath)}'.", exception);
        }
    }

    private static void InitializeCore(string databasePath)
    {
        EnsureDirectory(databasePath);
        using var connection = Open(databasePath);
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, SergioLoadMetadataSchema.CreateTableSql(SergioSql.Quote(SergioLoadPromoter.MetadataTableName)));
        ValidateMetadataSchema(connection, transaction);
        transaction.Commit();
    }

    private static SergioDuckDbLifecycleStatus InspectCore(string databasePath)
    {
        try
        {
            using var connection = Open(databasePath);
            var tables = ReadTableNames(connection);

            var hasMetadata = tables.Contains(SergioLoadPromoter.MetadataTableName, StringComparer.Ordinal);
            var hasCurrent = tables.Contains(SergioLoadPromoter.OfficialTableName, StringComparer.Ordinal);
            var hasLastImport = tables.Contains(SergioLoadPromoter.LastImportTableName, StringComparer.Ordinal);
            var transientWarning = BuildTransientTablesWarning(tables);

            if (!hasMetadata)
            {
                return Inconsistent(
                    "MISSING_METADATA_TABLE",
                    databasePath,
                    metadataRowCount: 0,
                    warning: transientWarning);
            }

            if (!MetadataSchemaMatches(connection))
            {
                return Inconsistent(
                    "METADATA_SCHEMA_MISMATCH",
                    databasePath,
                    metadataRowCount: SafeCountRows(connection, SergioLoadPromoter.MetadataTableName),
                    warning: transientWarning);
            }

            var metadataRows = CountRows(connection, SergioLoadPromoter.MetadataTableName);

            if (!hasCurrent && !hasLastImport)
            {
                if (metadataRows != 0)
                {
                    return Inconsistent(
                        "METADATA_ROWS_WITHOUT_DYNAMIC_TABLES",
                        databasePath,
                        metadataRows,
                        warning: transientWarning);
                }

                return Ready(
                    SergioDuckDbLifecycleKind.ReadyWithoutOfficialLoad,
                    "NO_OFFICIAL_LOAD",
                    databasePath,
                    metadataRows,
                    warning: transientWarning);
            }

            if (!hasCurrent || !hasLastImport)
            {
                return Inconsistent(
                    "PARTIAL_DYNAMIC_TABLES",
                    databasePath,
                    metadataRows,
                    warning: transientWarning);
            }

            if (metadataRows != 1)
            {
                return Inconsistent(
                    "METADATA_ROW_COUNT_INVALID",
                    databasePath,
                    metadataRows,
                    warning: transientWarning);
            }

            var metadata = ReadMetadataRow(connection);
            if (metadata is null)
            {
                return Inconsistent(
                    "METADATA_ROW_UNREADABLE",
                    databasePath,
                    metadataRows,
                    warning: transientWarning);
            }

            if (string.IsNullOrWhiteSpace(metadata.SelectedPrimaryKeyAlias))
            {
                return Inconsistent(
                    "SELECTED_PK_ALIAS_MISSING",
                    databasePath,
                    metadataRows,
                    warning: transientWarning);
            }

            if (!ColumnExists(connection, SergioLoadPromoter.OfficialTableName, metadata.SelectedPrimaryKeyAlias))
            {
                return Inconsistent(
                    "SELECTED_PK_ALIAS_NOT_IN_CURRENT",
                    databasePath,
                    metadataRows,
                    warning: transientWarning);
            }

            if (!ColumnExists(connection, SergioLoadPromoter.LastImportTableName, metadata.SelectedPrimaryKeyAlias))
            {
                return Inconsistent(
                    "SELECTED_PK_ALIAS_NOT_IN_LAST_IMPORT",
                    databasePath,
                    metadataRows,
                    warning: transientWarning);
            }

            if (!ColumnExists(connection, SergioLoadPromoter.LastImportTableName, SergioSchemaNames.RejectionReason))
            {
                return Inconsistent(
                    "LAST_IMPORT_REASON_COLUMN_MISSING",
                    databasePath,
                    metadataRows,
                    warning: transientWarning);
            }

            var officialRows = CountRows(connection, SergioLoadPromoter.OfficialTableName);
            var lastImportRows = CountRows(connection, SergioLoadPromoter.LastImportTableName);
            var lastImportValidRows = CountRowsWhereNull(connection, SergioLoadPromoter.LastImportTableName, SergioSchemaNames.RejectionReason);

            if (metadata.RowCount != officialRows)
            {
                return Inconsistent(
                    "METADATA_ROW_COUNT_MISMATCH_CURRENT",
                    databasePath,
                    metadataRows,
                    officialRows,
                    lastImportRows,
                    lastImportValidRows,
                    warning: transientWarning);
            }

            if (metadata.RowCount != lastImportValidRows)
            {
                return Inconsistent(
                    "METADATA_ROW_COUNT_MISMATCH_LAST_IMPORT_VALID",
                    databasePath,
                    metadataRows,
                    officialRows,
                    lastImportRows,
                    lastImportValidRows,
                    warning: transientWarning);
            }

            return Ready(
                SergioDuckDbLifecycleKind.ReadyWithOfficialLoad,
                "OFFICIAL_LOAD_READY",
                databasePath,
                metadataRows,
                officialRows,
                lastImportRows,
                lastImportValidRows,
                Path.GetFileName(metadata.SourceFileName),
                transientWarning);
        }
        catch (Exception exception)
        {
            return new SergioDuckDbLifecycleStatus(
                SergioDuckDbLifecycleKind.Unavailable,
                $"DUCKDB_UNAVAILABLE_{exception.GetType().Name}",
                databasePath,
                MetadataRowCount: 0,
                Warning: "DuckDB could not be opened or inspected safely.");
        }
    }

    private static SergioDuckDbLifecycleStatus Ready(
        SergioDuckDbLifecycleKind kind,
        string reason,
        string path,
        long metadataRows,
        long? officialRows = null,
        long? lastImportRows = null,
        long? lastImportValidRows = null,
        string? sourceFileName = null,
        string? warning = null)
    {
        return new SergioDuckDbLifecycleStatus(
            kind,
            reason,
            path,
            metadataRows,
            officialRows,
            lastImportRows,
            lastImportValidRows,
            sourceFileName,
            HasTransientSergioTablesWarning: warning is not null,
            warning);
    }

    private static SergioDuckDbLifecycleStatus Inconsistent(
        string reason,
        string path,
        long metadataRowCount,
        long? officialRows = null,
        long? lastImportRows = null,
        long? lastImportValidRows = null,
        string? warning = null)
    {
        return new SergioDuckDbLifecycleStatus(
            SergioDuckDbLifecycleKind.Inconsistent,
            reason,
            path,
            metadataRowCount,
            officialRows,
            lastImportRows,
            lastImportValidRows,
            HasTransientSergioTablesWarning: warning is not null,
            Warning: warning);
    }

    private static void ValidateMetadataSchema(DuckDBConnection connection, DuckDBTransaction? transaction)
    {
        if (!MetadataSchemaMatches(connection, transaction))
            throw new InvalidDataException("The Sergio metadata schema does not match the expected lifecycle contract.");
    }

    private static bool MetadataSchemaMatches(DuckDBConnection connection, DuckDBTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = ? ORDER BY ordinal_position";
        command.Parameters.Add(new DuckDBParameter { Value = SergioLoadPromoter.MetadataTableName });
        using var reader = command.ExecuteReader();
        var actual = new List<(string Name, string Type)>();
        while (reader.Read())
            actual.Add((reader.GetString(0), reader.GetString(1).ToUpperInvariant()));

        if (actual.Count != SergioLoadMetadataSchema.Columns.Count) return false;
        for (var index = 0; index < actual.Count; index++)
        {
            var expected = SergioLoadMetadataSchema.Columns[index];
            if (!string.Equals(actual[index].Name, expected.Name, StringComparison.Ordinal)) return false;
            if (!string.Equals(actual[index].Type, expected.Type, StringComparison.Ordinal)) return false;
        }

        return true;
    }

    private static SergioMetadataRow? ReadMetadataRow(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SergioLoadMetadataSchema.SourceFileNameColumn}, {SergioLoadMetadataSchema.RowCountColumn}, {SergioLoadMetadataSchema.SelectedPrimaryKeyAliasColumn} " +
            $"FROM {SergioSql.Quote(SergioLoadPromoter.MetadataTableName)} LIMIT 1";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new SergioMetadataRow(
            SourceFileName: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            RowCount: reader.GetInt64(1),
            SelectedPrimaryKeyAlias: reader.IsDBNull(2) ? string.Empty : reader.GetString(2));
    }

    private static IReadOnlyList<string> ReadTableNames(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    private static string? BuildTransientTablesWarning(IReadOnlyList<string> tables)
    {
        var staleStaging = tables.Any(IsReservedCandidateName);
        var staleAuxiliary = tables.Any(name =>
            name.StartsWith("sergio_previous_", StringComparison.Ordinal) ||
            name.StartsWith("sergio_publication_", StringComparison.Ordinal));

        if (!staleStaging && !staleAuxiliary) return null;
        return "Transient Sergio tables were detected and may come from an interrupted operation.";
    }

    private static bool IsReservedCandidateName(string name) =>
        name.StartsWith("sergio_staging_", StringComparison.Ordinal) &&
        name.Length == 47 &&
        name[15..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool ColumnExists(DuckDBConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = ? AND column_name = ?";
        command.Parameters.Add(new DuckDBParameter { Value = tableName });
        command.Parameters.Add(new DuckDBParameter { Value = columnName });
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static long CountRows(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {SergioSql.Quote(tableName)}";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long CountRowsWhereNull(DuckDBConnection connection, string tableName, string nullableColumn)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM {SergioSql.Quote(tableName)} WHERE {SergioSql.Quote(nullableColumn)} IS NULL";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long SafeCountRows(DuckDBConnection connection, string tableName)
    {
        try
        {
            return CountRows(connection, tableName);
        }
        catch
        {
            return 0;
        }
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The DuckDB path has no parent directory.");
        Directory.CreateDirectory(directory);
    }

    private static DuckDBConnection Open(string path)
    {
        var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(DuckDBConnection connection, DuckDBTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record SergioMetadataRow(string SourceFileName, long RowCount, string SelectedPrimaryKeyAlias);
}
