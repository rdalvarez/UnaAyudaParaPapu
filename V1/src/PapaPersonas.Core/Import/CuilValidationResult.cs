namespace PapaPersonas.Core.Import;

public sealed record CuilValidationResult(
    bool IsValid,
    string? NormalizedCuil,
    ValidationIssue? Issue)
{
    public static CuilValidationResult Valid(string normalizedCuil) =>
        new(true, normalizedCuil, null);

    public static CuilValidationResult Invalid(ValidationIssue issue) =>
        new(false, null, issue);
}
