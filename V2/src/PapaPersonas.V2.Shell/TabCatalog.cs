namespace PapaPersonas.V2.Shell;

public sealed record TabDefinition(string Title, bool IsEnabled);

public static class TabCatalog
{
    public static IReadOnlyList<TabDefinition> All { get; } =
    [
        new("Preparar archivo de Hernán", true),
        new("Ingresar archivo de Sergio", false),
        new("Generación y consulta de stock", false),
        new("Consultas (opcional)", false)
    ];
}
