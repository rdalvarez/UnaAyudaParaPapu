namespace PapaPersonas.Core.Stock.Paso4;

public sealed record Paso4ObraSocialCatalogState(
    IReadOnlyDictionary<string, string> NamesByNormalizedCode,
    IReadOnlyList<string> Warnings);
