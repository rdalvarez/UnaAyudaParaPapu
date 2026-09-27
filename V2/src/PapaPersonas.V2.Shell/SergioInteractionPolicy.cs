namespace PapaPersonas.V2.Shell;

public sealed record SergioStagingPresentation(
    string SelectedPrimaryKey,
    long ImportedRows,
    long ValidRows,
    long EmptyRows,
    long DuplicateGroups,
    long DuplicateRows,
    long RejectedRows)
{
    public bool DecisionRequired => RejectedRows > 0 && ValidRows > 0;
    public bool CanPreviewOrPromote => ValidRows > 0;
    public bool CanExport => RejectedRows > 0;
}

public static class SergioInteractionPolicy
{
    public static SergioStagingPresentation For(SergioStagingResult result) => new(
        result.SelectedPrimaryKeyAlias,
        result.TotalRows,
        result.ValidRows,
        result.EmptyPrimaryKeyRows,
        result.DuplicateGroups,
        result.DuplicateRows,
        result.TotalRows - result.ValidRows);

    public static bool ContinueAutomatically(SergioStagingPresentation presentation) => !presentation.DecisionRequired;

    public static string RejectionDecisionMessage(SergioStagingPresentation presentation) =>
        $"Clave primaria seleccionada: {presentation.SelectedPrimaryKey}. " +
        $"Importadas: {presentation.ImportedRows}. Válidas: {presentation.ValidRows}. " +
        $"Filas con clave vacía: {presentation.EmptyRows}. Grupos duplicados: {presentation.DuplicateGroups}. " +
        $"Filas duplicadas: {presentation.DuplicateRows}. Total rechazadas: {presentation.RejectedRows}. " +
        $"Todas las filas rechazadas serán excluidas. ¿Desea continuar?";

    public static string ZeroValidStatusMessage(SergioStagingPresentation presentation) =>
        presentation.RejectedRows > 0
            ? "No quedan filas válidas. No se puede continuar ni promover; puede exportar los rechazos."
            : "El libro no contiene filas de datos. No se puede previsualizar ni promover y no hay nada que exportar.";
}
