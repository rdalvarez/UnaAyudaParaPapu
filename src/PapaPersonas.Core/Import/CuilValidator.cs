namespace PapaPersonas.Core.Import;

public static class CuilValidator
{
    /// <summary>Normaliza y valida un CUIL, conservando el motivo de rechazo cuando no cumple el formato.</summary>
    public static CuilValidationResult Validate(string? rawCuil, int? sourceRowNumber = null)
    {
        if (string.IsNullOrWhiteSpace(rawCuil))
        {
            return CuilValidationResult.Invalid(new ValidationIssue(
                ValidationErrorCode.MissingCuil,
                "Se requiere el CUIL.",
                SourceRowNumber: sourceRowNumber,
                RawValue: rawCuil));
        }

        var normalizedChars = new List<char>(11);
        foreach (var ch in rawCuil)
        {
            if (ch is >= '0' and <= '9')
            {
                normalizedChars.Add(ch);
                continue;
            }

            if (ch == '-' || ch == ' ')
            {
                continue;
            }

            return CuilValidationResult.Invalid(new ValidationIssue(
                ValidationErrorCode.InvalidCuilCharacters,
                "El CUIL contiene caracteres no válidos.",
                SourceRowNumber: sourceRowNumber,
                RawValue: rawCuil));
        }

        if (normalizedChars.Count != 11)
        {
            return CuilValidationResult.Invalid(new ValidationIssue(
                ValidationErrorCode.InvalidCuilLength,
                "El CUIL debe quedar normalizado en exactamente 11 dígitos.",
                SourceRowNumber: sourceRowNumber,
                RawValue: rawCuil));
        }

        return CuilValidationResult.Valid(new string(normalizedChars.ToArray()));
    }
}
