namespace PapaPersonas.V2.Shell;

public static class SergioLoadPreviewText
{
    public static string Summary(SergioPreview preview) => $"Archivo: {preview.SourceFileName}. Importadas: {preview.ImportedRows}. Válidas a cargar: {preview.ValidRows}. Rechazadas excluidas: {preview.RejectedRows}. Columna PK: {preview.SelectedPrimaryKeyAlias}. " + (preview.PreviousMetadata is null ? "No existe una carga oficial previa." : $"Carga oficial previa: {preview.PreviousMetadata.SourceFileName}, {preview.PreviousMetadata.RowCount} filas, {preview.PreviousMetadata.LoadedAtUtc.ToLocalTime():g}.");
    public static string Warning(SergioPreview preview) => preview.PreviousOfficialExists ? "Esta acción reemplazará la carga oficial sergio_current. El reemplazo es atómico y se revierte si ocurre un error." : "La carga se publicará como la carga oficial sergio_current.";
    public static string Action(SergioPreview preview) => preview.PreviousOfficialExists ? "Reemplazar" : "Cargar";
}
