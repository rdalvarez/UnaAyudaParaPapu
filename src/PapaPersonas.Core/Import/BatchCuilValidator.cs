namespace PapaPersonas.Core.Import;

public static class BatchCuilValidator
{
    // TODO: Mantener determinista la devolución por fila al conectar el importador al streaming.
    // La implementación actual materializa los resultados en memoria por simplicidad.
    /// <summary>Valida cada CUIL, rechaza duplicados normalizados y resume el resultado de todo el lote.</summary>
    public static BatchCuilValidationResult Validate(IEnumerable<BatchCuilRowInput> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var results = new List<BatchCuilRowResult>();
        var validGroups = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var validation = CuilValidator.Validate(row.RawCuil, row.SourceRowNumber);
            if (!validation.IsValid)
            {
                results.Add(new BatchCuilRowResult(
                    row.SourceRowNumber,
                    row.RawCuil,
                    null,
                    false,
                    false,
                    validation.Issue));
                continue;
            }

            var normalizedCuil = validation.NormalizedCuil!;
            var resultIndex = results.Count;
            results.Add(new BatchCuilRowResult(
                row.SourceRowNumber,
                row.RawCuil,
                normalizedCuil,
                true,
                false,
                null));

            if (!validGroups.TryGetValue(normalizedCuil, out var indices))
            {
                indices = [];
                validGroups[normalizedCuil] = indices;
            }

            indices.Add(resultIndex);
        }

        foreach (var group in validGroups.Values.Where(g => g.Count > 1))
        {
            foreach (var index in group)
            {
                var current = results[index];
                results[index] = current with
                {
                    IsValid = false,
                    IsDuplicate = true,
                    Issue = new ValidationIssue(
                        ValidationErrorCode.DuplicateCuilInBatch,
                        "Hay un CUIL duplicado en el lote.",
                        SourceRowNumber: current.SourceRowNumber,
                        RawValue: current.RawCuil)
                };
            }
        }

        var totalRows = results.Count;
        var validRows = results.Count(r => r.IsValid);
        var missingRows = results.Count(r => r.Issue?.Code == ValidationErrorCode.MissingCuil);
        var malformedRows = results.Count(r =>
            r.Issue?.Code is ValidationErrorCode.InvalidCuilCharacters or ValidationErrorCode.InvalidCuilLength);
        var duplicateRows = results.Count(r => r.Issue?.Code == ValidationErrorCode.DuplicateCuilInBatch);
        var rejectedRows = totalRows - validRows;

        return new BatchCuilValidationResult(
            results,
            new BatchValidationSummary(
                totalRows,
                validRows,
                rejectedRows,
                missingRows,
                malformedRows,
                duplicateRows));
    }
}
