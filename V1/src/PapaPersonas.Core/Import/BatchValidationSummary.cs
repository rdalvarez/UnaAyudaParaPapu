namespace PapaPersonas.Core.Import;

public sealed record BatchValidationSummary(
    int TotalRows,
    int ValidRows,
    int RejectedRows,
    int MissingCuilRows,
    int MalformedCuilRows,
    int DuplicateCuilRows);
