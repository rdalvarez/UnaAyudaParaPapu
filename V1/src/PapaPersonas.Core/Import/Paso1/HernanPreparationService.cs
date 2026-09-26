namespace PapaPersonas.Core.Import.Paso1;

public sealed class HernanPreparationService
{
    private readonly IExcelRowSource _excelRowSource;
    private readonly ICsvFileWriter _csvFileWriter;

    public HernanPreparationService(IExcelRowSource excelRowSource, ICsvFileWriter csvFileWriter)
    {
        _excelRowSource = excelRowSource;
        _csvFileWriter = csvFileWriter;
    }

    /// <summary>Valida la configuración y los encabezados, procesa las filas y genera las salidas de Paso 1.</summary>
    public HernanPreparationResult Process(HernanPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var config = request.Config ?? HernanPaso1Config.Default;
        var configValidation = HernanConfigValidator.Validate(config);
        if (!configValidation.IsValid)
        {
            var configErrors = configValidation.Errors
                .Select(message => new ValidationIssue(ValidationErrorCode.MissingRequiredHeader, message))
                .ToArray();

            return HernanPreparationResult.ValidationFailure(configErrors, [], [], config.OutputColumns);
        }

        var headers = _excelRowSource.ReadHeaders(request.InputFilePath);
        var headerValidation = HeaderContractValidator.Validate(ImportStage.HernanRaw, headers);

        if (!headerValidation.IsValid)
        {
            return HernanPreparationResult.ValidationFailure(
                headerValidation.Issues,
                headerValidation.Notices,
                headerValidation.PresentCanonicalFields,
                config.OutputColumns);
        }

        var configuredSourceHeaders = config.OutputColumns
            .Select(x => x.SourceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sourceToCanonicalTrimmed = headerValidation.SourceToCanonical
            .ToDictionary(
                kvp => kvp.Key.Trim(),
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase);

        var missingConfiguredHeaders = configuredSourceHeaders
            .Where(source => !sourceToCanonicalTrimmed.ContainsKey(source.Trim()))
            .ToArray();

        if (missingConfiguredHeaders.Length > 0)
        {
            var errors = missingConfiguredHeaders
                .Select(source => new ValidationIssue(
                    ValidationErrorCode.MissingRequiredHeader,
                    $"No se encontró el encabezado de origen configurado '{source}' en el archivo de entrada.",
                    Header: source))
                .ToArray();

            return HernanPreparationResult.ValidationFailure(
                errors,
                headerValidation.Notices,
                headerValidation.PresentCanonicalFields,
                config.OutputColumns);
        }

        var resolvedOutputColumns = config.OutputColumns
            .Select(mapping => new HernanResolvedOutputColumn(
                mapping.OutputName,
                sourceToCanonicalTrimmed[mapping.SourceName.Trim()]))
            .ToArray();

        var outputPaths = BuildOutputPaths(request);

        var batchInput = _excelRowSource
            .ReadHernanRows(request.InputFilePath, headerValidation.SourceToCanonical)
            .Select(row => new BatchCuilRowInput(row.SourceRowNumber, row.RawCuil))
            .ToList();

        var batchResult = BatchCuilValidator.Validate(batchInput);
        var rowResults = batchResult.Rows.ToDictionary(r => r.SourceRowNumber);

        _csvFileWriter.WritePaso1Outputs(
            outputPaths.SergioCsvPath,
            outputPaths.RejectedCsvPath,
            _excelRowSource.ReadHernanRows(request.InputFilePath, headerValidation.SourceToCanonical),
            resolvedOutputColumns,
            rowResults,
            sourceRowNumber => rowResults.TryGetValue(sourceRowNumber, out var result) && result.IsValid,
            sourceRowNumber => rowResults.TryGetValue(sourceRowNumber, out var result) && !result.IsValid);

        return HernanPreparationResult.Success(
            outputPaths.SergioCsvPath,
            outputPaths.RejectedCsvPath,
            config.OutputColumns,
            batchResult.Summary,
            headerValidation.Notices,
            headerValidation.PresentCanonicalFields);
    }

    private static (string SergioCsvPath, string RejectedCsvPath) BuildOutputPaths(HernanPreparationRequest request)
    {
        Directory.CreateDirectory(request.OutputDirectory);

        var inputBaseName = Path.GetFileNameWithoutExtension(request.InputFilePath);
        var safeBaseName = string.IsNullOrWhiteSpace(inputBaseName) ? "hernan_input" : inputBaseName;
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        var sergioFileName = string.IsNullOrWhiteSpace(request.SergioOutputFileName)
            ? $"{safeBaseName}_para_sergio_{timestamp}.csv"
            : request.SergioOutputFileName;

        var rejectedFileName = string.IsNullOrWhiteSpace(request.RejectedOutputFileName)
            ? $"{safeBaseName}_rechazados_{timestamp}.csv"
            : request.RejectedOutputFileName;

        return (
            Path.Combine(request.OutputDirectory, sergioFileName),
            Path.Combine(request.OutputDirectory, rejectedFileName));
    }
}
