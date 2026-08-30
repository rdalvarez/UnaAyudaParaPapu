namespace PapaPersonas.Core.Stock.Paso4;

public interface IPaso4ColumnSelectionStore
{
    Paso4ColumnSelectionState Load();

    void Save(IReadOnlyList<string> selectedColumns);
}
