namespace PapaPersonas.Core.Import.Paso1;

public static class HernanConfigValidator
{
    /// <summary>Valida que la configuración de salida tenga mapeos completos y conserve el CUIL obligatorio.</summary>
    public static HernanConfigValidationResult Validate(HernanPaso1Config? config)
    {
        var errors = new List<string>();

        if (config is null)
        {
            errors.Add("Falta el objeto de configuración.");
            return new HernanConfigValidationResult(false, errors);
        }

        if (config.OutputColumns.Count == 0)
        {
            errors.Add("Se requiere al menos un mapeo de columna de salida.");
            return new HernanConfigValidationResult(false, errors);
        }

        var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in config.OutputColumns)
        {
            if (string.IsNullOrWhiteSpace(mapping.OutputName))
            {
                errors.Add("OutputName no puede estar vacío.");
            }

            if (string.IsNullOrWhiteSpace(mapping.SourceName))
            {
                errors.Add($"SourceName no puede estar vacío para la salida '{mapping.OutputName}'.");
            }

            if (!outputNames.Add(mapping.OutputName))
            {
                errors.Add($"No se permite repetir OutputName '{mapping.OutputName}'.");
            }

            sourceNames.Add(mapping.SourceName);
        }

        var cuilOutput = config.OutputColumns.Any(m =>
            m.OutputName.Equals("CUIL", StringComparison.OrdinalIgnoreCase)
            && m.SourceName.Equals("CUIL", StringComparison.OrdinalIgnoreCase));

        if (!cuilOutput)
        {
            errors.Add("La configuración debe incluir el mapeo 'CUIL' <- 'CUIL'.");
        }

        return new HernanConfigValidationResult(errors.Count == 0, errors);
    }
}
