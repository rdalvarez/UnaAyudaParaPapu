namespace PapaPersonas.Core.Query.Paso3;

public static class Paso3PreviewDefaults
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;
}

public sealed record Paso3PreviewRequest(
    string DatabasePath,
    string? ExactCuil,
    IReadOnlyList<Paso3Filter> Filters,
    DateOnly? ImportDateFrom,
    DateOnly? ImportDateTo,
    IReadOnlyList<string> SelectedColumns,
    int PageNumber,
    int PageSize);
