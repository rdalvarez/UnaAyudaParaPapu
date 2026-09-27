using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class SergioDuckDbLifecycleTests
{
    [Fact]
    public void InitializeCreatesMetadataTableAndReadyWithoutOfficialLoadState()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(workspace.DatabasePath);

        lifecycle.Initialize();
        var status = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.ReadyWithoutOfficialLoad, status.Kind);
        Assert.Equal("NO_OFFICIAL_LOAD", status.TechnicalReason);
        Assert.Equal(0, status.MetadataRowCount);
        using var verify = Open(workspace.DatabasePath);
        Assert.True(TableExists(verify, SergioLoadPromoter.MetadataTableName));
        Assert.False(TableExists(verify, SergioLoadPromoter.OfficialTableName));
        Assert.False(TableExists(verify, SergioLoadPromoter.LastImportTableName));
    }

    [Fact]
    public void InitializeIsIdempotent()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(workspace.DatabasePath);

        lifecycle.Initialize();
        lifecycle.Initialize();
        var status = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.ReadyWithoutOfficialLoad, status.Kind);
        Assert.Equal("NO_OFFICIAL_LOAD", status.TechnicalReason);
        Assert.Equal(0, status.MetadataRowCount);
    }

    [Fact]
    public void InspectReportsReadyWithOfficialLoadAndSanitizedSourceFileName()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(workspace.DatabasePath);
        lifecycle.Initialize();

        using (var connection = Open(workspace.DatabasePath))
        {
            BuildConsistentDynamicState(connection, selectedPrimaryKeyAlias: "CUIL", metadataRowCount: 2,
                metadataSourcePath: @"C:\very\secret\source\sergio.xlsx");
        }

        var status = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.ReadyWithOfficialLoad, status.Kind);
        Assert.Equal("OFFICIAL_LOAD_READY", status.TechnicalReason);
        Assert.Equal(1, status.MetadataRowCount);
        Assert.Equal(2, status.OfficialRowCount);
        Assert.Equal(3, status.LastImportRowCount);
        Assert.Equal(2, status.LastImportValidRowCount);
        Assert.Equal("sergio.xlsx", status.SourceFileName);
    }

    [Fact]
    public void InspectReportsPartialDynamicStateAsInconsistent()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(workspace.DatabasePath);
        lifecycle.Initialize();
        using var connection = Open(workspace.DatabasePath);
        Execute(connection, "CREATE TABLE sergio_current(CUIL VARCHAR)");

        var status = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.Inconsistent, status.Kind);
        Assert.Equal("PARTIAL_DYNAMIC_TABLES", status.TechnicalReason);
    }

    [Fact]
    public void InspectReportsMetadataCountMismatchAgainstCurrent()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(workspace.DatabasePath);
        lifecycle.Initialize();
        using (var connection = Open(workspace.DatabasePath))
        {
            BuildConsistentDynamicState(connection, selectedPrimaryKeyAlias: "CUIL", metadataRowCount: 99,
                metadataSourcePath: "sergio.xlsx");
        }

        var status = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.Inconsistent, status.Kind);
        Assert.Equal("METADATA_ROW_COUNT_MISMATCH_CURRENT", status.TechnicalReason);
    }

    [Fact]
    public void InspectReportsMetadataCountMismatchAgainstLastImportValidRows()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(workspace.DatabasePath);
        lifecycle.Initialize();
        using (var connection = Open(workspace.DatabasePath))
        {
            BuildConsistentDynamicState(connection, selectedPrimaryKeyAlias: "CUIL", metadataRowCount: 2,
                metadataSourcePath: "sergio.xlsx");
            Execute(connection, "UPDATE sergio_last_import SET __pp_motivo_rechazo = 'CLAVE_DUPLICADA' WHERE CUIL = '20-2'");
        }

        var status = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.Inconsistent, status.Kind);
        Assert.Equal("METADATA_ROW_COUNT_MISMATCH_LAST_IMPORT_VALID", status.TechnicalReason);
    }

    [Fact]
    public void InspectRequiresSelectedPrimaryKeyAliasAndReasonColumn()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(workspace.DatabasePath);
        lifecycle.Initialize();
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "CREATE TABLE sergio_current(CUIL VARCHAR, Nombre VARCHAR)");
            Execute(connection, "CREATE TABLE sergio_last_import(CUIL VARCHAR, Nombre VARCHAR)");
            Execute(connection, "DELETE FROM sergio_load_metadata");
            Execute(connection, "INSERT INTO sergio_load_metadata VALUES ('sergio.xlsx', NOW(), 0, '', NULL)");
        }

        var missingPkAlias = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.Inconsistent, missingPkAlias.Kind);
        Assert.Equal("SELECTED_PK_ALIAS_MISSING", missingPkAlias.TechnicalReason);

        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "DELETE FROM sergio_load_metadata");
            Execute(connection, "INSERT INTO sergio_load_metadata VALUES ('sergio.xlsx', NOW(), 0, 'CUIL', NULL)");
        }

        var missingReason = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.Inconsistent, missingReason.Kind);
        Assert.Equal("LAST_IMPORT_REASON_COLUMN_MISSING", missingReason.TechnicalReason);
    }

    [Fact]
    public void InspectWarnsWhenTransientSergioTablesArePresent()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(workspace.DatabasePath);
        lifecycle.Initialize();
        using var connection = Open(workspace.DatabasePath);
        Execute(connection, "CREATE TABLE sergio_staging_0123456789abcdef0123456789abcdef(value VARCHAR)");

        var status = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.ReadyWithoutOfficialLoad, status.Kind);
        Assert.True(status.HasTransientSergioTablesWarning);
        Assert.NotNull(status.Warning);
    }

    [Fact]
    public void InspectReturnsUnavailableForCorruptDatabaseFile()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.DatabasePath)!);
        File.WriteAllText(workspace.DatabasePath, "this is not a duckdb file");
        var lifecycle = NewLifecycle(workspace.DatabasePath);

        var status = lifecycle.Inspect();

        Assert.Equal(SergioDuckDbLifecycleKind.Unavailable, status.Kind);
        Assert.StartsWith("DUCKDB_UNAVAILABLE_", status.TechnicalReason, StringComparison.Ordinal);
        Assert.Equal("DuckDB could not be opened or inspected safely.", status.Warning);
    }

    [Fact]
    public void ResetRequiresExplicitConfirmation()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(workspace.DatabasePath);
        lifecycle.Initialize();

        Assert.Throws<InvalidOperationException>(() => lifecycle.Reset(confirmedByCaller: false));
    }

    [Fact]
    public void ResetBacksUpCurrentDatabaseAndInitializesFreshOne()
    {
        using var workspace = new TestWorkspace();
        var now = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var lifecycle = NewLifecycle(workspace.DatabasePath, () => now);
        lifecycle.Initialize();
        using (var connection = Open(workspace.DatabasePath))
        {
            BuildConsistentDynamicState(connection, selectedPrimaryKeyAlias: "CUIL", metadataRowCount: 2,
                metadataSourcePath: "old.xlsx");
        }

        var result = lifecycle.Reset(confirmedByCaller: true);

        Assert.Equal(workspace.DatabasePath, result.DatabasePath);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));
        Assert.Equal(SergioDuckDbLifecycleKind.ReadyWithoutOfficialLoad, result.Status.Kind);
        Assert.Equal("NO_OFFICIAL_LOAD", result.Status.TechnicalReason);
        using var verify = Open(workspace.DatabasePath);
        Assert.True(TableExists(verify, SergioLoadPromoter.MetadataTableName));
        Assert.False(TableExists(verify, SergioLoadPromoter.OfficialTableName));
        Assert.False(TableExists(verify, SergioLoadPromoter.LastImportTableName));
    }

    [Fact]
    public void ResetBackupPathIsCollisionSafe()
    {
        using var workspace = new TestWorkspace();
        var now = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var lifecycle = NewLifecycle(workspace.DatabasePath, () => now);
        lifecycle.Initialize();
        var stamp = now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var baseName = Path.GetFileNameWithoutExtension(workspace.DatabasePath);
        var extension = Path.GetExtension(workspace.DatabasePath);
        var collision = Path.Combine(Path.GetDirectoryName(workspace.DatabasePath)!,
            $"{baseName}_{stamp}.bak{extension}");
        File.WriteAllText(collision, "existing backup");

        var result = lifecycle.Reset(confirmedByCaller: true);

        Assert.Equal(Path.Combine(Path.GetDirectoryName(workspace.DatabasePath)!, $"{baseName}_{stamp}_2.bak{extension}"), result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));
        Assert.True(File.Exists(collision));
    }

    [Fact]
    public void ResetRestoresPreviousDatabaseWhenFreshInitializationFails()
    {
        using var workspace = new TestWorkspace();
        var lifecycle = NewLifecycle(
            workspace.DatabasePath,
            beforeFreshInitializationForReset: () => throw new IOException("forced reset failure"));
        lifecycle.Initialize();
        using (var connection = Open(workspace.DatabasePath))
        {
            BuildConsistentDynamicState(connection, selectedPrimaryKeyAlias: "CUIL", metadataRowCount: 2,
                metadataSourcePath: "before-reset.xlsx");
        }

        var exception = Assert.Throws<IOException>(() => lifecycle.Reset(confirmedByCaller: true));
        Assert.Contains("previous database was restored", exception.Message, StringComparison.OrdinalIgnoreCase);

        var status = lifecycle.Inspect();
        Assert.Equal(SergioDuckDbLifecycleKind.ReadyWithOfficialLoad, status.Kind);
        Assert.Equal("before-reset.xlsx", status.SourceFileName);
    }

    private static SergioDuckDbLifecycleService NewLifecycle(
        string databasePath,
        Func<DateTime>? clock = null,
        Action? beforeFreshInitializationForReset = null)
    {
        var analyzer = new SergioStagingAnalyzer(new DuckDbPathPolicy(databasePath));
        return new SergioDuckDbLifecycleService(analyzer, clock, beforeFreshInitializationForReset);
    }

    private static void BuildConsistentDynamicState(
        DuckDBConnection connection,
        string selectedPrimaryKeyAlias,
        long metadataRowCount,
        string metadataSourcePath)
    {
        Execute(connection,
            "CREATE TABLE sergio_current(CUIL VARCHAR, Nombre VARCHAR, PRIMARY KEY (CUIL));" +
            "INSERT INTO sergio_current VALUES ('20-1', 'Ana'), ('20-2', 'Beto');" +
            "CREATE TABLE sergio_last_import(CUIL VARCHAR, Nombre VARCHAR, __pp_motivo_rechazo VARCHAR);" +
            "INSERT INTO sergio_last_import VALUES ('20-1', 'Ana', NULL), ('20-2', 'Beto', NULL), ('20-1', 'Ana duplicada', 'CLAVE_DUPLICADA');" +
            "DELETE FROM sergio_load_metadata;");

        using var metadata = connection.CreateCommand();
        metadata.CommandText = "INSERT INTO sergio_load_metadata VALUES (?, NOW(), ?, ?, NULL)";
        metadata.Parameters.Add(new DuckDBParameter { Value = metadataSourcePath });
        metadata.Parameters.Add(new DuckDBParameter { Value = metadataRowCount });
        metadata.Parameters.Add(new DuckDBParameter { Value = selectedPrimaryKeyAlias });
        metadata.ExecuteNonQuery();
    }

    private static DuckDBConnection Open(string path)
    {
        var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static bool TableExists(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = ?";
        command.Parameters.Add(new DuckDBParameter { Value = tableName });
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static void Execute(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(directory, "test.duckdb");

        public TestWorkspace() => Directory.CreateDirectory(directory);

        public void Dispose()
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}
