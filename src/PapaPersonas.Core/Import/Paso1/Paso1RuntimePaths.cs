namespace PapaPersonas.Core.Import.Paso1;

public sealed record Paso1RuntimePaths(string DefaultConfigPath, string DefaultOutputDirectory);

public static class Paso1RuntimePathResolver
{
    public static Paso1RuntimePaths Resolve(string appBaseDirectory, string documentsDirectory, string localAppDataDirectory)
    {
        var configPath = Path.Combine(appBaseDirectory, "config", "PARA_HERNAN.json");

        var outputRoot = !string.IsNullOrWhiteSpace(documentsDirectory)
            ? documentsDirectory
            : localAppDataDirectory;

        var outputDirectory = Path.Combine(outputRoot, "PapaPersonas", "outputs", "paso1");

        return new Paso1RuntimePaths(configPath, outputDirectory);
    }
}
