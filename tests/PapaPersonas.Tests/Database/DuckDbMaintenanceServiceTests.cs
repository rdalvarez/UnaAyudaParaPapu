using DuckDB.NET.Data;
using PapaPersonas.Core.Database;
using PapaPersonas.Infrastructure.Database;

namespace PapaPersonas.Tests.Database;

public sealed class DuckDbMaintenanceServiceTests
{
    private const int TargetSchemaVersion = 5;

    [Fact]
    public void GetLatestImportDate_ReturnsMaximumDateFromPersonasTable()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");

        try
        {
            var bootstrapper = new DuckDbBootstrapper();
            Assert.True(bootstrapper.Initialize(databasePath).IsSuccess);

            using (var connection = OpenConnection(databasePath))
            {
                ExecuteNonQuery(
                    connection,
                    "INSERT INTO personas (cuil, fecha_actualizacion, fecha_importacion) VALUES ('10000000001', TIMESTAMP '2026-08-01 10:00:00', DATE '2026-08-10');");
                ExecuteNonQuery(
                    connection,
                    "INSERT INTO personas (cuil, fecha_actualizacion, fecha_importacion) VALUES ('10000000002', TIMESTAMP '2026-08-02 10:00:00', DATE '2026-08-19');");
            }

            var service = new DuckDbMaintenanceService();
            var latestImportDate = service.GetLatestImportDate(databasePath);

            Assert.Equal(new DateOnly(2026, 8, 19), latestImportDate);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void BackupCurrentDatabase_ProducesOpenableSchemaValidCopy_AndSuggestedNameUsesLatestImportDate()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var backupPath = Path.Combine(testDirectory, "exports", "2026-08-19_BaseMaestra_BK.duckdb");

        try
        {
            SeedLiveDatabase(databasePath, importDate: new DateOnly(2026, 8, 19));

            var service = new DuckDbMaintenanceService();
            var result = service.BackupCurrentDatabase(databasePath, backupPath);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(backupPath, result.OutputPath);
            Assert.Equal(new DateOnly(2026, 8, 19), result.LatestImportDate);
            Assert.True(File.Exists(backupPath));

            using var backupConnection = OpenConnection(backupPath);
            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(backupConnection));
            Assert.True(TableExists(backupConnection, "personas"));
            Assert.Equal(1, ExecuteScalar<int>(backupConnection, "SELECT COUNT(*) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void BackupCurrentDatabase_CleansTemporaryArtifacts_WhenFinalizeFails()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var exportDirectory = Path.Combine(testDirectory, "locked-export");
        var backupPath = Path.Combine(exportDirectory, "copia.duckdb");

        try
        {
            SeedLiveDatabase(databasePath, importDate: new DateOnly(2026, 8, 19));
            Directory.CreateDirectory(exportDirectory);
            File.WriteAllText(backupPath, "locked target");

            using var lockStream = new FileStream(backupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var service = new DuckDbMaintenanceService();
            var result = service.BackupCurrentDatabase(databasePath, backupPath);

            Assert.False(result.IsSuccess);
            Assert.True(File.Exists(backupPath));
            Assert.DoesNotContain(Directory.EnumerateFiles(exportDirectory, "*", SearchOption.TopDirectoryOnly), path => path.Contains(".tmp", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void BackupCurrentDatabase_AppendsExactTechnicalDetail_WhenInterleavingFails()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var backupPath = Path.Combine(testDirectory, "exports", "backup.duckdb");
        const string technicalMessage = "synthetic backup failure\r\ncode=BK-1";

        try
        {
            SeedLiveDatabase(databasePath, importDate: new DateOnly(2026, 8, 19));
            var service = new DuckDbMaintenanceService(
                new DuckDbBootstrapper(),
                point =>
                {
                    if (point == DatabaseMaintenanceInterleavingPoint.BackupSnapshotCopied)
                    {
                        throw new InvalidOperationException(technicalMessage);
                    }
                });

            var result = service.BackupCurrentDatabase(databasePath, backupPath);

            Assert.False(result.IsSuccess);
            Assert.Equal(
                $"No se pudo crear la copia de seguridad. Verificá la base y volvé a intentar. Detalle técnico: {technicalMessage}",
                result.Message);
            Assert.DoesNotContain(nameof(InvalidOperationException), result.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(" at ", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_RejectsMissingAndSameLiveInputs_WithoutMutatingSelectedSource()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourceBackupPath = Path.Combine(testDirectory, "source", "backup.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourceBackupPath, importDate: new DateOnly(2026, 8, 19));

            var originalBytes = File.ReadAllBytes(sourceBackupPath);
            var service = new DuckDbMaintenanceService();

            var missingResult = service.RestoreDatabase(livePath, Path.Combine(testDirectory, "missing.duckdb"), GetSafetyRoot(testDirectory));
            var sameLiveResult = service.RestoreDatabase(livePath, livePath, GetSafetyRoot(testDirectory));

            Assert.False(missingResult.IsSuccess);
            Assert.False(sameLiveResult.IsSuccess);
            Assert.Equal(originalBytes, File.ReadAllBytes(sourceBackupPath));
            using var liveConnection = OpenConnection(livePath);
            Assert.Equal(1, ExecuteScalar<int>(liveConnection, "SELECT COUNT(*) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_SucceedsAgainstCorruptLiveDatabase_AndPreservesRawEvidence()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourceBackupPath = Path.Combine(testDirectory, "source", "backup.duckdb");
        var safetyRoot = GetSafetyRoot(testDirectory);

        try
        {
            var corruptLiveBytes = "corrupt live bytes"u8.ToArray();
            var corruptWalBytes = "corrupt wal bytes"u8.ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(livePath)!);
            File.WriteAllBytes(livePath, corruptLiveBytes);
            File.WriteAllBytes($"{livePath}.wal", corruptWalBytes);

            SeedLiveDatabase(sourceBackupPath, importDate: new DateOnly(2026, 8, 19));
            using (var sourceConnection = OpenConnection(sourceBackupPath))
            {
                ExecuteNonQuery(
                    sourceConnection,
                    "INSERT INTO personas (cuil, fecha_actualizacion, fecha_importacion) VALUES ('20000000001', TIMESTAMP '2026-08-19 10:00:00', DATE '2026-08-19');");
            }

            var service = new DuckDbMaintenanceService();
            var result = service.RestoreDatabase(livePath, sourceBackupPath, safetyRoot);

            Assert.True(result.IsSuccess, result.Message);
            Assert.True(Directory.Exists(result.SafetyBackupPath));
            Assert.Equal(corruptLiveBytes, File.ReadAllBytes(Path.Combine(result.SafetyBackupPath!, Path.GetFileName(livePath))));
            Assert.Equal(corruptWalBytes, File.ReadAllBytes(Path.Combine(result.SafetyBackupPath!, Path.GetFileName(livePath) + ".wal")));

            using var restoredConnection = OpenConnection(livePath);
            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(restoredConnection));
            Assert.Equal(2, ExecuteScalar<int>(restoredConnection, "SELECT COUNT(*) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_ReturnsFailedWhenLiveDatabaseIsLockedAndLeavesNoPartialSafetyArtifacts()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourceBackupPath = Path.Combine(testDirectory, "source", "backup.duckdb");
        var safetyRoot = GetSafetyRoot(testDirectory);

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourceBackupPath, importDate: new DateOnly(2026, 8, 19));

            using var lockStream = new FileStream(livePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var service = new DuckDbMaintenanceService();
            var result = service.RestoreDatabase(livePath, sourceBackupPath, safetyRoot);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Message);
            Assert.Contains("acceso estable", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(safetyRoot));
            Assert.Empty(Directory.EnumerateFileSystemEntries(safetyRoot));

            lockStream.Dispose();

            using var liveConnection = OpenConnection(livePath);
            Assert.Equal(1, ExecuteScalar<int>(liveConnection, "SELECT COUNT(*) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_ReturnsFailedWhenSafetyRootPathIsInvalid()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourceBackupPath = Path.Combine(testDirectory, "source", "backup.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourceBackupPath, importDate: new DateOnly(2026, 8, 19));

            var service = new DuckDbMaintenanceService();
            var result = service.RestoreDatabase(livePath, sourceBackupPath, Path.Combine(testDirectory, "safety|root"));

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Message);

            using var liveConnection = OpenConnection(livePath);
            Assert.Equal(1, ExecuteScalar<int>(liveConnection, "SELECT COUNT(*) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_LeavesCurrentDatabaseIntactWhenSourceIsCorrupt()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var corruptSourcePath = Path.Combine(testDirectory, "source", "backup.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            using (var seededLiveConnection = OpenConnection(livePath))
            {
                ExecuteNonQuery(
                    seededLiveConnection,
                    "INSERT INTO personas (cuil, fecha_actualizacion, fecha_importacion) VALUES ('30000000001', TIMESTAMP '2026-08-18 10:00:00', DATE '2026-08-18');");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(corruptSourcePath)!);
            File.WriteAllText(corruptSourcePath, "not a duckdb file");

            var service = new DuckDbMaintenanceService();
            var result = service.RestoreDatabase(livePath, corruptSourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            using var restoredLiveConnection = OpenConnection(livePath);
            Assert.Equal(1, ExecuteScalar<int>(restoredLiveConnection, "SELECT COUNT(*) FROM personas WHERE cuil = '30000000001';"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void BackupCurrentDatabase_FailsSafelyWhenLiveDatabaseIsExternallyLocked()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var backupPath = Path.Combine(testDirectory, "exports", "backup.duckdb");

        try
        {
            SeedLiveDatabase(databasePath, importDate: new DateOnly(2026, 8, 19));
            using var lockStream = new FileStream(databasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = new DuckDbMaintenanceService().BackupCurrentDatabase(databasePath, backupPath);

            Assert.False(result.IsSuccess);
            Assert.False(File.Exists(backupPath));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(backupPath)!, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_RejectsZeroByteSourceBeforeLiveMutation()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "empty.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, []);
            var originalLiveBytes = File.ReadAllBytes(livePath);

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseMaintenanceFailureCode.SourceEmpty, result.FailureCode);
            Assert.Equal(originalLiveBytes, File.ReadAllBytes(livePath));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_RejectsArbitraryDuckDbWithoutLiveMutation()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "arbitrary.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, "not a PapaPersonas backup");
            var originalSourceBytes = File.ReadAllBytes(sourcePath);

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseMaintenanceFailureCode.SourceNotRecognized, result.FailureCode);
            Assert.Equal(originalSourceBytes, File.ReadAllBytes(sourcePath));
            using var live = OpenConnection(livePath);
            Assert.Equal(1, ExecuteScalar<int>(live, "SELECT COUNT(*) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_RejectsCurrentEmptyInitializedSource()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "empty-initialized.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            Assert.True(new DuckDbBootstrapper().Initialize(sourcePath).IsSuccess);

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseMaintenanceFailureCode.SourceEmpty, result.FailureCode);
            Assert.Equal(
                "La copia seleccionada está vacía y no es una base válida. Detalle técnico: La copia seleccionada está vacía: no contiene registros operativos para restaurar.",
                result.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_MapsCurrentSchemaContractMessageContainingSchemaPhraseToSourceNotRecognized()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "schema-contract.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourcePath, importDate: new DateOnly(2026, 8, 19));
            using (var source = OpenConnection(sourcePath))
            {
                ExecuteNonQuery(source, "ALTER TABLE personas ADD COLUMN \"versión de esquema\" VARCHAR;");
            }

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseMaintenanceFailureCode.SourceNotRecognized, result.FailureCode);
            Assert.Equal(
                "La copia seleccionada no es una base PapaPersonas reconocible. Detalle técnico: La copia seleccionada no es compatible. La columna inesperada 'personas.versión de esquema' está presente.",
                result.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_RejectsInvalidSchemaMetadataWithExactMessage()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "invalid-metadata.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourcePath, importDate: new DateOnly(2026, 8, 19));
            using (var source = OpenConnection(sourcePath))
            {
                ExecuteNonQuery(source, "ALTER TABLE schema_metadata ADD COLUMN extra_metadata VARCHAR;");
            }

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseMaintenanceFailureCode.SourceNotRecognized, result.FailureCode);
            Assert.Equal(
                "La copia seleccionada no es una base PapaPersonas reconocible. Detalle técnico: La copia seleccionada no contiene metadatos del esquema PapaPersonas válidos.",
                result.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_RejectsUnrecognizedSchemaVersionStructuresWithExactMessage()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "missing-structure.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourcePath, importDate: new DateOnly(2026, 8, 19));
            using (var source = OpenConnection(sourcePath))
            {
                ExecuteNonQuery(source, "DROP TABLE stock_members;");
            }

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseMaintenanceFailureCode.SourceNotRecognized, result.FailureCode);
            Assert.Equal(
                "La copia seleccionada no es una base PapaPersonas reconocible. Detalle técnico: La copia seleccionada no contiene las estructuras PapaPersonas reconocibles para la versión del esquema indicada.",
                result.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_AcceptsPopulatedCurrentSourceAndLeavesSourceBytesUnchanged()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "current.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourcePath, importDate: new DateOnly(2026, 8, 19));
            var sourceBytes = File.ReadAllBytes(sourcePath);

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
            using var restored = OpenConnection(livePath);
            Assert.Equal("2026-08-19", ExecuteScalar<string>(restored, "SELECT CAST(MAX(fecha_importacion) AS VARCHAR) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_HoldsDatabaseAndWalWriteBlockUntilReplacement()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "restore.duckdb");
        var liveWalPath = $"{livePath}.wal";
        var blockedPaths = new List<string>();

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourcePath, importDate: new DateOnly(2026, 8, 19));
            File.WriteAllBytes(liveWalPath, "live wal"u8.ToArray());

            var service = new DuckDbMaintenanceService(
                new DuckDbBootstrapper(),
                point =>
                {
                    if (point != DatabaseMaintenanceInterleavingPoint.RestoreSafetySnapshotCopied)
                    {
                        return;
                    }

                    foreach (var path in new[] { livePath, liveWalPath })
                    {
                        try
                        {
                            using var externalWrite = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                        }
                        catch (IOException)
                        {
                            blockedPaths.Add(path);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            blockedPaths.Add(path);
                        }
                    }
                });

            var result = service.RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal([livePath, liveWalPath], blockedPaths);
            using var restored = OpenConnection(livePath);
            Assert.Equal("2026-08-19", ExecuteScalar<string>(restored, "SELECT CAST(MAX(fecha_importacion) AS VARCHAR) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_RollsBackWhileBoundaryLeaseIsHeld_AndReleasesHandles()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "restore.duckdb");
        var liveWalPath = $"{livePath}.wal";

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourcePath, importDate: new DateOnly(2026, 8, 19));
            var originalWalBytes = "rollback wal"u8.ToArray();
            File.WriteAllBytes(liveWalPath, originalWalBytes);

            var service = new DuckDbMaintenanceService(
                new DuckDbBootstrapper(),
                point =>
                {
                    if (point == DatabaseMaintenanceInterleavingPoint.RestoreSafetySnapshotCopied)
                    {
                        throw new IOException("deterministic replacement failure");
                    }
                });

            var result = service.RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(
                "No se pudo restaurar la base seleccionada. Verificá la copia y volvé a intentar. Detalle técnico: deterministic replacement failure",
                result.Message);
            using (var live = OpenConnection(livePath))
            {
                Assert.Equal("2026-08-18", ExecuteScalar<string>(live, "SELECT CAST(MAX(fecha_importacion) AS VARCHAR) FROM personas;"));
            }
            Assert.Equal(originalWalBytes, File.ReadAllBytes(liveWalPath));

            using var exclusive = new FileStream(livePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void BackupCurrentDatabase_HoldsSourceLeaseUntilPublishedDestinationExists()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var backupPath = Path.Combine(testDirectory, "exports", "backup.duckdb");
        var writeBlocked = false;

        try
        {
            SeedLiveDatabase(databasePath, importDate: new DateOnly(2026, 8, 19));
            var service = new DuckDbMaintenanceService(
                new DuckDbBootstrapper(),
                point =>
                {
                    if (point != DatabaseMaintenanceInterleavingPoint.BackupSnapshotCopied)
                    {
                        return;
                    }

                    try
                    {
                        using var externalWrite = new FileStream(databasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    }
                    catch (IOException)
                    {
                        writeBlocked = true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        writeBlocked = true;
                    }

                    Assert.False(File.Exists(backupPath));
                });

            var result = service.BackupCurrentDatabase(databasePath, backupPath);

            Assert.True(result.IsSuccess, result.Message);
            Assert.True(writeBlocked);
            Assert.True(File.Exists(backupPath));
            using var exclusive = new FileStream(databasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_AcceptsPopulatedOlderSchemaAndMigratesOnStageCopy()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "older.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourcePath, importDate: new DateOnly(2026, 8, 19));
            using (var source = OpenConnection(sourcePath))
            {
                ExecuteNonQuery(source, "DROP TABLE stock_members; DROP TABLE stock_headers; DELETE FROM schema_metadata WHERE schema_version > 2;");
                Assert.Equal(2, ExecuteScalar<int>(source, "SELECT MAX(schema_version) FROM schema_metadata;"));
            }

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.True(result.IsSuccess, result.Message);
            using var restored = OpenConnection(livePath);
            Assert.Equal(TargetSchemaVersion, ExecuteScalar<int>(restored, "SELECT MAX(schema_version) FROM schema_metadata;"));
            Assert.Equal(1, ExecuteScalar<int>(restored, "SELECT COUNT(*) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_RejectsNewerSchemaBeforeLiveMutation()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "newer.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourcePath, importDate: new DateOnly(2026, 8, 19));
            using (var source = OpenConnection(sourcePath))
            {
                ExecuteNonQuery(source, "INSERT INTO schema_metadata (schema_version) VALUES (999);");
            }

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseMaintenanceFailureCode.UnsupportedSchema, result.FailureCode);
            Assert.Equal(
                "La copia seleccionada usa una versión del esquema no admitida. Detalle técnico: La copia seleccionada usa la versión del esquema 999, que es más reciente que la admitida por esta aplicación.",
                result.Message);
            using var live = OpenConnection(livePath);
            Assert.Equal("2026-08-18", ExecuteScalar<string>(live, "SELECT CAST(MAX(fecha_importacion) AS VARCHAR) FROM personas;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void RestoreDatabase_FailsBeforeLiveMutationWhenSourceIsExternallyLocked()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        var sourcePath = Path.Combine(testDirectory, "source", "locked.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            SeedLiveDatabase(sourcePath, importDate: new DateOnly(2026, 8, 19));
            using var lockStream = new FileStream(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = new DuckDbMaintenanceService().RestoreDatabase(livePath, sourcePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseMaintenanceFailureCode.SourceLocked, result.FailureCode);
            Assert.True(!Directory.Exists(GetSafetyRoot(testDirectory)) || !Directory.EnumerateFileSystemEntries(GetSafetyRoot(testDirectory)).Any());
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void ResetDatabase_CreatesFreshEmptySchemaAtLivePath()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));
            using (var seededLiveConnection = OpenConnection(livePath))
            {
                ExecuteNonQuery(
                    seededLiveConnection,
                    "INSERT INTO personas (cuil, fecha_actualizacion, fecha_importacion) VALUES ('40000000001', TIMESTAMP '2026-08-18 10:00:00', DATE '2026-08-18');");
            }

            var service = new DuckDbMaintenanceService();
            var result = service.ResetDatabase(livePath, GetSafetyRoot(testDirectory));

            Assert.True(result.IsSuccess, result.Message);
            using var resetConnection = OpenConnection(livePath);
            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(resetConnection));
            Assert.Equal(0, ExecuteScalar<int>(resetConnection, "SELECT COUNT(*) FROM personas;"));
            Assert.Equal(0, ExecuteScalar<int>(resetConnection, "SELECT COUNT(*) FROM import_runs;"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void ResetDatabase_PropagatesFatalBootstrapperException()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));

            var service = new DuckDbMaintenanceService(new ThrowingBootstrapper());

            Assert.Throws<OutOfMemoryException>(() => service.ResetDatabase(livePath, GetSafetyRoot(testDirectory)));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void ResetDatabase_AppendsExactTechnicalDetail_WhenBootstrapperReturnsFailure()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var livePath = Path.Combine(testDirectory, "live", "PapaPersonas.duckdb");
        const string technicalMessage = "synthetic reset bootstrap failure\r\ncode=RS-1";

        try
        {
            SeedLiveDatabase(livePath, importDate: new DateOnly(2026, 8, 18));

            var service = new DuckDbMaintenanceService(new FailingBootstrapper(technicalMessage));
            var result = service.ResetDatabase(livePath, GetSafetyRoot(testDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(
                $"No se pudo reiniciar la base. Verificá la base y volvé a intentar. Detalle técnico: {technicalMessage}",
                result.Message);
            Assert.DoesNotContain(nameof(InvalidOperationException), result.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(" at ", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    private static void SeedLiveDatabase(string databasePath, DateOnly importDate)
    {
        var bootstrapper = new DuckDbBootstrapper();
        var result = bootstrapper.Initialize(databasePath);
        Assert.True(result.IsSuccess, result.Message);

        using var connection = OpenConnection(databasePath);
        ExecuteNonQuery(
            connection,
            "INSERT INTO personas (cuil, fecha_actualizacion, fecha_importacion) VALUES ('10000000001', TIMESTAMP '2026-08-19 10:00:00', $importDate);",
            new DuckDBParameter("importDate", importDate));
    }

    private static string GetSafetyRoot(string testDirectory) => Path.Combine(testDirectory, "safety-backups");

    private static DuckDBConnection OpenConnection(string databasePath)
    {
        var builder = new DuckDBConnectionStringBuilder { DataSource = databasePath };
        var connection = new DuckDBConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(DuckDBConnection connection, string sql, params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        command.ExecuteNonQuery();
    }

    private static T ExecuteScalar<T>(DuckDBConnection connection, string sql, params DuckDBParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var value = command.ExecuteScalar();
        return (T)Convert.ChangeType(value!, typeof(T))!;
    }

    private static int GetCurrentSchemaVersion(DuckDBConnection connection)
    {
        return ExecuteScalar<int>(connection, "SELECT COALESCE(MAX(schema_version), 0) FROM schema_metadata;");
    }

    private static bool TableExists(DuckDBConnection connection, string tableName)
    {
        return ExecuteScalar<int>(
            connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'main' AND table_name = $tableName;",
            new DuckDBParameter("tableName", tableName)) == 1;
    }

    private static string CreateUniqueTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for test artifacts.
        }
    }

    private sealed class ThrowingBootstrapper : IDatabaseBootstrapper
    {
        public DatabaseBootstrapResult Initialize(string databaseFilePath)
        {
            throw new OutOfMemoryException("fatal test exception");
        }
    }

    private sealed class FailingBootstrapper : IDatabaseBootstrapper
    {
        private readonly string _message;

        public FailingBootstrapper(string message)
        {
            _message = message;
        }

        public DatabaseBootstrapResult Initialize(string databaseFilePath) =>
            DatabaseBootstrapResult.Failure(databaseFilePath, _message);
    }
}
