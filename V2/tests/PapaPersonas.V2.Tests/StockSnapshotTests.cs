using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.V2.Shell;
using Xunit;

namespace PapaPersonas.V2.Tests;

public sealed class StockSnapshotTests
{
    [Fact]
    public void GenerateCopiesOfficialColumnsAndPrimaryKeyWithoutChangingSergio()
    {
        using var workspace = new StockWorkspace();
        var loadedAt = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        SeedOfficial(workspace.DatabasePath, "Clave persona", "Código OS", "Descripción OS", loadedAt, "origen.xlsx", 2);

        var result = Service(workspace).Generate(new StockGenerateRequest("Clave persona", "Código OS", "Descripción OS", false));

        Assert.Equal(StockGenerateStatus.Completed, result.Status);
        Assert.Equal(2, result.Summary!.Total);
        using var connection = Open(workspace.DatabasePath);
        Assert.Equal(["Clave persona", "Código OS", "Descripción OS"], ReadColumns(connection, StockNames.DataTable));
        Assert.Equal(1, CountPrimaryKeys(connection, StockNames.DataTable, "Clave persona"));
        Assert.Equal(["member_id", "sold", "extraction_id"], ReadColumns(connection, StockNames.StatusTable));
        Assert.Equal(2, CountRows(connection, SergioLoadPromoter.OfficialTableName));
    }

    [Fact]
    public void ReplacingSergioDoesNotChangeStockAndOnlyWarns()
    {
        using var workspace = new StockWorkspace();
        var loadedAt = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", loadedAt, "origen.xlsx", 2);
        var service = Service(workspace);
        Assert.Equal(StockGenerateStatus.Completed, service.Generate(Request()).Status);

        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "DELETE FROM sergio_current; INSERT INTO sergio_current VALUES ('30', 'ZZZ', 'Nuevo'); UPDATE sergio_load_metadata SET source_file_name = 'nuevo.xlsx', row_count = 1");
        }

        var summary = service.GetSummary();
        using var verify = Open(workspace.DatabasePath);
        Assert.Equal(2, CountRows(verify, StockNames.DataTable));
        Assert.Equal(2, summary!.Total);
        Assert.Contains("informativo", summary.SourceWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("origen.xlsx", summary.Header.SourceFileName);
    }

    [Fact]
    public void MissingOfficialSourceDoesNotBlockExistingStockRead()
    {
        using var workspace = new StockWorkspace();
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", DateTime.UtcNow, "origen.xlsx", 1);
        var service = Service(workspace);
        service.Generate(Request());
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "CHECKPOINT");
            Execute(connection, "DROP TABLE sergio_current");
        }

        var summary = service.GetSummary();

        Assert.NotNull(summary);
        Assert.Equal(1, summary.Total);
        Assert.Contains("sigue operativo", summary.SourceWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingExtractionBlocksRegenerationWithoutChangingRows()
    {
        using var workspace = new StockWorkspace();
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", DateTime.UtcNow, "origen.xlsx", 2);
        var service = Service(workspace);
        service.Generate(Request());
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "INSERT INTO stock_extractions (extraction_id, state) VALUES ('pending-1', 'pending')");
            Execute(connection, "UPDATE stock_status SET sold = TRUE WHERE member_id = '20'");
        }

        var result = service.Generate(Request(confirmed: true) with { CodeAlias = "NOMBRE" });

        Assert.Equal(StockGenerateStatus.ValidationFailed, result.Status);
        Assert.Contains("extracción pendiente", result.Message, StringComparison.OrdinalIgnoreCase);
        using var verify = Open(workspace.DatabasePath);
        Assert.Equal(1, Scalar(verify, "SELECT COUNT(*) FROM stock_status WHERE sold = TRUE"));
        Assert.Equal("CODIGO", ScalarText(verify, "SELECT code_alias FROM stock_headers"));
    }

    [Fact]
    public void ConfirmedRegenerationReplacesOperationalRowsAndPreservesCatalog()
    {
        using var workspace = new StockWorkspace();
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", DateTime.UtcNow, "origen.xlsx", 2);
        var service = Service(workspace);
        service.Generate(Request());
        service.SaveCatalog(new Dictionary<string, string> { ["OSDE"] = "Nombre guardado" });
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "DELETE FROM sergio_current; INSERT INTO sergio_current VALUES ('40', 'osde', 'Otro'); UPDATE sergio_load_metadata SET row_count = 1");
        }

        var result = service.Generate(Request(confirmed: true));

        Assert.Equal(StockGenerateStatus.Completed, result.Status);
        Assert.Equal(1, result.Summary!.Total);
        Assert.Equal("Nombre guardado", result.Summary.Groups.Single(group => group.NormalizedCode == "OSDE").DisplayName);
        Assert.Equal("Nombre guardado", service.GetSummary()!.Groups.Single(group => group.NormalizedCode == "OSDE").DisplayName);
    }

    [Fact]
    public void EmptyOfficialLoadIsRejected()
    {
        using var workspace = new StockWorkspace();
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", DateTime.UtcNow, "origen.xlsx", 0);

        var result = Service(workspace).Generate(Request());

        Assert.Equal(StockGenerateStatus.ValidationFailed, result.Status);
        Assert.Contains("no tiene filas", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, "NOMBRE")]
    [InlineData("", "NOMBRE")]
    [InlineData("   ", "NOMBRE")]
    [InlineData("NO-EXISTE", "NOMBRE")]
    [InlineData("CODIGO", null)]
    [InlineData("CODIGO", "")]
    [InlineData("CODIGO", "NO-EXISTE")]
    public void InvalidCodeOrDescriptionAliasPreservesExistingSnapshot(string? codeAlias, string? descriptionAlias)
    {
        using var workspace = new StockWorkspace();
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", DateTime.UtcNow, "origen.xlsx", 2);
        var service = Service(workspace);
        Assert.Equal(StockGenerateStatus.Completed, service.Generate(Request()).Status);

        var result = service.Generate(new StockGenerateRequest("CUIL", codeAlias!, descriptionAlias!, true));

        Assert.Equal(StockGenerateStatus.ValidationFailed, result.Status);
        using var verify = Open(workspace.DatabasePath);
        Assert.Equal(2, CountRows(verify, StockNames.DataTable));
        Assert.Equal("CODIGO", ScalarText(verify, "SELECT code_alias FROM stock_headers"));
        Assert.Equal("NOMBRE", ScalarText(verify, "SELECT description_alias FROM stock_headers"));
    }

    [Fact]
    public void InvalidAliasOnFirstGenerationCreatesNoStock()
    {
        using var workspace = new StockWorkspace();
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", DateTime.UtcNow, "origen.xlsx", 1);

        var result = Service(workspace).Generate(new StockGenerateRequest("CUIL", "NO-EXISTE", "NOMBRE", false));

        Assert.Equal(StockGenerateStatus.ValidationFailed, result.Status);
        using var verify = Open(workspace.DatabasePath);
        Assert.Equal(0, Scalar(verify, "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'stock_data'"));
    }

    [Fact]
    public void CatalogSaveFailureKeepsCommittedStockUsableWithoutRegeneration()
    {
        using var workspace = new StockWorkspace();
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", DateTime.UtcNow, "origen.xlsx", 1);
        var catalog = new StockCatalogStore(workspace.CatalogDirectory, () => throw new IOException("disco lleno"));
        var service = new StockSnapshotService(new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)), catalog);

        var result = service.Generate(Request());

        Assert.Equal(StockGenerateStatus.Completed, result.Status);
        Assert.Contains("no se pudo guardar el catálogo", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sin regenerar", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, result.Summary!.Total);
        var retry = service.Generate(Request());
        Assert.Equal(StockGenerateStatus.ValidationFailed, retry.Status);
        Assert.Contains("confirmación explícita", retry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(service.GetSummary());
    }

    [Fact]
    public void QuotedUnicodeAliasesRemainALiteralCopy()
    {
        using var workspace = new StockWorkspace();
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "CREATE TABLE sergio_current(" + Quote("Clave \"persona\"") + " VARCHAR, " + Quote("SELECT") + " VARCHAR, " + Quote("Código OS") + " VARCHAR, PRIMARY KEY (" + Quote("Clave \"persona\"") + "))");
            Execute(connection, "INSERT INTO sergio_current VALUES ('20', 'x', 'osde')");
            Execute(connection, "CREATE TABLE sergio_load_metadata(source_file_name VARCHAR, loaded_at_utc TIMESTAMP, row_count BIGINT, selected_primary_key_alias VARCHAR, schema_version VARCHAR)");
            InsertMetadata(connection, "origen.xlsx", DateTime.UtcNow, 1, "Clave \"persona\"");
        }

        var result = Service(workspace).Generate(new StockGenerateRequest("Clave \"persona\"", "Código OS", "SELECT", false));

        Assert.Equal(StockGenerateStatus.Completed, result.Status);
        using var verify = Open(workspace.DatabasePath);
        Assert.Equal(1, CountPrimaryKeys(verify, StockNames.DataTable, "Clave \"persona\""));
        Assert.Equal("x", ScalarText(verify, "SELECT \"SELECT\" FROM stock_data"));
    }

    [Fact]
    public void GetOfficialSourceExposesMetadataIdentityReadOnly()
    {
        using var workspace = new StockWorkspace();
        var loadedAt = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", loadedAt, "origen.xlsx", 2);
        var service = Service(workspace);

        var official = service.GetOfficialSource();

        Assert.True(official.CanGenerate);
        Assert.NotNull(official.Identity);
        Assert.Equal("origen.xlsx", official.Identity.SourceFileName);
        Assert.Equal(loadedAt, official.Identity.LoadedAtUtc);
        Assert.Equal(2, official.Identity.RowCount);
        Assert.Equal("CUIL", official.Identity.PrimaryKeyAlias);
    }

    [Fact]
    public void GetOfficialSourceKeepsIdentityWhenNotReadyToGenerate()
    {
        using var workspace = new StockWorkspace();
        var loadedAt = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", loadedAt, "origen.xlsx", 0);
        var service = Service(workspace);

        var official = service.GetOfficialSource();

        Assert.False(official.CanGenerate);
        Assert.NotNull(official.Identity);
        Assert.Equal("origen.xlsx", official.Identity.SourceFileName);
        Assert.Equal(loadedAt, official.Identity.LoadedAtUtc);
        Assert.Equal(0, official.Identity.RowCount);
    }

    [Fact]
    public void CorruptOfficialMetadataSchemaLeavesSummaryUsableAndReportsUnavailable()
    {
        using var workspace = new StockWorkspace();
        var loadedAt = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", loadedAt, "origen.xlsx", 2);
        var initialService = Service(workspace);
        var initial = initialService.Generate(Request());
        Assert.Equal(StockGenerateStatus.Completed, initial.Status);

        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "DROP TABLE sergio_load_metadata; CREATE TABLE sergio_load_metadata (corrupt_col INT)");
        }

        var service = Service(workspace);

        var summary = service.GetSummary();
        Assert.NotNull(summary);
        Assert.Equal(2, summary.Total);
        Assert.Equal("No se pudo consultar la carga oficial de Sergio. El stock existente sigue operativo.", summary.SourceWarning);

        var officialFailed = false;
        StockOfficialSource? official = null;
        try
        {
            official = service.GetOfficialSource();
        }
        catch
        {
            officialFailed = true;
        }

        Assert.True(officialFailed);
        Assert.Null(official);

        var originModel = StockPresentation.EvaluateOrigin(summary.Header, null, officialUnavailable: true);
        Assert.Equal(StockPresentation.StatusOfficialUnavailable, originModel.StatusText);
        Assert.Equal(StockPresentation.OfficialUnavailableText, originModel.OfficialSourceText);
        Assert.True(originModel.HasStock);
        Assert.Equal("No se pudo consultar la carga oficial de Sergio. El stock existente sigue operativo.", originModel.ExplanatoryText);
    }

    [Fact]
    public void EmptyOfficialMetadataReportsAbsentWarningInsteadOfNewer()
    {
        using var workspace = new StockWorkspace();
        var loadedAt = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        SeedOfficial(workspace.DatabasePath, "CUIL", "CODIGO", "NOMBRE", loadedAt, "origen.xlsx", 2);
        var service = Service(workspace);
        var initial = service.Generate(Request());
        Assert.Equal(StockGenerateStatus.Completed, initial.Status);

        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "DELETE FROM sergio_load_metadata");
        }

        var summary = service.GetSummary();
        Assert.NotNull(summary);
        Assert.Equal(2, summary.Total);
        Assert.Equal("La carga oficial de Sergio ya no está disponible. El stock existente sigue operativo.", summary.SourceWarning);
        Assert.DoesNotContain("reciente", summary.SourceWarning, StringComparison.OrdinalIgnoreCase);
    }

    private static StockSnapshotService Service(StockWorkspace workspace) =>
        new(new SergioStagingAnalyzer(new DuckDbPathPolicy(workspace.DatabasePath)), new StockCatalogStore(workspace.CatalogDirectory));

    private static StockGenerateRequest Request(bool confirmed = false) =>
        new("CUIL", "CODIGO", "NOMBRE", confirmed);

    private static void SeedOfficial(string path, string key, string code, string description, DateTime loadedAt, string file, long metadataCount)
    {
        using var connection = Open(path);
        Execute(connection, $"CREATE TABLE sergio_current({Quote(key)} VARCHAR, {Quote(code)} VARCHAR, {Quote(description)} VARCHAR, PRIMARY KEY ({Quote(key)}))");
        if (metadataCount == 1)
            Execute(connection, "INSERT INTO sergio_current VALUES ('20', 'osde', 'OSDE')");
        else if (metadataCount > 1)
            Execute(connection, "INSERT INTO sergio_current VALUES ('20', 'osde', 'OSDE'), ('21', '   ', '   ')");

        Execute(connection, "CREATE TABLE sergio_load_metadata(source_file_name VARCHAR, loaded_at_utc TIMESTAMP, row_count BIGINT, selected_primary_key_alias VARCHAR, schema_version VARCHAR)");
        InsertMetadata(connection, file, loadedAt, metadataCount, key);
    }

    private static void InsertMetadata(DuckDBConnection connection, string file, DateTime loadedAt, long count, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sergio_load_metadata VALUES (?, ?, ?, ?, NULL)";
        command.Parameters.Add(new DuckDBParameter { Value = file });
        command.Parameters.Add(new DuckDBParameter { Value = loadedAt });
        command.Parameters.Add(new DuckDBParameter { Value = count });
        command.Parameters.Add(new DuckDBParameter { Value = key });
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> ReadColumns(DuckDBConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name = ? ORDER BY ordinal_position";
        command.Parameters.Add(new DuckDBParameter { Value = table });
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read()) columns.Add(reader.GetString(0));
        return columns;
    }

    private static long CountPrimaryKeys(DuckDBConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.table_constraints tc JOIN information_schema.key_column_usage ku ON tc.constraint_name = ku.constraint_name AND tc.table_name = ku.table_name WHERE tc.table_name = ? AND tc.constraint_type = 'PRIMARY KEY' AND ku.column_name = ?";
        command.Parameters.Add(new DuckDBParameter { Value = table });
        command.Parameters.Add(new DuckDBParameter { Value = column });
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long CountRows(DuckDBConnection connection, string table) =>
        Scalar(connection, $"SELECT COUNT(*) FROM {Quote(table)}");

    private static long Scalar(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string ScalarText(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)!;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static void Execute(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static DuckDBConnection Open(string path)
    {
        var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();
        return connection;
    }

    private sealed class StockWorkspace : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PapaPersonasTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(directory, "test.duckdb");
        public string CatalogDirectory => Path.Combine(directory, "catalog");
        public StockWorkspace() => Directory.CreateDirectory(directory);
        public void Dispose() { try { Directory.Delete(directory, true); } catch { } }
    }
}
