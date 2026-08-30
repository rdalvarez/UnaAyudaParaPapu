using System.Security.Cryptography;
using System.Text;

namespace PapaPersonas.Core.Import;

public static class HeaderContractValidator
{
    private const string RequiredCuilCanonical = "cuil";

    /// <summary>Valida encabezados, detecta colisiones y devuelve su correspondencia canónica para la etapa indicada.</summary>
    public static HeaderValidationResult Validate(
        ImportStage stage,
        IEnumerable<string?> sourceHeaders,
        bool allowUnknownHeaders = false)
    {
        ArgumentNullException.ThrowIfNull(sourceHeaders);

        var headerMap = HeaderContracts.GetHeaderToCanonicalMap(stage);
        var issues = new List<ValidationIssue>();
        var notices = new List<ValidationIssue>();
        var sourceToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canonicalToSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenNormalizedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSourceByNormalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unknownSourceHeaders = new List<string>();

        foreach (var rawHeader in sourceHeaders)
        {
            var normalizedHeader = NormalizeHeader(rawHeader);
            var sourceHeader = rawHeader ?? string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedHeader))
            {
                issues.Add(new ValidationIssue(
                    ValidationErrorCode.UnknownHeader,
                    "No se admite un encabezado vacío.",
                    Header: sourceHeader));
                continue;
            }

            if (!seenNormalizedHeaders.Add(normalizedHeader))
            {
                issues.Add(new ValidationIssue(
                    ValidationErrorCode.DuplicateSourceHeader,
                    $"El encabezado de origen '{sourceHeader}' está duplicado.",
                    Header: sourceHeader));
                continue;
            }

            seenSourceByNormalized[normalizedHeader] = sourceHeader;

            if (!headerMap.TryGetValue(normalizedHeader, out var canonicalField))
            {
                unknownSourceHeaders.Add(sourceHeader);
                if (stage == ImportStage.HernanRaw)
                {
                    notices.Add(new ValidationIssue(
                        ValidationErrorCode.UnknownHeader,
                         $"El encabezado de Hernán '{sourceHeader}' no está mapeado y se ignora durante la preparación.",
                        Header: sourceHeader));
                }
                else if (!allowUnknownHeaders)
                {
                    issues.Add(new ValidationIssue(
                        ValidationErrorCode.UnknownHeader,
                         $"El encabezado de origen '{sourceHeader}' es desconocido.",
                        Header: sourceHeader));
                }

                continue;
            }

            if (canonicalToSource.TryGetValue(canonicalField, out var previousSourceHeader))
            {
                issues.Add(new ValidationIssue(
                    ValidationErrorCode.CanonicalHeaderCollision,
                    $"Los encabezados '{previousSourceHeader}' y '{sourceHeader}' apuntan al mismo campo canónico '{canonicalField}'.",
                    Header: sourceHeader,
                    CanonicalField: canonicalField));
                continue;
            }

            sourceToCanonical[sourceHeader] = canonicalField;
            canonicalToSource[canonicalField] = sourceHeader;
        }

        if (!canonicalToSource.ContainsKey(RequiredCuilCanonical))
        {
            issues.Add(new ValidationIssue(
                ValidationErrorCode.MissingRequiredHeader,
                "Falta el encabezado obligatorio 'CUIL'.",
                Header: "CUIL",
                CanonicalField: RequiredCuilCanonical));
        }

        if (stage == ImportStage.HernanRaw)
        {
            foreach (var requiredHeader in HeaderContracts.HernanPreparationRequiredHeaders)
            {
                if (requiredHeader.Equals("CUIL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seenSourceByNormalized.ContainsKey(requiredHeader))
                {
                    continue;
                }

                issues.Add(new ValidationIssue(
                    ValidationErrorCode.MissingRequiredHeader,
                     $"Falta el encabezado obligatorio de preparación '{requiredHeader}'.",
                    Header: requiredHeader));
            }
        }

        var presentCanonicalFields = sourceToCanonical.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new HeaderValidationResult(
            issues.Count == 0,
            stage,
            sourceToCanonical,
            presentCanonicalFields,
            issues,
            notices,
            unknownSourceHeaders);
    }

    /// <summary>Calcula una huella del orden y contenido exactos de los encabezados de origen.</summary>
    public static string GetSourceShapeFingerprint(IEnumerable<string?> sourceHeaders)
    {
        ArgumentNullException.ThrowIfNull(sourceHeaders);

        var shape = string.Join("\u001f", sourceHeaders.Select(header => header ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shape)));
    }

    private static string NormalizeHeader(string? header)
    {
        return (header ?? string.Empty).Trim();
    }
}
