namespace PapaPersonas.Core.Stock.Paso4;

public interface IPaso4ObraSocialCatalogStore
{
    Paso4ObraSocialCatalogState Load();

    void Save(IReadOnlyDictionary<string, string> namesByNormalizedCode);
}
