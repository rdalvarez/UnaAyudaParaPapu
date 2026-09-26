using DuckDB.NET.Data;
using PapaPersonas.Core.Database;
using PapaPersonas.Infrastructure.Database;

namespace PapaPersonas.Tests.Database;

public sealed class DuckDbBootstrapperTests
{
    private const int TargetSchemaVersion = 5;
    private const int LegacyBackfillCohortSize = 819530;

    [Fact]
    public void Initialize_FreshDatabase_CreatesExpectedTablesAndTargetSchemaVersion()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "nested", "PapaPersonas.duckdb");

        try
        {
            var bootstrapper = new DuckDbBootstrapper();

            var result = bootstrapper.Initialize(databasePath);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(DatabaseBootstrapFailureCode.None, result.FailureCode);
            Assert.True(File.Exists(databasePath));

            using var connection = OpenConnection(databasePath);

            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(connection));
            Assert.True(TableExists(connection, "personas"));
            Assert.True(TableExists(connection, "import_runs"));
            Assert.True(TableExists(connection, "personas_staging"));
            Assert.True(TableExists(connection, "stock_headers"));
            Assert.True(TableExists(connection, "stock_members"));
            Assert.True(ColumnExists(connection, "personas", "anio"));
            Assert.True(ColumnExists(connection, "personas", "fecha_importacion"));
            Assert.True(ColumnExists(connection, "personas", "source_import_id"));
            Assert.True(ColumnExists(connection, "personas", "source_row_number"));
            Assert.True(ColumnExists(connection, "personas_staging", "anio"));
            Assert.True(ColumnExists(connection, "import_runs", "source_columns_present_json"));
            Assert.True(ColumnExists(connection, "import_runs", "fecha_importacion"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "data", "PapaPersonas.duckdb");

        try
        {
            var bootstrapper = new DuckDbBootstrapper();

            var firstResult = bootstrapper.Initialize(databasePath);
            var secondResult = bootstrapper.Initialize(databasePath);

            Assert.True(firstResult.IsSuccess, firstResult.Message);
            Assert.True(secondResult.IsSuccess, secondResult.Message);

            using var connection = OpenConnection(databasePath);

            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(connection));
            Assert.Equal(TargetSchemaVersion, GetSchemaVersionRowCount(connection));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public async Task Initialize_ConcurrentCallsInSameProcess_AreSafeAndIdempotent()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "concurrency", "PapaPersonas.duckdb");

        try
        {
            var bootstrapper = new DuckDbBootstrapper();
            var tasks = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => bootstrapper.Initialize(databasePath)))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.All(results, r => Assert.True(r.IsSuccess, r.Message));

            using var connection = OpenConnection(databasePath);
            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(connection));
            Assert.Equal(TargetSchemaVersion, GetSchemaVersionRowCount(connection));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_RejectsSchemaVersionAboveApplicationSupport()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "too-new", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using (var connection = OpenConnection(databasePath))
            {
                ExecuteNonQuery(connection, "DELETE FROM schema_metadata;");
                ExecuteNonQuery(connection, "INSERT INTO schema_metadata (schema_version) VALUES (999);");
            }

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseBootstrapFailureCode.UnsupportedSchemaVersion, result.FailureCode);
            Assert.StartsWith(
                "No se pudo inicializar la base de datos. Detalle técnico: ",
                result.Message,
                StringComparison.Ordinal);
            Assert.Equal(
                "No se pudo inicializar la base de datos. Detalle técnico: La versión del esquema 999 no está admitida. La versión admitida es 5.",
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
    public void Initialize_RejectsSupportedVersionMissingRequiredTable()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "missing-table", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using (var connection = OpenConnection(databasePath))
            {
                ExecuteNonQuery(connection, "DROP TABLE stock_members;");
            }

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.False(result.IsSuccess);
            Assert.Equal(DatabaseBootstrapFailureCode.Other, result.FailureCode);
            Assert.Contains("Falta la tabla obligatoria 'stock_members'", result.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_RejectsSupportedVersionMissingRequiredColumn()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "missing-column", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using (var connection = OpenConnection(databasePath))
            {
                ExecuteNonQuery(
                    connection,
                    """
                    DROP TABLE personas;
                    CREATE TABLE personas (
                        cuil VARCHAR PRIMARY KEY
                    );
                    """);
            }

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.False(result.IsSuccess);
            Assert.Contains("Falta la columna obligatoria 'personas.dni'", result.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Theory]
    [MemberData(nameof(SchemaContractMutationCases))]
    public void Initialize_RejectsBrokenVersionFiveSchema(string mutationSql, string expectedMessageFragment)
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "schema-contract", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using (var connection = OpenConnection(databasePath))
            {
                ExecuteNonQuery(connection, mutationSql);
            }

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.False(result.IsSuccess, result.Message);
            Assert.Contains(expectedMessageFragment, result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_RejectsUnexpectedForeignKeyOnPersonasStaging()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "extra-fk", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using (var connection = OpenConnection(databasePath))
            {
                ExecuteNonQuery(
                    connection,
                    """
                    DROP TABLE personas_staging;
                    CREATE TABLE personas_staging (
                        import_id UUID NOT NULL,
                        source_row_number BIGINT NOT NULL,
                        cuil VARCHAR,
                        dni VARCHAR,
                        fecha_nacimiento DATE,
                        sexo VARCHAR,
                        tipo_dni VARCHAR,
                        apellido VARCHAR,
                        nombre VARCHAR,
                        direccion VARCHAR,
                        codigo_postal VARCHAR,
                        localidad VARCHAR,
                        partido VARCHAR,
                        provincia VARCHAR,
                        nacionalidad VARCHAR,
                        telefono_fijo_1 VARCHAR,
                        telefono_fijo_2 VARCHAR,
                        telefono_fijo_3 VARCHAR,
                        telefono_fijo_4 VARCHAR,
                        telefono_fijo_5 VARCHAR,
                        celular_1 VARCHAR,
                        celular_2 VARCHAR,
                        celular_3 VARCHAR,
                        celular_4 VARCHAR,
                        celular_5 VARCHAR,
                        whatsapp_1 VARCHAR,
                        whatsapp_2 VARCHAR,
                        whatsapp_3 VARCHAR,
                        whatsapp_4 VARCHAR,
                        whatsapp_5 VARCHAR,
                        email_1 VARCHAR,
                        email_2 VARCHAR,
                        email_3 VARCHAR,
                        email_4 VARCHAR,
                        email_5 VARCHAR,
                        codigo_obra_social VARCHAR,
                        obra_social VARCHAR,
                        cuit_empleador VARCHAR,
                        edad SMALLINT,
                        anio SMALLINT,
                        fecha_actualizacion TIMESTAMP,
                        validation_outcome VARCHAR NOT NULL DEFAULT 'pending',
                        validation_error VARCHAR,
                        CHECK (validation_outcome IN ('pending', 'valid', 'rejected')),
                        PRIMARY KEY (import_id, source_row_number),
                        FOREIGN KEY (import_id) REFERENCES import_runs (import_id),
                        FOREIGN KEY (cuil) REFERENCES personas (cuil)
                    );
                    """);
            }

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.False(result.IsSuccess, result.Message);
            Assert.Contains("FOREIGN KEY inesperada", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_RejectsMissingExpectedCheckConstraint()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "missing-check", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using (var connection = OpenConnection(databasePath))
            {
                ExecuteNonQuery(
                    connection,
                    """
                    DROP TABLE stock_members;
                    CREATE TABLE stock_members (
                        stock_id UUID NOT NULL,
                        cuil VARCHAR NOT NULL,
                        codigo_obra_social VARCHAR,
                        obra_social VARCHAR,
                        source_order BIGINT NOT NULL,
                        vendido BOOLEAN NOT NULL DEFAULT FALSE,
                        fecha_venta TIMESTAMP,
                        extraction_token UUID,
                        PRIMARY KEY (stock_id, cuil),
                        FOREIGN KEY (stock_id) REFERENCES stock_headers (stock_id)
                    );
                    """);
            }

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.False(result.IsSuccess, result.Message);
            Assert.Contains("Falta la restricción CHECK", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_RejectsUnexpectedCheckConstraint()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "extra-check", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using (var connection = OpenConnection(databasePath))
            {
                ExecuteNonQuery(
                    connection,
                    """
                    DROP TABLE personas_staging;
                    CREATE TABLE personas_staging (
                        import_id UUID NOT NULL,
                        source_row_number BIGINT NOT NULL,
                        cuil VARCHAR,
                        dni VARCHAR,
                        fecha_nacimiento DATE,
                        sexo VARCHAR,
                        tipo_dni VARCHAR,
                        apellido VARCHAR,
                        nombre VARCHAR,
                        direccion VARCHAR,
                        codigo_postal VARCHAR,
                        localidad VARCHAR,
                        partido VARCHAR,
                        provincia VARCHAR,
                        nacionalidad VARCHAR,
                        telefono_fijo_1 VARCHAR,
                        telefono_fijo_2 VARCHAR,
                        telefono_fijo_3 VARCHAR,
                        telefono_fijo_4 VARCHAR,
                        telefono_fijo_5 VARCHAR,
                        celular_1 VARCHAR,
                        celular_2 VARCHAR,
                        celular_3 VARCHAR,
                        celular_4 VARCHAR,
                        celular_5 VARCHAR,
                        whatsapp_1 VARCHAR,
                        whatsapp_2 VARCHAR,
                        whatsapp_3 VARCHAR,
                        whatsapp_4 VARCHAR,
                        whatsapp_5 VARCHAR,
                        email_1 VARCHAR,
                        email_2 VARCHAR,
                        email_3 VARCHAR,
                        email_4 VARCHAR,
                        email_5 VARCHAR,
                        codigo_obra_social VARCHAR,
                        obra_social VARCHAR,
                        cuit_empleador VARCHAR,
                        edad SMALLINT,
                        anio SMALLINT,
                        fecha_actualizacion TIMESTAMP,
                        validation_outcome VARCHAR NOT NULL DEFAULT 'pending',
                        validation_error VARCHAR,
                        CHECK (validation_outcome IN ('pending', 'valid', 'rejected')),
                        CHECK (source_row_number > 0),
                        PRIMARY KEY (import_id, source_row_number),
                        FOREIGN KEY (import_id) REFERENCES import_runs (import_id)
                    );
                    """);
            }

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.False(result.IsSuccess, result.Message);
            Assert.Contains("restricción CHECK inesperada", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_ValidVersionFiveSchema_IsAccepted()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "valid-schema", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.True(result.IsSuccess, result.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_PriorVersionDatabase_MigratesSuccessfully()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "migrate", "PapaPersonas.duckdb");

        try
        {
            SeedVersionOneDatabase(databasePath);

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.True(result.IsSuccess, result.Message);

            using var connection = OpenConnection(databasePath);

            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(connection));
            Assert.True(TableExists(connection, "personas"));
            Assert.True(TableExists(connection, "import_runs"));
            Assert.True(TableExists(connection, "personas_staging"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_Version2Database_MigratesToVersion3WithoutDataLoss()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "migrate-v2", "PapaPersonas.duckdb");

        try
        {
            SeedVersionTwoDatabase(databasePath);

            using (var seededConnection = OpenConnection(databasePath))
            {
                InsertImportRun(seededConnection, Guid.Parse("11111111-1111-1111-1111-111111111111"));
                ExecuteNonQuery(
                    seededConnection,
                    "INSERT INTO personas (cuil, fecha_actualizacion) VALUES ('00999999999', $fecha);",
                    new DuckDBParameter("fecha", DateTime.UtcNow));
            }

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.True(result.IsSuccess, result.Message);

            using var connection = OpenConnection(databasePath);
            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(connection));
            Assert.Equal(1, ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM personas WHERE cuil = '00999999999';"));
            Assert.Equal(
                1,
                ExecuteScalar<int>(
                    connection,
                    "SELECT COUNT(*) FROM import_runs WHERE import_id = $id;",
                    new DuckDBParameter("id", Guid.Parse("11111111-1111-1111-1111-111111111111"))));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_Version3Database_LegacyCohort_BackfillsNullRowsWithoutOverwritingAndIsIdempotent()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "migrate-v3", "PapaPersonas.duckdb");

        try
        {
            SeedVersionThreeDatabase(databasePath);

            using (var seededConnection = OpenConnection(databasePath))
            {
                InsertLegacyCohortPersonas(seededConnection, LegacyBackfillCohortSize);
            }

            var bootstrapper = new DuckDbBootstrapper();
            var firstResult = bootstrapper.Initialize(databasePath);
            var secondResult = bootstrapper.Initialize(databasePath);

            Assert.True(firstResult.IsSuccess, firstResult.Message);
            Assert.True(secondResult.IsSuccess, secondResult.Message);

            using var connection = OpenConnection(databasePath);
            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(connection));
            Assert.True(ColumnExists(connection, "personas", "fecha_importacion"));
            Assert.True(ColumnExists(connection, "import_runs", "fecha_importacion"));

            Assert.Equal(
                LegacyBackfillCohortSize,
                ExecuteScalar<int>(
                    connection,
                    "SELECT COUNT(*) FROM personas WHERE fecha_importacion = DATE '2026-08-10';"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_Version3Database_NonLegacyOrReplayMetadata_DoesNotBackfillMixedNullRows()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "migrate-v3-idempotent", "PapaPersonas.duckdb");

        try
        {
            SeedVersionThreeDatabase(databasePath);

            using (var seededConnection = OpenConnection(databasePath))
            {
                ExecuteNonQuery(seededConnection, "ALTER TABLE personas ADD COLUMN IF NOT EXISTS fecha_importacion DATE;");

                ExecuteNonQuery(
                    seededConnection,
                    "INSERT INTO personas (cuil, fecha_actualizacion, fecha_importacion) VALUES ('20000000001', TIMESTAMP '2024-01-01 00:00:00', NULL);");

                ExecuteNonQuery(
                    seededConnection,
                    "INSERT INTO personas (cuil, fecha_actualizacion, fecha_importacion) VALUES ('20000000002', TIMESTAMP '2024-01-01 00:00:00', DATE '2024-05-20');");
            }

            var bootstrapper = new DuckDbBootstrapper();
            var firstResult = bootstrapper.Initialize(databasePath);

            Assert.True(firstResult.IsSuccess, firstResult.Message);

            using (var seededConnection2 = OpenConnection(databasePath))
            {
                ExecuteNonQuery(seededConnection2, "DELETE FROM schema_metadata WHERE schema_version = 4;");
            }

            var secondResult = bootstrapper.Initialize(databasePath);
            Assert.True(secondResult.IsSuccess, secondResult.Message);

            using var connection = OpenConnection(databasePath);
            Assert.Equal(TargetSchemaVersion, GetCurrentSchemaVersion(connection));

            Assert.Equal(
                1,
                ExecuteScalar<int>(
                    connection,
                    "SELECT COUNT(*) FROM personas WHERE fecha_importacion IS NULL;"));

            Assert.Equal(
                "2024-05-20",
                ExecuteScalar<string>(
                    connection,
                    "SELECT CAST(fecha_importacion AS VARCHAR) FROM personas WHERE cuil = '20000000002';"));

            Assert.Equal(
                0,
                ExecuteScalar<int>(
                    connection,
                    "SELECT COUNT(*) FROM personas WHERE fecha_importacion = DATE '2026-08-10';"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Initialize_FailedMigration_RollsBackAndVersionDoesNotAdvance()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "rollback", "PapaPersonas.duckdb");

        try
        {
            SeedVersionOneDatabaseWithConflictingImportRunsView(databasePath);

            var bootstrapper = new DuckDbBootstrapper();
            var result = bootstrapper.Initialize(databasePath);

            Assert.False(result.IsSuccess);

            using var connection = OpenConnection(databasePath);
            Assert.Equal(1, GetCurrentSchemaVersion(connection));
            Assert.False(TableExists(connection, "personas"));
            Assert.False(TableExists(connection, "personas_staging"));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Personas_PrimaryKey_RejectsDuplicateCuil()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "pk", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using var connection = OpenConnection(databasePath);

            InsertPersonaWithCuil(connection, "00000000001");

            Assert.ThrowsAny<Exception>(() => InsertPersonaWithCuil(connection, "00000000001"));

            var count = ExecuteScalar<int>(
                connection,
                "SELECT COUNT(*) FROM personas WHERE cuil = $cuil;",
                new DuckDBParameter("cuil", "00000000001"));

            Assert.Equal(1, count);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Staging_AllowsRowWithoutCuil()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "staging", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using var connection = OpenConnection(databasePath);

            var importId = Guid.NewGuid();
            InsertImportRun(connection, importId);

            ExecuteNonQuery(
                connection,
                """
                INSERT INTO personas_staging (import_id, source_row_number, cuil, validation_outcome, validation_error)
                VALUES ($importId, 10, NULL, 'rejected', 'CUIL missing');
                """,
                new DuckDBParameter("importId", importId));

            var cuilIsNull = ExecuteScalar<int>(
                connection,
                "SELECT COUNT(*) FROM personas_staging WHERE import_id = $importId AND cuil IS NULL;",
                new DuckDBParameter("importId", importId));

            Assert.Equal(1, cuilIsNull);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Staging_ForeignKey_RejectsUnknownImportId()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "staging-fk", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using var connection = OpenConnection(databasePath);
            var missingImportId = Guid.NewGuid();

            Assert.ThrowsAny<Exception>(() =>
                ExecuteNonQuery(
                    connection,
                    """
                    INSERT INTO personas_staging (import_id, source_row_number, cuil, validation_outcome)
                    VALUES ($importId, 1, '00123456789', 'valid');
                    """,
                    new DuckDBParameter("importId", missingImportId)));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Personas_IdentifierFields_PreserveLeadingZeros()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "formats", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using var connection = OpenConnection(databasePath);

            ExecuteNonQuery(
                connection,
                """
                INSERT INTO personas (
                    cuil,
                    dni,
                    codigo_postal,
                    celular_1,
                    cuit_empleador,
                    fecha_actualizacion
                )
                VALUES ($cuil, $dni, $codigoPostal, $celular, $cuitEmpleador, $fechaActualizacion);
                """,
                new DuckDBParameter("cuil", "00123456789"),
                new DuckDBParameter("dni", "00001234"),
                new DuckDBParameter("codigoPostal", "0001"),
                new DuckDBParameter("celular", "01100000000"),
                new DuckDBParameter("cuitEmpleador", "00000000001"),
                new DuckDBParameter("fechaActualizacion", DateTime.UtcNow));

            using var query = connection.CreateCommand();
            query.CommandText = """
                SELECT cuil, dni, codigo_postal, celular_1, cuit_empleador
                FROM personas
                WHERE cuil = '00123456789';
                """;

            using var reader = query.ExecuteReader();
            Assert.True(reader.Read());

            Assert.Equal("00123456789", reader.GetString(0));
            Assert.Equal("00001234", reader.GetString(1));
            Assert.Equal("0001", reader.GetString(2));
            Assert.Equal("01100000000", reader.GetString(3));
            Assert.Equal("00000000001", reader.GetString(4));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void Personas_Anio_RoundTrips()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "anio", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using var connection = OpenConnection(databasePath);
            ExecuteNonQuery(
                connection,
                """
                INSERT INTO personas (cuil, anio, fecha_actualizacion)
                VALUES ('00777777777', 2024, $fechaActualizacion);
                """,
                new DuckDBParameter("fechaActualizacion", DateTime.UtcNow));

            var anio = ExecuteScalar<int>(
                connection,
                "SELECT anio FROM personas WHERE cuil = '00777777777';");

            Assert.Equal(2024, anio);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void ImportRuns_SourceColumnsPresentJson_RoundTrips()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "source-columns", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using var connection = OpenConnection(databasePath);
            var importId = Guid.NewGuid();
            const string sourceColumnsJson = "[\"CUIL\",\"APELLIDO\",\"ANIO\"]";

            ExecuteNonQuery(
                connection,
                """
                INSERT INTO import_runs (
                    import_id,
                    stage_type,
                    source_file_name,
                    source_file_path,
                    status,
                    source_columns_present_json)
                VALUES ($importId, $stageType, $fileName, $filePath, 'pending', $sourceColumnsJson);
                """,
                new DuckDBParameter("importId", importId),
                new DuckDBParameter("stageType", "sergio_return"),
                new DuckDBParameter("fileName", "synthetic_return.xlsx"),
                new DuckDBParameter("filePath", "C:/synthetic/synthetic_return.xlsx"),
                new DuckDBParameter("sourceColumnsJson", sourceColumnsJson));

            var roundTrip = ExecuteScalar<string>(
                connection,
                "SELECT source_columns_present_json FROM import_runs WHERE import_id = $importId;",
                new DuckDBParameter("importId", importId));

            Assert.Equal(sourceColumnsJson, roundTrip);
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    [Fact]
    public void StockTables_Constraints_RejectInvalidState_AndAcceptValidRows()
    {
        var testDirectory = CreateUniqueTemporaryDirectory();
        var databasePath = Path.Combine(testDirectory, "stock-constraints", "PapaPersonas.duckdb");

        try
        {
            InitializeDatabase(databasePath);

            using var connection = OpenConnection(databasePath);
            var stockId = Guid.NewGuid();
            var extractionToken = Guid.NewGuid();

            ExecuteNonQuery(
                connection,
                """
                INSERT INTO stock_headers (
                    stock_id,
                    source_fecha_importacion,
                    source_import_id,
                    pending_extraction_token,
                    pending_started_utc,
                    pending_output_path,
                    pending_expected_rows,
                    pending_selected_columns_json)
                VALUES ($stockId, DATE '2026-08-10', $sourceImportId, $token, CURRENT_TIMESTAMP, 'C:/tmp/out.csv', 1, '["CUIL","APELLIDO","CODIGOOS","OBRASOCIAL"]');
                """,
                new DuckDBParameter("stockId", stockId),
                new DuckDBParameter("sourceImportId", Guid.NewGuid()),
                new DuckDBParameter("token", extractionToken));

            ExecuteNonQuery(
                connection,
                """
                INSERT INTO stock_members (stock_id, cuil, codigo_obra_social, obra_social, source_order, vendido, fecha_venta, extraction_token)
                VALUES ($stockId, '20123456789', '123', 'OS TEST', 1, FALSE, NULL, NULL);
                """,
                new DuckDBParameter("stockId", stockId));

            ExecuteNonQuery(
                connection,
                """
                INSERT INTO stock_members (stock_id, cuil, source_order, vendido, fecha_venta, extraction_token)
                VALUES ($stockId, '20987654321', 2, TRUE, CURRENT_TIMESTAMP, $token);
                """,
                new DuckDBParameter("stockId", stockId),
                new DuckDBParameter("token", extractionToken));

            Assert.ThrowsAny<Exception>(() =>
                ExecuteNonQuery(
                    connection,
                    """
                    INSERT INTO stock_headers (
                        stock_id,
                        source_fecha_importacion,
                        pending_extraction_token)
                    VALUES ($stockId, DATE '2026-08-10', $token);
                    """,
                    new DuckDBParameter("stockId", Guid.NewGuid()),
                    new DuckDBParameter("token", Guid.NewGuid())));

            Assert.ThrowsAny<Exception>(() =>
                ExecuteNonQuery(
                    connection,
                    """
                    INSERT INTO stock_headers (
                        stock_id,
                        source_fecha_importacion,
                        pending_extraction_token,
                        pending_selected_columns_json,
                        pending_started_utc,
                        pending_output_path,
                        pending_expected_rows)
                    VALUES ($stockId, DATE '2026-08-10', $token, '["CUIL"]', CURRENT_TIMESTAMP, 'C:/tmp/out.csv', -1);
                    """,
                    new DuckDBParameter("stockId", Guid.NewGuid()),
                    new DuckDBParameter("token", Guid.NewGuid())));

            Assert.ThrowsAny<Exception>(() =>
                ExecuteNonQuery(
                    connection,
                    """
                    INSERT INTO stock_headers (
                        stock_id,
                        source_fecha_importacion,
                        pending_extraction_token,
                        pending_started_utc,
                        pending_output_path,
                        pending_expected_rows,
                        pending_selected_columns_json)
                    VALUES ($stockId, DATE '2026-08-10', $token, CURRENT_TIMESTAMP, 'C:/tmp/out.csv', 1, NULL);
                    """,
                    new DuckDBParameter("stockId", Guid.NewGuid()),
                    new DuckDBParameter("token", Guid.NewGuid())));

            Assert.ThrowsAny<Exception>(() =>
                ExecuteNonQuery(
                    connection,
                    """
                    INSERT INTO stock_members (stock_id, cuil, source_order, vendido, fecha_venta)
                    VALUES ($stockId, '20000000001', 3, FALSE, CURRENT_TIMESTAMP);
                    """,
                    new DuckDBParameter("stockId", stockId)));

            Assert.ThrowsAny<Exception>(() =>
                ExecuteNonQuery(
                    connection,
                    """
                    INSERT INTO stock_members (stock_id, cuil, source_order, vendido, fecha_venta)
                    VALUES ($stockId, '20000000002', 4, TRUE, NULL);
                    """,
                    new DuckDBParameter("stockId", stockId)));

            Assert.Equal(1, ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM stock_headers WHERE stock_id = $stockId;", new DuckDBParameter("stockId", stockId)));
            Assert.Equal(2, ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM stock_members WHERE stock_id = $stockId;", new DuckDBParameter("stockId", stockId)));
        }
        finally
        {
            DeleteDirectoryIfExists(testDirectory);
        }
    }

    private static string CreateUniqueTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        const int maxAttempts = 5;
        var delay = TimeSpan.FromMilliseconds(50);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(delay);
                delay += delay;
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(delay);
                delay += delay;
            }
        }

        throw new IOException($"Failed to clean test directory after {maxAttempts} attempts: {path}");
    }

    private static string BuildConnectionString(string databasePath)
    {
        var builder = new DuckDBConnectionStringBuilder
        {
            DataSource = databasePath
        };

        return builder.ConnectionString;
    }

    private static DuckDBConnection OpenConnection(string databasePath)
    {
        var connection = new DuckDBConnection(BuildConnectionString(databasePath));
        connection.Open();
        return connection;
    }

    private static void SeedVersionOneDatabase(string databasePath)
    {
        var parent = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Test database path must include a parent directory.");

        Directory.CreateDirectory(parent);

        using var connection = OpenConnection(databasePath);

        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS schema_metadata (
                schema_version INTEGER PRIMARY KEY,
                initialized_utc TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            INSERT INTO schema_metadata (schema_version) VALUES (1);
            """);
    }

    private static void SeedVersionTwoDatabase(string databasePath)
    {
        SeedVersionOneDatabase(databasePath);

        using var connection = OpenConnection(databasePath);
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS personas (
                cuil VARCHAR PRIMARY KEY,
                dni VARCHAR,
                fecha_nacimiento DATE,
                sexo VARCHAR,
                tipo_dni VARCHAR,
                apellido VARCHAR,
                nombre VARCHAR,
                direccion VARCHAR,
                codigo_postal VARCHAR,
                localidad VARCHAR,
                partido VARCHAR,
                provincia VARCHAR,
                nacionalidad VARCHAR,
                telefono_fijo_1 VARCHAR,
                telefono_fijo_2 VARCHAR,
                telefono_fijo_3 VARCHAR,
                telefono_fijo_4 VARCHAR,
                telefono_fijo_5 VARCHAR,
                celular_1 VARCHAR,
                celular_2 VARCHAR,
                celular_3 VARCHAR,
                celular_4 VARCHAR,
                celular_5 VARCHAR,
                whatsapp_1 VARCHAR,
                whatsapp_2 VARCHAR,
                whatsapp_3 VARCHAR,
                whatsapp_4 VARCHAR,
                whatsapp_5 VARCHAR,
                email_1 VARCHAR,
                email_2 VARCHAR,
                email_3 VARCHAR,
                email_4 VARCHAR,
                email_5 VARCHAR,
                codigo_obra_social VARCHAR,
                obra_social VARCHAR,
                cuit_empleador VARCHAR,
                edad SMALLINT,
                fecha_actualizacion TIMESTAMP NOT NULL
            );

            CREATE TABLE IF NOT EXISTS import_runs (
                import_id UUID PRIMARY KEY,
                stage_type VARCHAR NOT NULL,
                source_file_name VARCHAR NOT NULL,
                source_file_path VARCHAR NOT NULL,
                status VARCHAR NOT NULL DEFAULT 'pending',
                started_utc TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                analyzed_utc TIMESTAMP,
                completed_utc TIMESTAMP,
                total_rows BIGINT NOT NULL DEFAULT 0,
                valid_rows BIGINT NOT NULL DEFAULT 0,
                rejected_rows BIGINT NOT NULL DEFAULT 0,
                missing_cuil_rows BIGINT NOT NULL DEFAULT 0,
                duplicate_cuil_rows BIGINT NOT NULL DEFAULT 0,
                malformed_cuil_rows BIGINT NOT NULL DEFAULT 0,
                rows_to_insert BIGINT NOT NULL DEFAULT 0,
                rows_to_update BIGINT NOT NULL DEFAULT 0,
                error_message VARCHAR,
                CHECK (stage_type IN ('hernan_raw', 'sergio_return')),
                CHECK (status IN ('pending', 'analyzing', 'ready_for_confirmation', 'applying', 'completed', 'failed'))
            );

            CREATE TABLE IF NOT EXISTS personas_staging (
                import_id UUID NOT NULL,
                source_row_number BIGINT NOT NULL,
                cuil VARCHAR,
                dni VARCHAR,
                fecha_nacimiento DATE,
                sexo VARCHAR,
                tipo_dni VARCHAR,
                apellido VARCHAR,
                nombre VARCHAR,
                direccion VARCHAR,
                codigo_postal VARCHAR,
                localidad VARCHAR,
                partido VARCHAR,
                provincia VARCHAR,
                nacionalidad VARCHAR,
                telefono_fijo_1 VARCHAR,
                telefono_fijo_2 VARCHAR,
                telefono_fijo_3 VARCHAR,
                telefono_fijo_4 VARCHAR,
                telefono_fijo_5 VARCHAR,
                celular_1 VARCHAR,
                celular_2 VARCHAR,
                celular_3 VARCHAR,
                celular_4 VARCHAR,
                celular_5 VARCHAR,
                whatsapp_1 VARCHAR,
                whatsapp_2 VARCHAR,
                whatsapp_3 VARCHAR,
                whatsapp_4 VARCHAR,
                whatsapp_5 VARCHAR,
                email_1 VARCHAR,
                email_2 VARCHAR,
                email_3 VARCHAR,
                email_4 VARCHAR,
                email_5 VARCHAR,
                codigo_obra_social VARCHAR,
                obra_social VARCHAR,
                cuit_empleador VARCHAR,
                edad SMALLINT,
                fecha_actualizacion TIMESTAMP,
                validation_outcome VARCHAR NOT NULL DEFAULT 'pending',
                validation_error VARCHAR,
                CHECK (validation_outcome IN ('pending', 'valid', 'rejected')),
                PRIMARY KEY (import_id, source_row_number),
                FOREIGN KEY (import_id) REFERENCES import_runs (import_id)
            );

            INSERT INTO schema_metadata (schema_version) VALUES (2);
            """);
    }

    private static void SeedVersionThreeDatabase(string databasePath)
    {
        SeedVersionTwoDatabase(databasePath);

        using var connection = OpenConnection(databasePath);
        ExecuteNonQuery(
            connection,
            """
            ALTER TABLE personas
            ADD COLUMN IF NOT EXISTS anio SMALLINT;

            ALTER TABLE personas_staging
            ADD COLUMN IF NOT EXISTS anio SMALLINT;

            ALTER TABLE import_runs
            ADD COLUMN IF NOT EXISTS source_columns_present_json VARCHAR;

            UPDATE import_runs
            SET source_columns_present_json = '[]'
            WHERE source_columns_present_json IS NULL;

            INSERT INTO schema_metadata (schema_version) VALUES (3);
            """);
    }

    private static void SeedVersionOneDatabaseWithConflictingImportRunsView(string databasePath)
    {
        SeedVersionOneDatabase(databasePath);

        using var connection = OpenConnection(databasePath);
        ExecuteNonQuery(connection, "CREATE VIEW import_runs AS SELECT 1 AS fake_col;");
    }

    private static void InitializeDatabase(string databasePath)
    {
        var bootstrapper = new DuckDbBootstrapper();
        var result = bootstrapper.Initialize(databasePath);
        Assert.True(result.IsSuccess, result.Message);
    }

    private static void InsertImportRun(DuckDBConnection connection, Guid importId)
    {
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO import_runs (import_id, stage_type, source_file_name, source_file_path, status)
            VALUES ($importId, $stageType, $fileName, $filePath, 'pending');
            """,
            new DuckDBParameter("importId", importId),
            new DuckDBParameter("stageType", "hernan_raw"),
            new DuckDBParameter("fileName", "synthetic_source.xlsx"),
            new DuckDBParameter("filePath", "C:/synthetic/synthetic_source.xlsx"));
    }

    private static void InsertPersonaWithCuil(DuckDBConnection connection, string cuil)
    {
        ExecuteNonQuery(
            connection,
            "INSERT INTO personas (cuil, fecha_actualizacion) VALUES ($cuil, $fecha);",
            new DuckDBParameter("cuil", cuil),
            new DuckDBParameter("fecha", DateTime.UtcNow));
    }

    private static void InsertLegacyCohortPersonas(DuckDBConnection connection, int rowCount)
    {
        ExecuteNonQuery(
            connection,
            """
            INSERT INTO personas (cuil, fecha_actualizacion)
            SELECT LPAD(CAST(i AS VARCHAR), 11, '0') AS cuil,
                   TIMESTAMP '2024-01-01 00:00:00' AS fecha_actualizacion
            FROM range(1, $rowCount + 1) AS t(i);
            """,
            new DuckDBParameter("rowCount", rowCount));
    }

    private static int GetCurrentSchemaVersion(DuckDBConnection connection)
    {
        return ExecuteScalar<int>(connection, "SELECT COALESCE(MAX(schema_version), 0) FROM schema_metadata;");
    }

    private static int GetSchemaVersionRowCount(DuckDBConnection connection)
    {
        return ExecuteScalar<int>(connection, "SELECT COUNT(*) FROM schema_metadata;");
    }

    private static bool TableExists(DuckDBConnection connection, string tableName)
    {
        return ExecuteScalar<int>(
            connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'main' AND table_name = $tableName;",
            new DuckDBParameter("tableName", tableName)) == 1;
    }

    private static bool ColumnExists(DuckDBConnection connection, string tableName, string columnName)
    {
        return ExecuteScalar<int>(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'main'
              AND table_name = $tableName
              AND column_name = $columnName;
            """,
            new DuckDBParameter("tableName", tableName),
            new DuckDBParameter("columnName", columnName)) == 1;
    }

    public static IEnumerable<object[]> SchemaContractMutationCases()
    {
        yield return
            new object[]
            {
                """
                DROP TABLE personas;
                CREATE TABLE personas (
                    cuil VARCHAR PRIMARY KEY
                );
                """,
                "Falta la columna obligatoria 'personas.dni'"
            };

        yield return
            new object[]
            {
                """
                DROP TABLE personas;
                CREATE TABLE personas (
                    cuil VARCHAR PRIMARY KEY,
                    dni BIGINT
                );
                """,
                "La columna 'personas.dni' tiene tipo"
            };

        yield return
            new object[]
            {
                """
                DROP TABLE personas;
                CREATE TABLE personas (
                    cuil VARCHAR,
                    dni VARCHAR
                );
                """,
                "La nulabilidad de 'personas.cuil' es 'NULL'"
            };

        yield return
            new object[]
            {
                """
                DROP TABLE schema_metadata;
                CREATE TABLE schema_metadata (
                    schema_version INTEGER NOT NULL,
                    initialized_utc TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """,
                "La tabla de metadatos debe tener PRIMARY KEY (schema_version)."
            };

        yield return
            new object[]
            {
                """
                DROP TABLE stock_members;
                CREATE TABLE stock_members (
                    stock_id UUID NOT NULL,
                    cuil VARCHAR NOT NULL,
                    codigo_obra_social VARCHAR,
                    obra_social VARCHAR,
                    source_order BIGINT NOT NULL,
                    vendido BOOLEAN NOT NULL DEFAULT FALSE,
                    fecha_venta TIMESTAMP,
                    extraction_token UUID,
                    PRIMARY KEY (stock_id, cuil),
                    CHECK (
                        (vendido = FALSE AND fecha_venta IS NULL)
                        OR
                        (vendido = TRUE AND fecha_venta IS NOT NULL)
                    )
                );
                """,
                "Falta la FOREIGN KEY"
            };
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
        return (T)Convert.ChangeType(value!, typeof(T));
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
}
