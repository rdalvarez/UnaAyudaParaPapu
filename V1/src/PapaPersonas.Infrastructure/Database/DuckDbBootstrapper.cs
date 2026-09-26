using DuckDB.NET.Data;
using PapaPersonas.Core.Database;
using PapaPersonas.Core.Import;
using System.Collections.Concurrent;

namespace PapaPersonas.Infrastructure.Database;

public sealed class DuckDbBootstrapper : IDatabaseBootstrapper
{
    private static readonly ConcurrentDictionary<string, object> DatabaseInitializationLocks = new(StringComparer.OrdinalIgnoreCase);

    private const int BaselineSchemaVersion = 1;
    private const string EnsureSchemaMetadataSql = """
        CREATE TABLE IF NOT EXISTS schema_metadata (
            schema_version INTEGER PRIMARY KEY,
            initialized_utc TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        """;

    /// <summary>Inicializa la base, aplica migraciones pendientes y valida el contrato de esquema soportado.</summary>
    public DatabaseBootstrapResult Initialize(string databaseFilePath)
    {
        if (string.IsNullOrWhiteSpace(databaseFilePath))
        {
            return DatabaseBootstrapResult.Failure(string.Empty, "La ruta de la base de datos no puede estar vacía.");
        }

        var fullPath = Path.GetFullPath(databaseFilePath);
        var initializationLock = DatabaseInitializationLocks.GetOrAdd(fullPath, _ => new object());

        lock (initializationLock)
        {
            try
            {
                var parentDirectory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(parentDirectory))
                {
                    return DatabaseBootstrapResult.Failure(fullPath, "La ruta de la base de datos debe incluir una carpeta contenedora.");
                }

                Directory.CreateDirectory(parentDirectory);

                using var connection = new DuckDBConnection(BuildConnectionString(fullPath));
                connection.Open();

                var schemaMetadataExisted = TableExists(connection, "schema_metadata");
                if (!schemaMetadataExisted && HasAnyApplicationTable(connection))
                {
                    return DatabaseBootstrapResult.Failure(fullPath, "Las tablas existentes requieren la tabla de metadatos de inicio 'schema_metadata'.");
                }

                EnsureSchemaMetadataTable(connection);
                if (!ValidateSchemaMetadataTable(connection, out var metadataMessage))
                {
                    return DatabaseBootstrapResult.Failure(fullPath, metadataMessage);
                }

                ApplyPendingMigrations(connection);

                var validationResult = SupportedSchemaContract.Validate(connection);
                if (!validationResult.IsSuccess)
                {
                    return DatabaseBootstrapResult.Failure(fullPath, validationResult.Message);
                }

                return DatabaseBootstrapResult.Success(fullPath);
            }
            catch (UnsupportedSchemaVersionException ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
            {
                return DatabaseBootstrapResult.Failure(
                    fullPath,
                    UserFacingExceptionMessage.WithTechnicalDetail("No se pudo inicializar la base de datos.", ex),
                    DatabaseBootstrapFailureCode.UnsupportedSchemaVersion);
            }
            catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
            {
                return DatabaseBootstrapResult.Failure(
                    fullPath,
                    UserFacingExceptionMessage.WithTechnicalDetail("No se pudo inicializar la base de datos.", ex));
            }
        }
    }

    private static string BuildConnectionString(string databaseFilePath)
    {
        var builder = new DuckDBConnectionStringBuilder
        {
            DataSource = databaseFilePath
        };

        return builder.ConnectionString;
    }

    private static void EnsureSchemaMetadataTable(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = EnsureSchemaMetadataSql;
        command.ExecuteNonQuery();
    }

    // Comprueba que la tabla de metadatos tenga columnas, tipos y clave primaria exactos.
    private static bool ValidateSchemaMetadataTable(DuckDBConnection connection, out string message)
    {
        if (!TableExists(connection, "schema_metadata"))
        {
            message = "Falta la tabla obligatoria 'schema_metadata'.";
            return false;
        }

        var metadataColumns = ReadColumns(connection, "schema_metadata");
        if (metadataColumns.Count != 2)
        {
            message = "La tabla 'schema_metadata' debe contener schema_version e initialized_utc.";
            return false;
        }

        var schemaVersion = metadataColumns.SingleOrDefault(column => string.Equals(column.Name, "schema_version", StringComparison.OrdinalIgnoreCase));
        var initializedUtc = metadataColumns.SingleOrDefault(column => string.Equals(column.Name, "initialized_utc", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(schemaVersion.Name) || !string.Equals(schemaVersion.DuckDbType, "INTEGER", StringComparison.OrdinalIgnoreCase) || schemaVersion.IsNullable)
        {
            message = "La columna de metadatos 'schema_version' debe ser INTEGER NOT NULL.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(initializedUtc.Name) || !string.Equals(initializedUtc.DuckDbType, "TIMESTAMP", StringComparison.OrdinalIgnoreCase) || initializedUtc.IsNullable)
        {
            message = "La columna de metadatos 'initialized_utc' debe ser TIMESTAMP NOT NULL.";
            return false;
        }

        if (!HasPrimaryKey(connection, "schema_metadata", ["schema_version"]))
        {
            message = "La tabla de metadatos debe tener PRIMARY KEY (schema_version).";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static IReadOnlyList<(string Name, string DuckDbType, bool IsNullable)> ReadColumns(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_schema = 'main' AND table_name = $tableName ORDER BY ordinal_position;";
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));

        var columns = new List<(string Name, string DuckDbType, bool IsNullable)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add((
                reader.GetString(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)));
        }

        return columns;
    }

    private static bool HasPrimaryKey(DuckDBConnection connection, string tableName, IReadOnlyList<string> expectedColumns)
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
              AND tc.constraint_type = 'PRIMARY KEY'
            ORDER BY kcu.ordinal_position;
            """;
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns.SequenceEqual(expectedColumns, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TableExists(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'main' AND table_name = $tableName;";
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool HasAnyApplicationTable(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'main'
              AND table_name IN ('personas', 'import_runs', 'personas_staging', 'stock_headers', 'stock_members');
            """;

        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    // Aplica en orden las migraciones faltantes y registra cada versión dentro de transacciones.
    private static void ApplyPendingMigrations(DuckDBConnection connection)
    {
        using (var bootstrapTransaction = connection.BeginTransaction())
        {
            var currentVersion = GetCurrentSchemaVersion(connection, bootstrapTransaction);
            if (currentVersion > SupportedSchemaContract.SupportedSchemaVersion)
            {
                throw new UnsupportedSchemaVersionException(currentVersion, SupportedSchemaContract.SupportedSchemaVersion);
            }

            if (currentVersion == 0)
            {
                InsertSchemaVersion(connection, bootstrapTransaction, BaselineSchemaVersion);
                currentVersion = BaselineSchemaVersion;
            }

            bootstrapTransaction.Commit();
        }

        var currentSchemaVersion = GetCurrentSchemaVersionWithoutTransaction(connection);

        foreach (var migration in SchemaMigrations.All.OrderBy(m => m.Version))
        {
            if (migration.Version <= currentSchemaVersion)
            {
                continue;
            }

            using var migrationTransaction = connection.BeginTransaction();
            try
            {
                ExecuteSql(connection, migrationTransaction, migration.Sql);
                InsertSchemaVersion(connection, migrationTransaction, migration.Version);
                migrationTransaction.Commit();
                currentSchemaVersion = migration.Version;
            }
            catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
            {
                try
                {
                    migrationTransaction.Rollback();
                }
                catch
                {
                    // TODO(database-maintenance-hardening): If rollback throws, apply fatal-exception propagation here and preserve that failure without hiding the original migration error.
                }

                throw;
            }
        }
    }

    private static int GetCurrentSchemaVersionWithoutTransaction(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(schema_version), 0) FROM schema_metadata;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int GetCurrentSchemaVersion(DuckDBConnection connection, System.Data.Common.DbTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(schema_version), 0) FROM schema_metadata;";

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void InsertSchemaVersion(DuckDBConnection connection, System.Data.Common.DbTransaction transaction, int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO schema_metadata (schema_version) VALUES ($version) ON CONFLICT DO NOTHING;";
        command.Parameters.Add(new DuckDBParameter("version", version));
        command.ExecuteNonQuery();
    }

    private static void ExecuteSql(DuckDBConnection connection, System.Data.Common.DbTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class UnsupportedSchemaVersionException : Exception
    {
        public UnsupportedSchemaVersionException(int currentVersion, int supportedVersion)
            : base($"La versión del esquema {currentVersion} no está admitida. La versión admitida es {supportedVersion}.")
        {
        }
    }
}
