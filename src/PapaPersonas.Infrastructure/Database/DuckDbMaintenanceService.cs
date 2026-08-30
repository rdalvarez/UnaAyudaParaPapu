using DuckDB.NET.Data;
using PapaPersonas.Core.Database;
using PapaPersonas.Core.Import;

namespace PapaPersonas.Infrastructure.Database;

public sealed class DuckDbMaintenanceService : IDatabaseMaintenanceService
{
    private readonly IDatabaseBootstrapper _bootstrapper;
    private readonly Action<DatabaseMaintenanceInterleavingPoint>? _interleavingHook;

    public DuckDbMaintenanceService()
        : this(new DuckDbBootstrapper())
    {
    }

    internal DuckDbMaintenanceService(
        IDatabaseBootstrapper bootstrapper,
        Action<DatabaseMaintenanceInterleavingPoint>? interleavingHook = null)
    {
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _interleavingHook = interleavingHook;
    }

    public DateOnly? GetLatestImportDate(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return null;
        }

        try
        {
            using var connection = OpenConnection(databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(fecha_importacion) FROM personas;";

            return ReadDateOnly(command.ExecuteScalar());
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            return null;
        }
    }

    /// <summary>Crea una copia consistente de la base activa mediante un archivo temporal y checkpoint.</summary>
    public DatabaseMaintenanceBackupResult BackupCurrentDatabase(string databasePath, string destinationPath)
    {
        if (!TryValidatePaths(databasePath, destinationPath, out var fullDatabasePath, out var fullDestinationPath, out var errorMessage))
        {
            return DatabaseMaintenanceBackupResult.Failed(errorMessage);
        }

        if (string.Equals(fullDatabasePath, fullDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseMaintenanceBackupResult.Failed("La ruta de destino no puede ser la base en uso.");
        }

        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return DatabaseMaintenanceBackupResult.Failed("La ruta de destino no es válida.");
        }

        var tempPath = BuildSiblingTempPath(destinationDirectory, Path.GetFileName(fullDestinationPath));
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            CheckpointDatabase(fullDatabasePath);
            using var sourceLease = OpenDatabaseFileLease(fullDatabasePath);
            if (sourceLease.WalStream is not null)
            {
                throw new IOException("El WAL de la base sigue presente después del checkpoint; la copia se canceló para evitar un snapshot inconsistente.");
            }

            CopyPreservedFile(sourceLease.DatabaseStream, fullDatabasePath, tempPath);
            _interleavingHook?.Invoke(DatabaseMaintenanceInterleavingPoint.BackupSnapshotCopied);
            var latestImportDate = GetLatestImportDate(tempPath);
            File.Move(tempPath, fullDestinationPath, overwrite: true);

            return DatabaseMaintenanceBackupResult.Completed(fullDestinationPath, latestImportDate);
        }
        catch (IOException ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            CleanupIfExists(tempPath);
            return DatabaseMaintenanceBackupResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo obtener acceso exclusivo a la base. Cerrá DBeaver y las demás herramientas DuckDB, y volvé a intentar.",
                    ex));
        }
        catch (UnauthorizedAccessException ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            CleanupIfExists(tempPath);
            return DatabaseMaintenanceBackupResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo obtener acceso exclusivo a la base. Cerrá DBeaver y las demás herramientas DuckDB, y volvé a intentar.",
                    ex));
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            CleanupIfExists(tempPath);
            return DatabaseMaintenanceBackupResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo crear la copia de seguridad. Verificá la base y volvé a intentar.",
                    ex));
        }
    }

    /// <summary>Preserva la base activa, valida la copia en staging y reemplaza el archivo sólo al final.</summary>
    public DatabaseMaintenanceMutationResult RestoreDatabase(string liveDatabasePath, string backupSourcePath, string safetyBackupRootDirectory)
    {
        if (!TryValidatePaths(liveDatabasePath, backupSourcePath, out var fullLiveDatabasePath, out var fullBackupSourcePath, out var errorMessage))
        {
            return DatabaseMaintenanceMutationResult.Failed(errorMessage);
        }

        if (string.Equals(fullLiveDatabasePath, fullBackupSourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseMaintenanceMutationResult.Failed("No se puede restaurar la base usando la misma ruta que está en uso.");
        }

        if (!File.Exists(fullBackupSourcePath))
        {
            return DatabaseMaintenanceMutationResult.Failed("La copia seleccionada no existe.");
        }

        string stagePath = string.Empty;
        SafetySnapshotResult? safetySnapshot = null;
        try
        {
            stagePath = BuildStageCopyPath(fullLiveDatabasePath, "restore");
            ValidateBackupSourceOnStageCopy(_bootstrapper, fullBackupSourcePath, stagePath);

            safetySnapshot = PreserveCurrentDatabase(fullLiveDatabasePath, safetyBackupRootDirectory, "restore");
            if (!safetySnapshot.IsSuccess)
            {
                return DatabaseMaintenanceMutationResult.Failed(
                    safetySnapshot.Message ?? "No se pudo preservar la base actual.",
                    failureCode: DatabaseMaintenanceFailureCode.LiveDatabaseLocked);
            }

            _interleavingHook?.Invoke(DatabaseMaintenanceInterleavingPoint.RestoreSafetySnapshotCopied);
            ReplaceLiveDatabase(fullLiveDatabasePath, stagePath, safetySnapshot);

            return DatabaseMaintenanceMutationResult.Completed(
                fullLiveDatabasePath,
                safetySnapshot.SafetyBackupPath,
                safetySnapshot.PreservedRawDatabase,
                "Base restaurada correctamente.");
        }
        catch (RestoreSourceValidationException ex)
        {
            CleanupIfExists(stagePath);
            return DatabaseMaintenanceMutationResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(RestoreFailureMessage(ex.FailureCode), ex),
                failureCode: ex.FailureCode);
        }
        catch (IOException ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            if (safetySnapshot is null)
            {
                CleanupIfExists(stagePath);
                return DatabaseMaintenanceMutationResult.Failed(
                    UserFacingExceptionMessage.WithTechnicalDetail(
                        "No se pudo obtener acceso exclusivo a la copia seleccionada. Cerrá DBeaver y las demás herramientas DuckDB, y volvé a intentar.",
                        ex),
                    failureCode: DatabaseMaintenanceFailureCode.SourceLocked);
            }

            TryRollbackLiveDatabase(fullLiveDatabasePath, safetySnapshot);
            CleanupIfExists(stagePath);
            return DatabaseMaintenanceMutationResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo restaurar la base seleccionada. Verificá la copia y volvé a intentar.",
                    ex),
                safetySnapshot.SafetyBackupPath,
                DatabaseMaintenanceFailureCode.OperationFailed);
        }
        catch (UnauthorizedAccessException ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            if (safetySnapshot is null)
            {
                CleanupIfExists(stagePath);
                return DatabaseMaintenanceMutationResult.Failed(
                    UserFacingExceptionMessage.WithTechnicalDetail(
                        "No se pudo obtener acceso exclusivo a la copia seleccionada. Cerrá DBeaver y las demás herramientas DuckDB, y volvé a intentar.",
                        ex),
                    failureCode: DatabaseMaintenanceFailureCode.SourceLocked);
            }

            TryRollbackLiveDatabase(fullLiveDatabasePath, safetySnapshot);
            CleanupIfExists(stagePath);
            return DatabaseMaintenanceMutationResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo restaurar la base seleccionada. Verificá la copia y volvé a intentar.",
                    ex),
                safetySnapshot.SafetyBackupPath,
                DatabaseMaintenanceFailureCode.OperationFailed);
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            if (safetySnapshot is not null)
            {
                TryRollbackLiveDatabase(fullLiveDatabasePath, safetySnapshot);
            }

            CleanupIfExists(stagePath);
            return DatabaseMaintenanceMutationResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo restaurar la base seleccionada. Verificá la copia y volvé a intentar.",
                    ex),
                safetySnapshot?.SafetyBackupPath,
                DatabaseMaintenanceFailureCode.OperationFailed);
        }
        finally
        {
            safetySnapshot?.Lease?.Dispose();
            CleanupIfExists(stagePath);
            CleanupIfExists(GetWalPath(stagePath));
        }
    }

    /// <summary>Preserva la base activa y la reemplaza por una base nueva cuyo esquema ya fue validado.</summary>
    public DatabaseMaintenanceMutationResult ResetDatabase(string liveDatabasePath, string safetyBackupRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(liveDatabasePath))
        {
            return DatabaseMaintenanceMutationResult.Failed("La ruta de la base no puede estar vacía.");
        }

        string fullLiveDatabasePath;
        try
        {
            fullLiveDatabasePath = Path.GetFullPath(liveDatabasePath);
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            return DatabaseMaintenanceMutationResult.Failed("La ruta de la base no es válida.");
        }

        var safetySnapshot = PreserveCurrentDatabase(fullLiveDatabasePath, safetyBackupRootDirectory, "reset");
        if (!safetySnapshot.IsSuccess)
        {
            return DatabaseMaintenanceMutationResult.Failed(safetySnapshot.Message ?? "No se pudo preservar la base actual.");
        }

        string stagePath = string.Empty;
        try
        {
            stagePath = BuildStageCopyPath(fullLiveDatabasePath, "reset");
            Directory.CreateDirectory(Path.GetDirectoryName(stagePath)!);
            var bootstrapResult = _bootstrapper.Initialize(stagePath);
            if (!bootstrapResult.IsSuccess)
            {
                throw new InvalidOperationException(bootstrapResult.Message);
            }

            CheckpointDatabase(stagePath);
            ReplaceLiveDatabase(fullLiveDatabasePath, stagePath, safetySnapshot);

            return DatabaseMaintenanceMutationResult.Completed(
                fullLiveDatabasePath,
                safetySnapshot.SafetyBackupPath,
                safetySnapshot.PreservedRawDatabase,
                "Base reiniciada correctamente.");
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            TryRollbackLiveDatabase(fullLiveDatabasePath, safetySnapshot);
            CleanupIfExists(stagePath);
            return DatabaseMaintenanceMutationResult.Failed(
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo reiniciar la base. Verificá la base y volvé a intentar.",
                    ex),
                safetySnapshot.SafetyBackupPath);
        }
        finally
        {
            safetySnapshot.Lease?.Dispose();
            CleanupIfExists(stagePath);
            CleanupIfExists(GetWalPath(stagePath));
        }
    }

    private static string BuildSiblingTempPath(string directory, string finalFileName)
    {
        return Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(finalFileName)}.{Guid.NewGuid():N}.tmp");
    }

    private static string BuildStageCopyPath(string liveDatabasePath, string operation)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(liveDatabasePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("La ruta de la base debe incluir una carpeta válida.");
        }

        return Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(liveDatabasePath)}.{operation}.{Guid.NewGuid():N}.duckdb");
    }

    private static void ValidateBackupSourceOnStageCopy(IDatabaseBootstrapper bootstrapper, string backupSourcePath, string stagePath)
    {
        var sourceFullPath = Path.GetFullPath(backupSourcePath);
        var stageDirectory = Path.GetDirectoryName(stagePath);
        if (string.IsNullOrWhiteSpace(stageDirectory))
        {
            throw new InvalidOperationException("No se pudo preparar la carpeta temporal de restauración.");
        }

        Directory.CreateDirectory(stageDirectory);

        var sourceInfo = new FileInfo(sourceFullPath);
        if (sourceInfo.Length == 0)
        {
            throw new RestoreSourceValidationException(
                DatabaseMaintenanceFailureCode.SourceEmpty,
                "La copia seleccionada está vacía y no es una base PapaPersonas válida.");
        }

        CopyDatabaseSnapshot(sourceFullPath, stagePath);

        try
        {
            using var stagedConnection = OpenConnection(stagePath);
            ValidateRecognizedRestoreSource(stagedConnection);
        }
        catch (RestoreSourceValidationException)
        {
            throw;
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            throw new RestoreSourceValidationException(
                DatabaseMaintenanceFailureCode.SourceNotRecognized,
                "La copia seleccionada no es una base DuckDB PapaPersonas reconocible.");
        }

        var bootstrapResult = bootstrapper.Initialize(stagePath);
        if (!bootstrapResult.IsSuccess)
        {
            throw new RestoreSourceValidationException(
                bootstrapResult.FailureCode == DatabaseBootstrapFailureCode.UnsupportedSchemaVersion
                    ? DatabaseMaintenanceFailureCode.UnsupportedSchema
                    : DatabaseMaintenanceFailureCode.SourceNotRecognized,
                $"La copia seleccionada no es compatible. {bootstrapResult.Message}");
        }

        CheckpointDatabase(stagePath);
        CleanupIfExists(GetWalPath(stagePath));
    }

    // Reemplaza la base activa con la etapa validada y restaura el WAL si el movimiento falla.
    private static void ReplaceLiveDatabase(string liveDatabasePath, string stagePath, SafetySnapshotResult safetySnapshot)
    {
        var liveWalPath = GetWalPath(liveDatabasePath);
        var liveWalExists = File.Exists(liveWalPath);

        if (liveWalExists)
        {
            File.Delete(liveWalPath);
        }

        try
        {
            // Se elimina primero: File.Move(overwrite:true) intenta abrir el destino con escritura,
            // incompatible con la lease que bloquea escritores externos.
            File.Delete(liveDatabasePath);
            File.Move(stagePath, liveDatabasePath);
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            if (liveWalExists && File.Exists(safetySnapshot.SafetyWalPath))
            {
                CleanupIfExists(liveWalPath);
                File.Copy(safetySnapshot.SafetyWalPath, liveWalPath);
            }

            throw;
        }

        CleanupIfExists(GetWalPath(stagePath));
    }

    // Restaura la base y el WAL desde el resguardo sin ocultar el resultado principal de la operación.
    private static void TryRollbackLiveDatabase(string liveDatabasePath, SafetySnapshotResult safetySnapshot)
    {
        try
        {
            if (File.Exists(safetySnapshot.SafetyDatabasePath))
            {
                CleanupIfExists(liveDatabasePath);
                File.Copy(safetySnapshot.SafetyDatabasePath, liveDatabasePath);
            }

            if (File.Exists(safetySnapshot.SafetyWalPath))
            {
                CleanupIfExists(GetWalPath(liveDatabasePath));
                File.Copy(safetySnapshot.SafetyWalPath, GetWalPath(liveDatabasePath));
            }
            else
            {
                CleanupIfExists(GetWalPath(liveDatabasePath));
            }
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            // El rollback es de mejor esfuerzo; la copia de seguridad conserva el estado anterior.
        }
    }

    // Crea un resguardo estable de la base y su WAL para permitir recuperación ante fallas.
    private SafetySnapshotResult PreserveCurrentDatabase(string liveDatabasePath, string safetyBackupRootDirectory, string operation)
    {
        string safetyFolder = string.Empty;
        string safetyDatabasePath = string.Empty;
        string safetyWalPath = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(liveDatabasePath))
            {
                return new SafetySnapshotResult(string.Empty, string.Empty, string.Empty, false, false, "La ruta de la base no puede estar vacía.");
            }

            var fullLiveDatabasePath = Path.GetFullPath(liveDatabasePath);
            if (!File.Exists(fullLiveDatabasePath))
            {
                return new SafetySnapshotResult(string.Empty, string.Empty, string.Empty, false, false, "No se encontró la base activa.");
            }

            var safetyRoot = string.IsNullOrWhiteSpace(safetyBackupRootDirectory)
                ? DatabaseMaintenanceNaming.BuildSafetyBackupRootDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                : Path.GetFullPath(safetyBackupRootDirectory);
            Directory.CreateDirectory(safetyRoot);

            // La lease permanece en el resultado hasta decidir reemplazo o rollback.
            var liveLease = OpenDatabaseFileLease(fullLiveDatabasePath);
            try
            {
                safetyFolder = Path.Combine(safetyRoot, $"{operation}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(safetyFolder);

                safetyDatabasePath = Path.Combine(safetyFolder, Path.GetFileName(fullLiveDatabasePath));
                safetyWalPath = GetWalPath(safetyDatabasePath);
                var liveWalPath = GetWalPath(fullLiveDatabasePath);

                CopyPreservedFile(liveLease.DatabaseStream, fullLiveDatabasePath, safetyDatabasePath);

                if (liveLease.WalStream is not null)
                {
                    CopyPreservedFile(liveLease.WalStream, liveWalPath, safetyWalPath);
                }

                return new SafetySnapshotResult(
                    SafetyFolderPath: safetyFolder,
                    SafetyDatabasePath: safetyDatabasePath,
                    SafetyWalPath: safetyWalPath,
                    PreservedRawDatabase: true,
                    IsSuccess: true,
                    Message: null,
                    Lease: liveLease);
            }
            catch
            {
                liveLease.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            CleanupIfExists(safetyFolder);

            if (ex is IOException or UnauthorizedAccessException)
            {
                return new SafetySnapshotResult(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false,
                    false,
                    UserFacingExceptionMessage.WithTechnicalDetail(
                        "No se pudo obtener acceso estable a la base. Cerrá DBeaver y las demás herramientas DuckDB, y volvé a intentar.",
                        ex));
            }

            return new SafetySnapshotResult(
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                false,
                UserFacingExceptionMessage.WithTechnicalDetail("No se pudo preservar la base actual.", ex));
        }
    }

    private static void CopyDatabaseSnapshot(string sourceDatabasePath, string stageDatabasePath)
    {
        var stageDirectory = Path.GetDirectoryName(stageDatabasePath);
        if (string.IsNullOrWhiteSpace(stageDirectory))
        {
            throw new InvalidOperationException("No se pudo preparar la carpeta temporal de restauración.");
        }

        Directory.CreateDirectory(stageDirectory);

        using var sourceLease = OpenDatabaseFileLease(sourceDatabasePath);
        CopyPreservedFile(sourceLease.DatabaseStream, sourceDatabasePath, stageDatabasePath);
        if (sourceLease.WalStream is not null)
        {
            CopyPreservedFile(sourceLease.WalStream, GetWalPath(sourceDatabasePath), GetWalPath(stageDatabasePath));
        }
    }

    private static void CheckpointDatabase(string databasePath)
    {
        using var connection = OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "CHECKPOINT;";
        command.ExecuteNonQuery();
    }

    private static void ValidateRecognizedRestoreSource(DuckDBConnection connection)
    {
        if (!TableExists(connection, "schema_metadata"))
        {
            throw new RestoreSourceValidationException(
                DatabaseMaintenanceFailureCode.SourceNotRecognized,
                "La copia seleccionada no contiene evidencia de una base PapaPersonas.");
        }

        var metadataColumns = ReadTableColumns(connection, "schema_metadata");
        if (metadataColumns.Count != 2
            || !metadataColumns.Any(column => string.Equals(column.Name, "schema_version", StringComparison.OrdinalIgnoreCase)
                                               && string.Equals(column.Type, "INTEGER", StringComparison.OrdinalIgnoreCase)
                                               && !column.IsNullable)
            || !metadataColumns.Any(column => string.Equals(column.Name, "initialized_utc", StringComparison.OrdinalIgnoreCase)
                                               && string.Equals(column.Type, "TIMESTAMP", StringComparison.OrdinalIgnoreCase)
                                               && !column.IsNullable)
            || !HasPrimaryKey(connection, "schema_metadata", ["schema_version"]))
        {
            throw new RestoreSourceValidationException(
                DatabaseMaintenanceFailureCode.SourceNotRecognized,
                "La copia seleccionada no contiene metadatos del esquema PapaPersonas válidos.");
        }

        var schemaVersion = ReadCurrentSchemaVersion(connection);
        if (schemaVersion > SupportedSchemaContract.SupportedSchemaVersion)
        {
            throw new RestoreSourceValidationException(
                DatabaseMaintenanceFailureCode.UnsupportedSchema,
                $"La copia seleccionada usa la versión del esquema {schemaVersion}, que es más reciente que la admitida por esta aplicación.");
        }

        var requiredTables = schemaVersion switch
        {
            2 or 3 or 4 => new[] { "personas", "import_runs", "personas_staging" },
            5 => new[] { "personas", "import_runs", "personas_staging", "stock_headers", "stock_members" },
            _ => Array.Empty<string>()
        };

        if (requiredTables.Length == 0 || requiredTables.Any(table => !TableExists(connection, table)))
        {
            throw new RestoreSourceValidationException(
                DatabaseMaintenanceFailureCode.SourceNotRecognized,
                "La copia seleccionada no contiene las estructuras PapaPersonas reconocibles para la versión del esquema indicada.");
        }

        var operationalRows = requiredTables.Sum(table => ReadTableRowCount(connection, table));
        if (operationalRows == 0)
        {
            throw new RestoreSourceValidationException(
                DatabaseMaintenanceFailureCode.SourceEmpty,
                "La copia seleccionada está vacía: no contiene registros operativos para restaurar.");
        }
    }

    private static bool TableExists(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'main' AND table_name = $tableName;";
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static IReadOnlyList<(string Name, string Type, bool IsNullable)> ReadTableColumns(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_schema = 'main' AND table_name = $tableName ORDER BY ordinal_position;";
        command.Parameters.Add(new DuckDBParameter("tableName", tableName));

        var columns = new List<(string Name, string Type, bool IsNullable)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add((reader.GetString(0), reader.GetString(1), string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)));
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

    private static int ReadCurrentSchemaVersion(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(schema_version), 0) FROM schema_metadata;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static long ReadTableRowCount(DuckDBConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static DatabaseFileLease OpenDatabaseFileLease(string databasePath)
    {
        FileStream? databaseStream = null;
        try
        {
            // Comparte sólo DELETE: los escritores no pueden abrir el archivo, pero el proceso puede reemplazarlo.
            databaseStream = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Delete);
            var walPath = GetWalPath(databasePath);
            var walStream = File.Exists(walPath)
                ? new FileStream(walPath, FileMode.Open, FileAccess.Read, FileShare.Delete)
                : null;

            return new DatabaseFileLease(databaseStream, walStream);
        }
        catch
        {
            databaseStream?.Dispose();
            throw;
        }
    }

    // Copia un archivo preservado verificando que no cambie y que el destino conserve su tamaño.
    private static void CopyPreservedFile(FileStream sourceStream, string sourcePath, string destinationPath)
    {
        var sourceInfoBefore = new FileInfo(sourcePath);
        var sourceLength = sourceInfoBefore.Length;
        var sourceLastWriteTimeUtc = sourceInfoBefore.LastWriteTimeUtc;

        sourceStream.Position = 0;
        using var destinationStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        sourceStream.CopyTo(destinationStream);
        destinationStream.Flush(flushToDisk: true);

        var sourceInfoAfter = new FileInfo(sourcePath);
        if (sourceInfoAfter.Length != sourceLength || sourceInfoAfter.LastWriteTimeUtc != sourceLastWriteTimeUtc)
        {
            throw new IOException($"El archivo {Path.GetFileName(sourcePath)} cambió mientras se preservaba.");
        }

        var destinationInfo = new FileInfo(destinationPath);
        if (destinationInfo.Length != sourceLength)
        {
            throw new IOException($"La copia de {Path.GetFileName(sourcePath)} no coincide con el tamaño original.");
        }
    }

    private static DuckDBConnection OpenConnection(string databasePath)
    {
        var builder = new DuckDBConnectionStringBuilder { DataSource = databasePath };
        var connection = new DuckDBConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static string GetWalPath(string databasePath) => $"{databasePath}.wal";

    private static DateOnly? ReadDateOnly(object? value)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        return value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ when DateOnly.TryParse(value.ToString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool TryValidatePaths(string primaryPath, string secondaryPath, out string fullPrimaryPath, out string fullSecondaryPath, out string errorMessage)
    {
        fullPrimaryPath = string.Empty;
        fullSecondaryPath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(primaryPath))
        {
            errorMessage = "La ruta principal no puede estar vacía.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(secondaryPath))
        {
            errorMessage = "La ruta secundaria no puede estar vacía.";
            return false;
        }

        try
        {
            fullPrimaryPath = Path.GetFullPath(primaryPath);
            fullSecondaryPath = Path.GetFullPath(secondaryPath);
            return true;
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            errorMessage = "La ruta seleccionada no es válida.";
            return false;
        }
    }

    private static void CleanupIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (!DatabaseExceptionPolicy.IsFatal(ex))
        {
            // Best effort cleanup only.
        }
    }

    private static string RestoreFailureMessage(DatabaseMaintenanceFailureCode failureCode)
    {
        return failureCode switch
        {
            DatabaseMaintenanceFailureCode.SourceEmpty => "La copia seleccionada está vacía y no es una base válida.",
            DatabaseMaintenanceFailureCode.UnsupportedSchema => "La copia seleccionada usa una versión del esquema no admitida.",
            DatabaseMaintenanceFailureCode.SourceNotRecognized => "La copia seleccionada no es una base PapaPersonas reconocible.",
            DatabaseMaintenanceFailureCode.SourceLocked => "No se pudo acceder a la copia seleccionada. Cerrá DBeaver y las demás herramientas DuckDB, y volvé a intentar.",
            DatabaseMaintenanceFailureCode.LiveDatabaseLocked => "No se pudo preservar la base activa. Cerrá DBeaver y las demás herramientas DuckDB, y volvé a intentar.",
            _ => "No se pudo restaurar la base seleccionada. Verificá la copia y volvé a intentar."
        };
    }

    private sealed record SafetySnapshotResult(
        string SafetyFolderPath,
        string SafetyDatabasePath,
        string SafetyWalPath,
        bool PreservedRawDatabase,
        bool IsSuccess,
        string? Message,
        DatabaseFileLease? Lease = null)
    {
        public string SafetyBackupPath => SafetyFolderPath;
    }

    private sealed class DatabaseFileLease : IDisposable
    {
        public DatabaseFileLease(FileStream databaseStream, FileStream? walStream)
        {
            DatabaseStream = databaseStream;
            WalStream = walStream;
        }

        public FileStream DatabaseStream { get; }
        public FileStream? WalStream { get; }

        public void Dispose()
        {
            WalStream?.Dispose();
            DatabaseStream.Dispose();
        }
    }

    private sealed class RestoreSourceValidationException : Exception
    {
        public RestoreSourceValidationException(DatabaseMaintenanceFailureCode failureCode, string message)
            : base(message)
        {
            FailureCode = failureCode;
        }

        public DatabaseMaintenanceFailureCode FailureCode { get; }
    }
}
