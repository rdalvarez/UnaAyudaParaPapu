using PapaPersonas.Core.Import;

namespace PapaPersonas.Core.Import.Paso2;

public sealed record SergioStagePreviewSummary(
    int TotalRows,
    int ValidRows,
    int RejectedRows,
    int MissingCuilRows,
    int MalformedCuilRows,
    int DuplicateCuilRows,
    int RowsToInsert,
    int RowsToUpdate,
    int InvalidTypedValueRows);
