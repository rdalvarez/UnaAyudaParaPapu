using System.Globalization;
using System.Text.Json;
using DuckDB.NET.Data;

namespace PapaPersonas.V2.Shell;

public enum StockGenerateStatus
{
    Completed = 0,
    ValidationFailed = 1,
    Failed = 2
}

public enum StockExtractionState
{
    Pending = 0,
    Completed = 1
}

public sealed record StockGenerateRequest(
    string PrimaryKeyAlias,
    string CodeAlias,
    string DescriptionAlias,
    bool ConfirmedRegeneration);

public sealed record StockOfficialSource(
    string? PrimaryKeyAlias,
    IReadOnlyList<string> Columns,
    bool CanGenerate,
    StockSourceIdentity? Identity = null);

public sealed record StockSourceIdentity(
    string SourceFileName,
    DateTime LoadedAtUtc,
    long RowCount,
    string PrimaryKeyAlias);

public sealed record StockHeader(
    string SourceFileName,
    DateTime LoadedAtUtc,
    long OfficialRowCount,
    string PrimaryKeyAlias,
    string CodeAlias,
    string DescriptionAlias,
    DateTime GeneratedUtc);

public sealed record StockGroupSummary(
    string NormalizedCode,
    string DisplayName,
    long Total,
    long Sold,
    long Available,
    long Reserved);

public sealed record StockSummary(
    StockHeader Header,
    long Total,
    long Sold,
    long Available,
    IReadOnlyList<StockGroupSummary> Groups,
    string? SourceWarning,
    IReadOnlyList<string> CatalogWarnings);

public sealed record StockGroupQuantity(string NormalizedCode, int Quantity);

public sealed record StockDetailExportRequest(
    string DestinationCsvPath,
    IReadOnlyList<string> SelectedColumns,
    bool OnlyAvailable);

public sealed record StockExtractionRequest(
    string DestinationCsvPath,
    IReadOnlyList<string> SelectedColumns,
    IReadOnlyList<StockGroupQuantity> Groups,
    string? SaleReference);

public sealed record StockPendingExtraction(
    string ExtractionId,
    string OutputPath,
    long ExpectedRows,
    string? SaleReference);

public sealed record StockCompletedExtraction(
    string ExtractionId,
    string OutputPath,
    long ExpectedRows,
    string? SaleReference,
    DateTime? StartedUtc,
    DateTime? CompletedUtc);

public sealed record StockFileResult(
    StockGenerateStatus Status,
    string? Message,
    string? ExtractionId,
    long RowCount,
    string? OutputPath)
{
    public static StockFileResult Completed(long rowCount, string outputPath, string? extractionId = null) =>
        new(StockGenerateStatus.Completed, null, extractionId, rowCount, outputPath);

    public static StockFileResult ValidationFailed(string message) =>
        new(StockGenerateStatus.ValidationFailed, message, null, 0, null);

    public static StockFileResult Failed(string message, string? extractionId = null) =>
        new(StockGenerateStatus.Failed, message, extractionId, 0, null);
}

public sealed record StockGenerateResult(
    StockGenerateStatus Status,
    string? Message,
    StockSummary? Summary)
{
    public static StockGenerateResult Completed(StockSummary summary, string? warning = null) =>
        new(StockGenerateStatus.Completed, warning, summary);

    public static StockGenerateResult ValidationFailed(string message) =>
        new(StockGenerateStatus.ValidationFailed, message, null);

    public static StockGenerateResult Failed(string message) =>
        new(StockGenerateStatus.Failed, message, null);
}

public sealed record StockCatalogState(
    IReadOnlyDictionary<string, string> NamesByNormalizedCode,
    IReadOnlyList<string> Warnings);

public sealed class StockCatalogStore
{
    public const string RelativePath = "config/stock-obras-sociales.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string baseDirectory;
    private readonly Action? beforeSave;

    public StockCatalogStore(string baseDirectory, Action? beforeSave = null)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("El directorio del catálogo de stock es obligatorio.", nameof(baseDirectory));

        this.baseDirectory = baseDirectory;
        this.beforeSave = beforeSave;
    }

    public string CatalogPath => Path.Combine(baseDirectory, RelativePath);

    public StockCatalogState Load()
    {
        if (!File.Exists(CatalogPath))
            return Empty();

        try
        {
            var payload = JsonSerializer.Deserialize<CatalogPayload>(File.ReadAllText(CatalogPath));
            if (payload?.NamesByNormalizedCode is null)
            {
                return new StockCatalogState(
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    ["La estructura del catálogo de stock no es válida. Se usaron nombres sugeridos."]);
            }

            return new StockCatalogState(Normalize(payload.NamesByNormalizedCode), []);
        }
        catch (Exception exception)
        {
            return new StockCatalogState(
                new Dictionary<string, string>(StringComparer.Ordinal),
                [$"El JSON del catálogo de stock no es válido. Se usaron nombres sugeridos. {exception.Message}"]);
        }
    }

    public void Save(IReadOnlyDictionary<string, string> namesByNormalizedCode)
    {
        ArgumentNullException.ThrowIfNull(namesByNormalizedCode);
        beforeSave?.Invoke();
        var directory = Path.GetDirectoryName(CatalogPath)
            ?? throw new InvalidOperationException("La ruta del catálogo de stock no es válida.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(CatalogPath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new CatalogPayload(Normalize(namesByNormalizedCode)), JsonOptions));
        File.Move(tempPath, CatalogPath, overwrite: true);
    }

    private static StockCatalogState Empty() =>
        new(new Dictionary<string, string>(StringComparer.Ordinal), []);

    internal static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string> names)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in names)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
                continue;

            normalized[StockNames.NormalizeCode(pair.Key)] = pair.Value.Trim();
        }

        return normalized;
    }

    private sealed record CatalogPayload(Dictionary<string, string>? NamesByNormalizedCode);
}

public sealed record StockUiState(
    bool CanGenerate,
    bool CanExport,
    bool CanExtract,
    bool CanRetryPending,
    bool CanCancelPending,
    bool CanReexport,
    bool CanSaveNames,
    string? StatusText);

public sealed record StockOriginPresentationModel(
    string StatusText,
    string StockSourceText,
    string OfficialSourceText,
    string? GeneratedText,
    string? ExplanatoryText,
    bool HasStock,
    bool HasExplanatoryText);

public static class StockPresentation
{
    public const string StatusNoStock = "Sin generar";
    public const string StatusCurrentLoad = "Generado con la carga actual";
    public const string StatusPreviousLoad = "Generado con una carga anterior";
    public const string StatusOfficialAbsent = "Stock generado";
    public const string StatusOfficialUnavailable = "Stock generado";

    public const string OfficialAbsentText = "Carga oficial: Sin archivo cargado";
    public const string OfficialUnavailableText = "Carga oficial: Consulta no disponible";
    public const string StockAbsentText = "Archivo del stock: Sin stock generado";

    public const string AnteriorLoadExplanatoryText =
        "El stock no cambia automáticamente con una nueva carga oficial. Puede continuar utilizándolo o regenerarlo cuando lo decida.";

    public const string DestructiveConfirmation =
        "Esta acción reemplazará el stock operativo, las ventas, el historial y las referencias. No hay backup. El catálogo de nombres se conserva.\n\n¿Desea continuar?";

    public const string CreateConfirmation =
        "Se creará el stock desde la carga oficial actual. Esta acción no reemplaza ventas ni historial.\n\n¿Desea continuar?";

    public static StockOriginPresentationModel EvaluateOrigin(
        StockHeader? header,
        StockSourceIdentity? officialSource,
        bool officialUnavailable = false)
    {
        if (header is null)
        {
            var officialText = officialUnavailable
                ? OfficialUnavailableText
                : officialSource is null
                    ? OfficialAbsentText
                    : $"Carga oficial: {SafeFileName(officialSource.SourceFileName)} (Carga: {FormatDisplayDateTime(officialSource.LoadedAtUtc)})";

            return new StockOriginPresentationModel(
                StatusText: StatusNoStock,
                StockSourceText: StockAbsentText,
                OfficialSourceText: officialText,
                GeneratedText: null,
                ExplanatoryText: null,
                HasStock: false,
                HasExplanatoryText: false);
        }

        var stockText = $"Archivo del stock: {SafeFileName(header.SourceFileName)} (Carga: {FormatDisplayDateTime(header.LoadedAtUtc)})";
        var generatedText = $"Generación del stock: {FormatDisplayDateTime(header.GeneratedUtc)}";

        if (officialUnavailable)
        {
            return new StockOriginPresentationModel(
                StatusText: StatusOfficialUnavailable,
                StockSourceText: stockText,
                OfficialSourceText: OfficialUnavailableText,
                GeneratedText: generatedText,
                ExplanatoryText: "No se pudo consultar la carga oficial de Sergio. El stock existente sigue operativo.",
                HasStock: true,
                HasExplanatoryText: true);
        }

        if (officialSource is null)
        {
            return new StockOriginPresentationModel(
                StatusText: StatusOfficialAbsent,
                StockSourceText: stockText,
                OfficialSourceText: OfficialAbsentText,
                GeneratedText: generatedText,
                ExplanatoryText: "La carga oficial ya no está disponible. El stock existente sigue operativo.",
                HasStock: true,
                HasExplanatoryText: true);
        }

        var officialTextPresent = $"Carga oficial: {SafeFileName(officialSource.SourceFileName)} (Carga: {FormatDisplayDateTime(officialSource.LoadedAtUtc)})";
        var matches = SourcesMatch(header, officialSource);

        if (matches)
        {
            return new StockOriginPresentationModel(
                StatusText: StatusCurrentLoad,
                StockSourceText: stockText,
                OfficialSourceText: officialTextPresent,
                GeneratedText: generatedText,
                ExplanatoryText: null,
                HasStock: true,
                HasExplanatoryText: false);
        }
        else
        {
            return new StockOriginPresentationModel(
                StatusText: StatusPreviousLoad,
                StockSourceText: stockText,
                OfficialSourceText: officialTextPresent,
                GeneratedText: generatedText,
                ExplanatoryText: AnteriorLoadExplanatoryText,
                HasStock: true,
                HasExplanatoryText: true);
        }
    }

    public static bool SourcesMatch(StockHeader header, StockSourceIdentity official) =>
        string.Equals(header.SourceFileName, official.SourceFileName, StringComparison.Ordinal)
        && AsUtc(header.LoadedAtUtc) == AsUtc(official.LoadedAtUtc)
        && header.OfficialRowCount == official.RowCount
        && string.Equals(header.PrimaryKeyAlias, official.PrimaryKeyAlias, StringComparison.Ordinal);

    public static string SafeFileName(string? pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
            return string.Empty;

        try
        {
            var name = Path.GetFileName(pathOrName);
            return string.IsNullOrWhiteSpace(name) ? pathOrName.Trim() : name;
        }
        catch
        {
            return pathOrName.Trim();
        }
    }

    public static DateTime AsUtc(DateTime dateTime) =>
        dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime.ToUniversalTime();

    public static string FormatDisplayDateTime(DateTime dateTime) =>
        AsUtc(dateTime).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    public static StockUiState Evaluate(
        bool isBusy,
        bool canGenerateFromSource,
        bool hasStock,
        bool hasPending,
        bool hasCompleted)
    {
        if (isBusy)
        {
            return new StockUiState(false, false, false, false, false, false, false, "Hay una operación en curso.");
        }

        return new StockUiState(
            canGenerateFromSource && !hasPending,
            hasStock,
            hasStock && !hasPending,
            hasPending,
            hasPending,
            hasCompleted,
            hasStock,
            hasPending
                ? "Hay una extracción pendiente. Resuélvala antes de generar o extraer."
                : null);
    }

    public static string? ValidateGeneration(string? codeAlias, string? descriptionAlias) =>
        string.IsNullOrWhiteSpace(codeAlias) || string.IsNullOrWhiteSpace(descriptionAlias)
            ? "Seleccione la columna de código y la columna de descripción para esta generación."
            : null;

    public static string ResultMessage(StockFileResult? result, string success, string failure) =>
        result?.Status == StockGenerateStatus.Completed ? result.Message ?? success : result?.Message ?? failure;

    public static IReadOnlyList<(string Name, bool IsMandatory, bool IsSelected)> BuildExportColumns(
        IReadOnlyList<string> snapshotColumns,
        IReadOnlyList<string> mandatoryColumns,
        IReadOnlyList<(string Name, bool IsSelected)>? previous)
    {
        var previousByName = previous?.ToDictionary(item => item.Name, item => item.IsSelected, StringComparer.Ordinal);
        var sameSchema = previousByName is not null && previousByName.Count == snapshotColumns.Count && snapshotColumns.All(previousByName.ContainsKey);
        return snapshotColumns.Select(column =>
        {
            var mandatory = mandatoryColumns.Contains(column, StringComparer.Ordinal);
            var selected = mandatory || !sameSchema || previousByName![column];
            return (column, mandatory, selected);
        }).ToArray();
    }

    public static IReadOnlyList<StockGroupQuantity> ParseQuantities(IReadOnlyList<(string Code, string? Quantity)> rows)
    {
        var parsed = new List<StockGroupQuantity>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Quantity) || row.Quantity.Trim() == "0")
                continue;
            if (!int.TryParse(row.Quantity.Trim(), out var quantity) || quantity < 0)
                throw new InvalidOperationException("La cantidad solicitada debe ser un entero positivo.");
            parsed.Add(new StockGroupQuantity(row.Code, quantity));
        }

        return parsed;
    }
}

public static class StockNames
{
    public const string EmptyCodeDisplayName = "Sin código";
    public const string DataTable = "stock_data";
    public const string StatusTable = "stock_status";
    public const string ExtractionsTable = "stock_extractions";
    public const string HeadersTable = "stock_headers";

    public static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    public static string DisplayName(string normalizedCode, string? catalogName) =>
        string.IsNullOrWhiteSpace(catalogName)
            ? (string.IsNullOrEmpty(normalizedCode) ? EmptyCodeDisplayName : normalizedCode)
            : catalogName.Trim();
}

public sealed class StockSnapshotService
{
    private readonly SergioStagingAnalyzer analyzer;
    private readonly StockCatalogStore catalog;

    private readonly Action<string>? beforePublish;
    private readonly Action<string>? beforeFinalize;

    public StockSnapshotService(
        SergioStagingAnalyzer analyzer,
        StockCatalogStore catalog,
        Action<string>? beforePublish = null,
        Action<string>? beforeFinalize = null)
    {
        this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.beforePublish = beforePublish;
        this.beforeFinalize = beforeFinalize;
    }

    public string DatabasePath => analyzer.DatabasePath;

    public StockOfficialSource GetOfficialSource()
    {
        using var operation = analyzer.AcquirePromotionLock();
        using var connection = Open(DatabasePath);
        if (!TableExists(connection, SergioLoadPromoter.OfficialTableName) || !TableExists(connection, SergioLoadPromoter.MetadataTableName))
            return new StockOfficialSource(null, [], false, null);

        var metadata = ReadMetadata(connection);
        var columns = ReadColumns(connection, SergioLoadPromoter.OfficialTableName, null);
        var canGenerate = metadata is not null
            && !string.IsNullOrWhiteSpace(metadata.SelectedPrimaryKeyAlias)
            && columns.Contains(metadata.SelectedPrimaryKeyAlias, StringComparer.Ordinal)
            && metadata.RowCount > 0;
        var identity = metadata is null ? null : new StockSourceIdentity(
            metadata.SourceFileName,
            metadata.LoadedAtUtc,
            metadata.RowCount,
            metadata.SelectedPrimaryKeyAlias);
        return new StockOfficialSource(metadata?.SelectedPrimaryKeyAlias, columns, canGenerate, identity);
    }

    public IReadOnlyList<string> GetSnapshotColumns()
    {
        using var operation = analyzer.AcquirePromotionLock();
        using var connection = Open(DatabasePath);
        return TableExists(connection, StockNames.DataTable) ? ReadColumns(connection, StockNames.DataTable, null) : [];
    }

    public StockSummary? GetSummary()
    {
        using var operation = analyzer.AcquirePromotionLock();
        using var connection = Open(DatabasePath);
        return ReadSummary(connection, null);
    }

    public StockGenerateResult Generate(StockGenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = analyzer.AcquirePromotionLock();
        using var connection = Open(DatabasePath);
        try
        {
            using var transaction = connection.BeginTransaction();
            var pending = PendingExtractionCount(connection, transaction);
            if (pending > 0)
            {
                transaction.Rollback();
                return StockGenerateResult.ValidationFailed("El stock actual tiene una extracción pendiente.");
            }

            if (StockExists(connection, transaction) && !request.ConfirmedRegeneration)
            {
                transaction.Rollback();
                return StockGenerateResult.ValidationFailed("La regeneración del stock requiere una confirmación explícita.");
            }

            var source = ReadOfficialSource(connection, transaction, request);
            if (source.Error is not null)
            {
                transaction.Rollback();
                return StockGenerateResult.ValidationFailed(source.Error);
            }

            ReplaceOperationalTables(connection, transaction, source.Columns, request);
            InsertHeader(connection, transaction, source.Identity, request);
            transaction.Commit();
        }
        catch (Exception exception)
        {
            return StockGenerateResult.Failed($"No se pudo generar el stock. La operación no confirmó cambios. {exception.Message}");
        }

        var catalogWarning = SeedMissingCatalogNames(connection);
        var summary = ReadSummary(connection, null);
        if (summary is null)
        {
            return StockGenerateResult.Failed("El stock se confirmó, pero no se pudo leer el resumen. La base no se revirtió.");
        }

        return StockGenerateResult.Completed(summary, catalogWarning);
    }

    public void SaveCatalog(IReadOnlyDictionary<string, string> namesByNormalizedCode) =>
        catalog.Save(namesByNormalizedCode);

    public StockFileResult ExportSummary(string destinationCsvPath)
    {
        try
        {
            ValidateOutputPath(destinationCsvPath);
            using var operation = analyzer.AcquirePromotionLock();
            using var connection = Open(DatabasePath);
            var summary = ReadSummary(connection, null);
            if (summary is null)
                return StockFileResult.ValidationFailed("No existe un stock actual.");

            var rows = new List<IReadOnlyList<string?>>
            {
                new[] { "CODIGO", "DESCRIPCION", "TOTAL", "VENDIDO", "DISPONIBLE" }
            };
            foreach (var group in summary.Groups)
            {
                rows.Add(new[]
                {
                    group.NormalizedCode,
                    group.DisplayName,
                    group.Total.ToString(CultureInfo.InvariantCulture),
                    group.Sold.ToString(CultureInfo.InvariantCulture),
                    (group.Available + group.Reserved).ToString(CultureInfo.InvariantCulture)
                });
            }

            return Publish(destinationCsvPath, rows, rows.Count - 1);
        }
        catch (Exception exception)
        {
            return StockFileResult.Failed($"No se pudo exportar el resumen de stock. {exception.Message}");
        }
    }

    public StockFileResult ExportDetail(StockDetailExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            ValidateOutputPath(request.DestinationCsvPath);
            using var operation = analyzer.AcquirePromotionLock();
            using var connection = Open(DatabasePath);
            var header = ReadHeader(connection, null);
            if (header is null)
                return StockFileResult.ValidationFailed("No existe un stock actual.");

            var columns = BuildDetailColumns(connection, header, request.SelectedColumns);
            if (columns.Error is not null)
                return StockFileResult.ValidationFailed(columns.Error);

            var tempPath = NewTempPath(request.DestinationCsvPath);
            try
            {
                var count = WriteDetail(connection, header, columns.Columns, request.OnlyAvailable, null, tempPath);
                PublishTemp(tempPath, request.DestinationCsvPath);
                return StockFileResult.Completed(count, request.DestinationCsvPath);
            }
            finally
            {
                TryDeleteOwnedTemp(tempPath);
            }
        }
        catch (Exception exception)
        {
            return StockFileResult.Failed($"No se pudo exportar el detalle de stock. {exception.Message}");
        }
    }

    public StockFileResult BeginExtraction(StockExtractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? extractionId = null;
        try
        {
            ValidateOutputPath(request.DestinationCsvPath);
            using var operation = analyzer.AcquirePromotionLock();
            using var connection = Open(DatabasePath);
            using var transaction = connection.BeginTransaction();
            if (PendingExtractionCount(connection, transaction) > 0)
            {
                transaction.Rollback();
                return StockFileResult.ValidationFailed("Ya existe una extracción pendiente.");
            }

            var header = ReadHeader(connection, transaction);
            if (header is null)
            {
                transaction.Rollback();
                return StockFileResult.ValidationFailed("No existe un stock actual.");
            }

            var groups = ValidateGroups(request.Groups);
            if (groups.Error is not null)
            {
                transaction.Rollback();
                return StockFileResult.ValidationFailed(groups.Error);
            }

            var columns = BuildDetailColumns(connection, header, request.SelectedColumns);
            if (columns.Error is not null)
            {
                transaction.Rollback();
                return StockFileResult.ValidationFailed(columns.Error);
            }

            var availability = QueryAvailable(connection, transaction, header);
            foreach (var group in groups.Groups)
            {
                if (!availability.TryGetValue(group.NormalizedCode, out var available) || available < group.Quantity)
                {
                    transaction.Rollback();
                    return StockFileResult.ValidationFailed("El grupo solicitado no tiene suficientes personas disponibles.");
                }
            }

            extractionId = Guid.NewGuid().ToString("N");
            var expected = groups.Groups.Sum(group => group.Quantity);
            foreach (var group in groups.Groups)
            {
                var reserved = ReserveGroup(connection, transaction, header, extractionId, group);
                if (reserved != group.Quantity)
                {
                    transaction.Rollback();
                    return StockFileResult.ValidationFailed("No se pudieron reservar todas las personas solicitadas.");
                }
            }

            InsertPending(connection, transaction, extractionId, request, columns.Columns, expected);
            transaction.Commit();

            var tempPath = NewTempPath(request.DestinationCsvPath);
            try
            {
                var exported = WriteDetail(connection, header, columns.Columns, false, extractionId, tempPath);
                if (exported != expected)
                    return StockFileResult.Failed("La extracción quedó pendiente porque el archivo no coincide con la reserva.", extractionId);

                PublishTemp(tempPath, request.DestinationCsvPath);
            }
            finally
            {
                TryDeleteOwnedTemp(tempPath);
            }

            beforeFinalize?.Invoke(request.DestinationCsvPath);
            Finalize(connection, extractionId, expected, request.DestinationCsvPath);
            return StockFileResult.Completed(expected, request.DestinationCsvPath, extractionId);
        }
        catch (Exception exception)
        {
            return StockFileResult.Failed(
                extractionId is null
                    ? $"No se pudo iniciar la extracción. No se confirmó ninguna reserva. {exception.Message}"
                    : $"La extracción quedó pendiente y se puede reintentar. {exception.Message}",
                extractionId);
        }
    }

    public StockPendingExtraction? GetPending()
    {
        using var operation = analyzer.AcquirePromotionLock();
        using var connection = Open(DatabasePath);
        return ReadPending(connection, null);
    }

    public StockFileResult RetryPending(string? alternateOutputPath = null)
    {
        string? extractionId = null;
        try
        {
            using var operation = analyzer.AcquirePromotionLock();
            using var connection = Open(DatabasePath);
            var pending = ReadPending(connection, null);
            if (pending is null)
                return StockFileResult.ValidationFailed("No existe una extracción pendiente.");

            extractionId = pending.ExtractionId;
            var destination = string.IsNullOrWhiteSpace(alternateOutputPath) ? pending.OutputPath : alternateOutputPath;
            if (!string.Equals(Path.GetFullPath(destination), Path.GetFullPath(pending.OutputPath), StringComparison.OrdinalIgnoreCase))
                ValidateOutputPath(destination);

            var header = ReadHeader(connection, null) ?? throw new InvalidOperationException("No existe el encabezado del stock.");
            var stored = ReadExtraction(connection, extractionId);
            var tempPath = NewTempPath(destination);
            try
            {
                var exported = WriteDetail(connection, header, stored.Columns, false, extractionId, tempPath);
                if (exported != pending.ExpectedRows)
                    return StockFileResult.Failed("La extracción pendiente no coincide con sus filas reservadas.", extractionId);

                if (File.Exists(destination))
                {
                    if (!FilesEqual(tempPath, destination))
                        return StockFileResult.Failed("El archivo de salida existe y no coincide con la extracción pendiente. Indique otra ruta para recuperarla.", extractionId);
                }
                else
                {
                    PublishTemp(tempPath, destination);
                }

                beforeFinalize?.Invoke(destination);
                Finalize(connection, extractionId, pending.ExpectedRows, destination);
                return StockFileResult.Completed(exported, destination, extractionId);
            }
            finally
            {
                TryDeleteOwnedTemp(tempPath);
            }
        }
        catch (Exception exception)
        {
            return StockFileResult.Failed($"No se pudo reintentar la extracción pendiente. {exception.Message}", extractionId);
        }
    }

    public StockFileResult CancelPending()
    {
        try
        {
            using var operation = analyzer.AcquirePromotionLock();
            using var connection = Open(DatabasePath);
            using var transaction = connection.BeginTransaction();
            var pending = ReadPending(connection, transaction);
            if (pending is null)
            {
                transaction.Rollback();
                return StockFileResult.ValidationFailed("No existe una extracción pendiente.");
            }

            Execute(connection, transaction, $"UPDATE {Q(StockNames.StatusTable)} SET extraction_id = NULL WHERE extraction_id = ? AND sold = FALSE", pending.ExtractionId);
            Execute(connection, transaction, $"DELETE FROM {Q(StockNames.ExtractionsTable)} WHERE extraction_id = ? AND state = 'pending'", pending.ExtractionId);
            transaction.Commit();
            return StockFileResult.Completed(0, pending.OutputPath, pending.ExtractionId);
        }
        catch (Exception exception)
        {
            return StockFileResult.Failed($"No se pudo cancelar la extracción pendiente. {exception.Message}");
        }
    }

    public IReadOnlyList<StockCompletedExtraction> ListCompleted()
    {
        using var operation = analyzer.AcquirePromotionLock();
        using var connection = Open(DatabasePath);
        if (!TableExists(connection, StockNames.ExtractionsTable))
            return [];

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT extraction_id, output_path, expected_rows, sale_reference, started_utc, completed_utc FROM {Q(StockNames.ExtractionsTable)} WHERE state = 'completed' ORDER BY completed_utc";
        using var reader = command.ExecuteReader();
        var rows = new List<StockCompletedExtraction>();
        while (reader.Read())
        {
            rows.Add(new StockCompletedExtraction(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5)));
        }

        return rows;
    }

    public StockFileResult ReexportCompleted(string extractionId, string destinationCsvPath)
    {
        try
        {
            ValidateOutputPath(destinationCsvPath);
            using var operation = analyzer.AcquirePromotionLock();
            using var connection = Open(DatabasePath);
            var stored = ReadExtraction(connection, extractionId);
            if (!string.Equals(stored.State, "completed", StringComparison.Ordinal))
                return StockFileResult.ValidationFailed("La extracción solicitada no está completada.");

            var header = ReadHeader(connection, null) ?? throw new InvalidOperationException("No existe el encabezado del stock.");
            var tempPath = NewTempPath(destinationCsvPath);
            try
            {
                var exported = WriteDetail(connection, header, stored.Columns, false, extractionId, tempPath);
                PublishTemp(tempPath, destinationCsvPath);
                return StockFileResult.Completed(exported, destinationCsvPath, extractionId);
            }
            finally
            {
                TryDeleteOwnedTemp(tempPath);
            }
        }
        catch (Exception exception)
        {
            return StockFileResult.Failed($"No se pudo reexportar la extracción. {exception.Message}");
        }
    }

    private string? SeedMissingCatalogNames(DuckDBConnection connection)
    {
        try
        {
            var state = catalog.Load();
            if (state.Warnings.Count > 0)
                return null;

            var header = ReadHeader(connection, null);
            if (header is null)
                return null;

            var seeds = QuerySeedNames(connection, header);
            if (seeds.Count == 0)
                return null;

            var merged = new Dictionary<string, string>(state.NamesByNormalizedCode, StringComparer.Ordinal);
            var changed = false;
            foreach (var seed in seeds)
            {
                if (merged.ContainsKey(seed.Key))
                    continue;

                merged[seed.Key] = seed.Value;
                changed = true;
            }

            if (changed)
                catalog.Save(merged);

            return null;
        }
        catch (Exception exception)
        {
            return "El stock se confirmó, pero no se pudo guardar el catálogo de nombres. La base no se revirtió. Puede guardar los nombres manualmente sin regenerar. " + exception.Message;
        }
    }

    private StockSummary? ReadSummary(DuckDBConnection connection, DuckDBTransaction? transaction)
    {
        var header = ReadHeader(connection, transaction);
        if (header is null)
            return null;

        var catalogState = catalog.Load();
        var displayNames = new Dictionary<string, string>(catalogState.NamesByNormalizedCode, StringComparer.Ordinal);
        if (catalogState.Warnings.Count > 0)
        {
            foreach (var seed in QuerySeedNames(connection, header))
            {
                if (!displayNames.ContainsKey(seed.Key))
                    displayNames[seed.Key] = seed.Value;
            }
        }

        var groups = QueryGroups(connection, transaction, header, displayNames);
        return new StockSummary(
            header,
            groups.Sum(group => group.Total),
            groups.Sum(group => group.Sold),
            groups.Sum(group => group.Available),
            groups,
            SourceWarning(connection, header),
            catalogState.Warnings);
    }

    private static string? SourceWarning(DuckDBConnection connection, StockHeader header)
    {
        try
        {
            if (!TableExists(connection, SergioLoadPromoter.OfficialTableName) || !TableExists(connection, SergioLoadPromoter.MetadataTableName))
            {
                return "La carga oficial de Sergio ya no está disponible. El stock existente sigue operativo.";
            }

            var current = ReadMetadata(connection);
            if (current is null)
            {
                return "La carga oficial de Sergio ya no está disponible. El stock existente sigue operativo.";
            }

            var official = new StockSourceIdentity(
                current.SourceFileName,
                current.LoadedAtUtc,
                current.RowCount,
                current.SelectedPrimaryKeyAlias);

            if (!StockPresentation.SourcesMatch(header, official))
            {
                return "Hay una carga oficial de Sergio más reciente que el stock. El aviso es informativo y no bloquea el stock existente.";
            }

            return null;
        }
        catch (Exception)
        {
            return "No se pudo consultar la carga oficial de Sergio. El stock existente sigue operativo.";
        }
    }

    private static void ReplaceOperationalTables(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        IReadOnlyList<string> columns,
        StockGenerateRequest request)
    {
        Execute(connection, transaction, $"DROP TABLE IF EXISTS {SergioSql.Quote(StockNames.StatusTable)}");
        Execute(connection, transaction, $"DROP TABLE IF EXISTS {SergioSql.Quote(StockNames.ExtractionsTable)}");
        Execute(connection, transaction, $"DROP TABLE IF EXISTS {SergioSql.Quote(StockNames.HeadersTable)}");
        Execute(connection, transaction, $"DROP TABLE IF EXISTS {SergioSql.Quote(StockNames.DataTable)}");

        var projection = string.Join(", ", columns.Select(SergioSql.Quote));
        Execute(
            connection,
            transaction,
            $"CREATE TABLE {SergioSql.Quote(StockNames.DataTable)} AS SELECT {projection} FROM {SergioSql.Quote(SergioLoadPromoter.OfficialTableName)}");
        Execute(
            connection,
            transaction,
            $"ALTER TABLE {SergioSql.Quote(StockNames.DataTable)} ADD PRIMARY KEY ({SergioSql.Quote(request.PrimaryKeyAlias)})");
        Execute(
            connection,
            transaction,
            $"CREATE TABLE {SergioSql.Quote(StockNames.StatusTable)} (member_id VARCHAR PRIMARY KEY, sold BOOLEAN NOT NULL DEFAULT FALSE, extraction_id VARCHAR)");
        Execute(
            connection,
            transaction,
            $"INSERT INTO {SergioSql.Quote(StockNames.StatusTable)} (member_id) SELECT {SergioSql.Quote(request.PrimaryKeyAlias)} FROM {SergioSql.Quote(StockNames.DataTable)}");
        Execute(
            connection,
            transaction,
            $"""
            CREATE TABLE {SergioSql.Quote(StockNames.ExtractionsTable)} (
                extraction_id VARCHAR PRIMARY KEY,
                state VARCHAR NOT NULL,
                started_utc TIMESTAMP,
                completed_utc TIMESTAMP,
                output_path VARCHAR,
                expected_rows BIGINT,
                selected_columns_json VARCHAR,
                sale_reference VARCHAR
            )
            """);
        Execute(
            connection,
            transaction,
            $"""
            CREATE TABLE {SergioSql.Quote(StockNames.HeadersTable)} (
                stock_id VARCHAR PRIMARY KEY,
                source_file_name VARCHAR NOT NULL,
                loaded_at_utc TIMESTAMP NOT NULL,
                official_row_count BIGINT NOT NULL,
                primary_key_alias VARCHAR NOT NULL,
                code_alias VARCHAR NOT NULL,
                description_alias VARCHAR NOT NULL,
                generated_utc TIMESTAMP NOT NULL
            )
            """);
    }

    private static void InsertHeader(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        StockSourceIdentity identity,
        StockGenerateRequest request)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO {SergioSql.Quote(StockNames.HeadersTable)} VALUES (?, ?, ?, ?, ?, ?, ?, ?)";
        command.Parameters.Add(new DuckDBParameter { Value = "current" });
        command.Parameters.Add(new DuckDBParameter { Value = identity.SourceFileName });
        command.Parameters.Add(new DuckDBParameter { Value = identity.LoadedAtUtc });
        command.Parameters.Add(new DuckDBParameter { Value = identity.RowCount });
        command.Parameters.Add(new DuckDBParameter { Value = request.PrimaryKeyAlias });
        command.Parameters.Add(new DuckDBParameter { Value = request.CodeAlias });
        command.Parameters.Add(new DuckDBParameter { Value = request.DescriptionAlias });
        command.Parameters.Add(new DuckDBParameter { Value = DateTime.UtcNow });
        command.ExecuteNonQuery();
    }

    private static (string? Error, IReadOnlyList<string> Columns, StockSourceIdentity Identity) ReadOfficialSource(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        StockGenerateRequest request)
    {
        if (!TableExists(connection, SergioLoadPromoter.OfficialTableName, transaction)
            || !TableExists(connection, SergioLoadPromoter.MetadataTableName, transaction))
        {
            return ("No hay una carga oficial de Sergio lista para generar stock.", [], null!);
        }

        var metadata = ReadMetadata(connection, transaction);
        if (metadata is null)
            return ("No hay metadatos de la carga oficial de Sergio.", [], null!);

        if (string.IsNullOrWhiteSpace(request.PrimaryKeyAlias)
            || !string.Equals(metadata.SelectedPrimaryKeyAlias, request.PrimaryKeyAlias, StringComparison.Ordinal))
        {
            return ("La clave seleccionada no coincide con la clave de la carga oficial.", [], null!);
        }

        var columns = ReadColumns(connection, SergioLoadPromoter.OfficialTableName, transaction);
        if (!columns.Contains(request.PrimaryKeyAlias, StringComparer.Ordinal))
            return ("La clave seleccionada no está en la carga oficial.", [], null!);

        if (string.IsNullOrWhiteSpace(request.CodeAlias) || !columns.Contains(request.CodeAlias, StringComparer.Ordinal))
            return ("La columna de código seleccionada no está en la carga oficial.", [], null!);

        if (string.IsNullOrWhiteSpace(request.DescriptionAlias) || !columns.Contains(request.DescriptionAlias, StringComparer.Ordinal))
            return ("La columna de descripción seleccionada no está en la carga oficial.", [], null!);

        var rowCount = Scalar(connection, $"SELECT COUNT(*) FROM {SergioSql.Quote(SergioLoadPromoter.OfficialTableName)}", transaction);
        if (rowCount == 0)
            return ("La carga oficial de Sergio no tiene filas.", [], null!);

        if (rowCount != metadata.RowCount)
            return ("La carga oficial de Sergio no coincide con sus metadatos.", [], null!);

        return (null, columns, new StockSourceIdentity(
            metadata.SourceFileName,
            metadata.LoadedAtUtc,
            metadata.RowCount,
            metadata.SelectedPrimaryKeyAlias));
    }

    private static Dictionary<string, string> QuerySeedNames(DuckDBConnection connection, StockHeader header)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT
                UPPER(TRIM(COALESCE({SergioSql.Quote(header.CodeAlias)}, ''))) AS normalized_code,
                MIN(TRIM({SergioSql.Quote(header.DescriptionAlias)})) AS description
            FROM {SergioSql.Quote(StockNames.DataTable)}
            WHERE TRIM(COALESCE({SergioSql.Quote(header.DescriptionAlias)}, '')) <> ''
            GROUP BY 1
            """;
        using var reader = command.ExecuteReader();
        var seeds = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var code = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var description = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (!string.IsNullOrWhiteSpace(description))
                seeds[code] = description.Trim();
        }

        return seeds;
    }

    private static IReadOnlyList<StockGroupSummary> QueryGroups(
        DuckDBConnection connection,
        DuckDBTransaction? transaction,
        StockHeader header,
        IReadOnlyDictionary<string, string> catalogNames)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT
                UPPER(TRIM(COALESCE(data.{SergioSql.Quote(header.CodeAlias)}, ''))) AS normalized_code,
                COUNT(*) AS total,
                SUM(CASE WHEN status.sold THEN 1 ELSE 0 END) AS sold,
                SUM(CASE WHEN status.sold = FALSE AND status.extraction_id IS NOT NULL THEN 1 ELSE 0 END) AS reserved
            FROM {SergioSql.Quote(StockNames.DataTable)} AS data
            JOIN {SergioSql.Quote(StockNames.StatusTable)} AS status
              ON status.member_id = data.{SergioSql.Quote(header.PrimaryKeyAlias)}
            GROUP BY 1
            ORDER BY CASE WHEN normalized_code = '' THEN 1 ELSE 0 END, normalized_code
            """;
        using var reader = command.ExecuteReader();
        var groups = new List<StockGroupSummary>();
        while (reader.Read())
        {
            var code = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var total = reader.GetInt64(1);
            var sold = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            var reserved = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
            catalogNames.TryGetValue(code, out var catalogName);
            groups.Add(new StockGroupSummary(code, StockNames.DisplayName(code, catalogName), total, sold, total - sold - reserved, reserved));
        }

        return groups;
    }

    private static StockHeader? ReadHeader(DuckDBConnection connection, DuckDBTransaction? transaction)
    {
        if (!TableExists(connection, StockNames.HeadersTable, transaction))
            return null;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT source_file_name, loaded_at_utc, official_row_count, primary_key_alias,
                   code_alias, description_alias, generated_utc
            FROM {SergioSql.Quote(StockNames.HeadersTable)}
            LIMIT 1
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new StockHeader(
            reader.GetString(0),
            reader.GetDateTime(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetDateTime(6));
    }

    private static SergioLoadMetadata? ReadMetadata(DuckDBConnection connection, DuckDBTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT {SergioLoadMetadataSchema.SourceFileNameColumn}, {SergioLoadMetadataSchema.LoadedAtUtcColumn}, {SergioLoadMetadataSchema.RowCountColumn}, {SergioLoadMetadataSchema.SelectedPrimaryKeyAliasColumn}, {SergioLoadMetadataSchema.SchemaVersionColumn} " +
            $"FROM {SergioSql.Quote(SergioLoadPromoter.MetadataTableName)} LIMIT 1";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new SergioLoadMetadata(
            reader.GetString(0),
            reader.GetDateTime(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static long PendingExtractionCount(DuckDBConnection connection, DuckDBTransaction transaction)
    {
        if (!TableExists(connection, StockNames.ExtractionsTable, transaction))
            return 0;

        return Scalar(
            connection,
            $"SELECT COUNT(*) FROM {SergioSql.Quote(StockNames.ExtractionsTable)} WHERE state = 'pending'",
            transaction);
    }

    private static bool StockExists(DuckDBConnection connection, DuckDBTransaction transaction) =>
        TableExists(connection, StockNames.HeadersTable, transaction)
        && Scalar(connection, $"SELECT COUNT(*) FROM {SergioSql.Quote(StockNames.HeadersTable)}", transaction) > 0;

    private static IReadOnlyList<string> ReadColumns(DuckDBConnection connection, string table, DuckDBTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name = ? ORDER BY ordinal_position";
        command.Parameters.Add(new DuckDBParameter { Value = table });
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
            columns.Add(reader.GetString(0));

        return columns;
    }

    private static bool TableExists(DuckDBConnection connection, string table, DuckDBTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = ?";
        command.Parameters.Add(new DuckDBParameter { Value = table });
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static long Scalar(DuckDBConnection connection, string sql, DuckDBTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Execute(DuckDBConnection connection, DuckDBTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void Finalize(DuckDBConnection connection, string extractionId, long expectedRows, string outputPath)
    {
        using var transaction = connection.BeginTransaction();
        var updated = Execute(
            connection,
            transaction,
            $"UPDATE {Q(StockNames.StatusTable)} SET sold = TRUE WHERE extraction_id = ? AND sold = FALSE",
            extractionId);
        if (updated != expectedRows)
        {
            transaction.Rollback();
            throw new InvalidOperationException("La finalización no coincide con las filas reservadas.");
        }

        var closed = Execute(
            connection,
            transaction,
            $"UPDATE {Q(StockNames.ExtractionsTable)} SET state = 'completed', completed_utc = ?, output_path = ? WHERE extraction_id = ? AND state = 'pending' AND expected_rows = ?",
            DateTime.UtcNow,
            outputPath,
            extractionId,
            expectedRows);
        if (closed != 1)
        {
            transaction.Rollback();
            throw new InvalidOperationException("La extracción pendiente ya no coincide con su configuración.");
        }

        transaction.Commit();
    }

    private static int ReserveGroup(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        StockHeader header,
        string extractionId,
        StockGroupQuantity group)
    {
        var code = Q(header.CodeAlias);
        var key = Q(header.PrimaryKeyAlias);
        return Execute(
            connection,
            transaction,
            $"""
            UPDATE {Q(StockNames.StatusTable)}
            SET extraction_id = ?
            WHERE member_id IN (
                SELECT data.{key}
                FROM {Q(StockNames.DataTable)} AS data
                JOIN {Q(StockNames.StatusTable)} AS status ON status.member_id = data.{key}
                WHERE status.sold = FALSE
                  AND status.extraction_id IS NULL
                  AND UPPER(TRIM(COALESCE(data.{code}, ''))) = ?
                ORDER BY data.{key}
                LIMIT ?
            )
            """,
            extractionId,
            group.NormalizedCode,
            group.Quantity);
    }

    private static Dictionary<string, long> QueryAvailable(DuckDBConnection connection, DuckDBTransaction transaction, StockHeader header)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT UPPER(TRIM(COALESCE(data.{Q(header.CodeAlias)}, ''))), COUNT(*)
            FROM {Q(StockNames.DataTable)} AS data
            JOIN {Q(StockNames.StatusTable)} AS status ON status.member_id = data.{Q(header.PrimaryKeyAlias)}
            WHERE status.sold = FALSE AND status.extraction_id IS NULL
            GROUP BY 1
            """;
        using var reader = command.ExecuteReader();
        var available = new Dictionary<string, long>(StringComparer.Ordinal);
        while (reader.Read())
            available[reader.IsDBNull(0) ? string.Empty : reader.GetString(0)] = reader.GetInt64(1);
        return available;
    }

    private static (string? Error, IReadOnlyList<StockGroupQuantity> Groups) ValidateGroups(IReadOnlyList<StockGroupQuantity> groups)
    {
        if (groups is null || groups.Count == 0 || groups.All(group => group.Quantity == 0))
            return ("Se requiere al menos un grupo con cantidad positiva.", []);

        var normalized = new List<StockGroupQuantity>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (group.Quantity <= 0)
                return ("La cantidad solicitada debe ser un entero positivo.", []);

            var code = StockNames.NormalizeCode(group.NormalizedCode);
            if (!seen.Add(code))
                return ("El grupo solicitado está repetido.", []);

            normalized.Add(new StockGroupQuantity(code, group.Quantity));
        }

        return (null, normalized);
    }

    private static (string? Error, IReadOnlyList<string> Columns) BuildDetailColumns(
        DuckDBConnection connection,
        StockHeader header,
        IReadOnlyList<string> selected)
    {
        var available = ReadColumns(connection, StockNames.DataTable, null);
        var columns = new List<string>();
        foreach (var alias in new[] { header.PrimaryKeyAlias, header.CodeAlias, header.DescriptionAlias })
        {
            if (!available.Contains(alias, StringComparer.Ordinal))
                return ("La configuración guardada ya no coincide con el stock.", []);
            if (!columns.Contains(alias, StringComparer.Ordinal))
                columns.Add(alias);
        }

        foreach (var alias in selected ?? [])
        {
            if (string.IsNullOrWhiteSpace(alias) || !available.Contains(alias, StringComparer.Ordinal))
                return ("Una columna seleccionada no está en el stock.", []);
            if (!columns.Contains(alias, StringComparer.Ordinal))
                columns.Add(alias);
        }

        return (null, columns);
    }

    private static long WriteDetail(
        DuckDBConnection connection,
        StockHeader header,
        IReadOnlyList<string> columns,
        bool onlyAvailable,
        string? extractionId,
        string tempPath)
    {
        var select = string.Join(", ", columns.Select(column => $"data.{Q(column)}"));
        var where = extractionId is null
            ? onlyAvailable ? "WHERE status.sold = FALSE AND status.extraction_id IS NULL" : string.Empty
            : "WHERE status.extraction_id = ?";
        var soldHeader = UniqueSoldHeader(columns);
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {select}, CASE WHEN status.sold THEN '1' ELSE '0' END
            FROM {Q(StockNames.DataTable)} AS data
            JOIN {Q(StockNames.StatusTable)} AS status ON status.member_id = data.{Q(header.PrimaryKeyAlias)}
            {where}
            ORDER BY data.{Q(header.PrimaryKeyAlias)}
            """;
        if (extractionId is not null)
            command.Parameters.Add(new DuckDBParameter { Value = extractionId });

        using var reader = command.ExecuteReader();
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        using var writer = StreamingCsvWriter.OpenNew(tempPath);
        writer.WriteRow(columns.Append(soldHeader));
        long count = 0;
        while (reader.Read())
        {
            var fields = new string?[reader.FieldCount];
            for (var index = 0; index < fields.Length; index++)
                fields[index] = reader.IsDBNull(index) ? null : reader.GetString(index);
            writer.WriteRow(fields);
            count++;
        }

        return count;
    }

    private StockFileResult Publish(string destination, IReadOnlyList<IReadOnlyList<string?>> rows, long count)
    {
        var tempPath = NewTempPath(destination);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            using (var writer = StreamingCsvWriter.OpenNew(tempPath))
            {
                foreach (var row in rows)
                    writer.WriteRow(row);
            }

            PublishTemp(tempPath, destination);
            return StockFileResult.Completed(count, destination);
        }
        finally
        {
            TryDeleteOwnedTemp(tempPath);
        }
    }

    private void PublishTemp(string tempPath, string destination)
    {
        beforePublish?.Invoke(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.Move(tempPath, destination, overwrite: false);
    }

    private static void InsertPending(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        string extractionId,
        StockExtractionRequest request,
        IReadOnlyList<string> columns,
        int expected)
    {
        var reference = string.IsNullOrWhiteSpace(request.SaleReference) ? null : request.SaleReference.Trim().ToUpperInvariant();
        Execute(
            connection,
            transaction,
            $"INSERT INTO {Q(StockNames.ExtractionsTable)} (extraction_id, state, started_utc, output_path, expected_rows, selected_columns_json, sale_reference) VALUES (?, 'pending', ?, ?, ?, ?, ?)",
            extractionId,
            DateTime.UtcNow,
            request.DestinationCsvPath,
            expected,
            JsonSerializer.Serialize(columns),
            (object?)reference ?? DBNull.Value);
    }

    private static StockPendingExtraction? ReadPending(DuckDBConnection connection, DuckDBTransaction? transaction)
    {
        if (!TableExists(connection, StockNames.ExtractionsTable, transaction))
            return null;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT extraction_id, output_path, expected_rows, sale_reference FROM {Q(StockNames.ExtractionsTable)} WHERE state = 'pending' LIMIT 1";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new StockPendingExtraction(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static (string State, IReadOnlyList<string> Columns) ReadExtraction(DuckDBConnection connection, string extractionId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT state, selected_columns_json FROM {Q(StockNames.ExtractionsTable)} WHERE extraction_id = ?";
        command.Parameters.Add(new DuckDBParameter { Value = extractionId });
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("La extracción solicitada no existe.");

        var columns = JsonSerializer.Deserialize<List<string>>(reader.GetString(1)) ?? [];
        return (reader.GetString(0), columns);
    }

    private void ValidateOutputPath(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new InvalidOperationException("La ruta de salida es obligatoria.");

        var full = Path.GetFullPath(destination);
        var database = Path.GetFullPath(DatabasePath);
        var blocked = new[] { database, database + ".wal", catalog.CatalogPath };
        if (blocked.Any(path => string.Equals(full, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("La ruta de salida no puede sobrescribir la base, su archivo auxiliar ni el catálogo.");

        if (File.Exists(full))
            throw new IOException("El archivo de salida ya existe y no se sobrescribe.");
    }

    private static string NewTempPath(string destination) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

    private static void TryDeleteOwnedTemp(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string UniqueSoldHeader(IReadOnlyList<string> columns)
    {
        var candidate = "VENDIDO";
        var suffix = 0;
        while (columns.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            candidate = suffix++ == 0 ? "ESTADO_VENDIDO" : $"ESTADO_VENDIDO_{suffix}";
        return candidate;
    }

    private static bool FilesEqual(string expectedPath, string actualPath)
    {
        using var expected = File.OpenRead(expectedPath);
        using var actual = File.OpenRead(actualPath);
        if (expected.Length != actual.Length)
            return false;

        var expectedBuffer = new byte[81920];
        var actualBuffer = new byte[81920];
        int count;
        while ((count = expected.Read(expectedBuffer, 0, expectedBuffer.Length)) > 0)
        {
            actual.ReadExactly(actualBuffer.AsSpan(0, count));
            if (!expectedBuffer.AsSpan(0, count).SequenceEqual(actualBuffer.AsSpan(0, count)))
                return false;
        }
        return true;
    }

    private static string Q(string identifier) => SergioSql.Quote(identifier);

    private static int Execute(DuckDBConnection connection, DuckDBTransaction? transaction, string sql, params object[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values)
            command.Parameters.Add(new DuckDBParameter { Value = value });
        return command.ExecuteNonQuery();
    }

    private static DuckDBConnection Open(string path)
    {
        var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();
        return connection;
    }
}
