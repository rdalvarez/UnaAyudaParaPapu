namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4ColumnSelectionState(
    IReadOnlyList<string> SelectedColumns,
    IReadOnlyList<string> Warnings);
