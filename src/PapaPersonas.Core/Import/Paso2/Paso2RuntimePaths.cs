namespace PapaPersonas.Core.Import.Paso2;

public sealed record Paso2RuntimePaths(string DefaultRejectedOutputDirectory);

public static class Paso2RuntimePathResolver
{
    public static Paso2RuntimePaths Resolve(string documentsDirectory, string localAppDataDirectory)
    {
        var root = !string.IsNullOrWhiteSpace(documentsDirectory)
            ? documentsDirectory
            : localAppDataDirectory;

        var outputDirectory = Path.Combine(root, "PapaPersonas", "Proceso2");
        return new Paso2RuntimePaths(outputDirectory);
    }
}
