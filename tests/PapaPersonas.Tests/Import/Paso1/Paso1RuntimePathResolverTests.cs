using PapaPersonas.Core.Import.Paso1;

namespace PapaPersonas.Tests.Import.Paso1;

public sealed class Paso1RuntimePathResolverTests
{
    [Fact]
    public void Resolve_UsesDocumentsDirectoryWhenProvided()
    {
        var result = Paso1RuntimePathResolver.Resolve(
            appBaseDirectory: @"C:\app\bin",
            documentsDirectory: @"C:\Users\dad\Documents",
            localAppDataDirectory: @"C:\Users\dad\AppData\Local");

        Assert.Equal(@"C:\app\bin\config\PARA_HERNAN.json", result.DefaultConfigPath);
        Assert.Equal(@"C:\Users\dad\Documents\PapaPersonas\outputs\paso1", result.DefaultOutputDirectory);
    }

    [Fact]
    public void Resolve_FallsBackToLocalAppDataWhenDocumentsMissing()
    {
        var result = Paso1RuntimePathResolver.Resolve(
            appBaseDirectory: @"C:\app\bin",
            documentsDirectory: " ",
            localAppDataDirectory: @"C:\Users\dad\AppData\Local");

        Assert.Equal(@"C:\app\bin\config\PARA_HERNAN.json", result.DefaultConfigPath);
        Assert.Equal(@"C:\Users\dad\AppData\Local\PapaPersonas\outputs\paso1", result.DefaultOutputDirectory);
    }
}
