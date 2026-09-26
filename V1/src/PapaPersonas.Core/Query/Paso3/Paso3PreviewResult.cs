namespace PapaPersonas.Core.Query.Paso3;

public sealed record Paso3PreviewResult(
    IReadOnlyList<Paso3PreviewRow> Rows,
    int TotalCount,
    DateOnly? LatestImportDate);
