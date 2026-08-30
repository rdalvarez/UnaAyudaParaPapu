using PapaPersonas.Core.Import.Paso2;

namespace PapaPersonas.Tests.Import.Paso2;

public sealed class Paso2RuntimePathResolverTests
{
    [Fact]
    public void Resolve_UsesDocumentsDirectoryWhenProvided()
    {
        var result = Paso2RuntimePathResolver.Resolve(
            documentsDirectory: @"C:\Users\dad\Documents",
            localAppDataDirectory: @"C:\Users\dad\AppData\Local");

        Assert.Equal(@"C:\Users\dad\Documents\PapaPersonas\Proceso2", result.DefaultRejectedOutputDirectory);
    }

    [Fact]
    public void Resolve_FallsBackToLocalAppDataWhenDocumentsMissing()
    {
        var result = Paso2RuntimePathResolver.Resolve(
            documentsDirectory: " ",
            localAppDataDirectory: @"C:\Users\dad\AppData\Local");

        Assert.Equal(@"C:\Users\dad\AppData\Local\PapaPersonas\Proceso2", result.DefaultRejectedOutputDirectory);
    }
}
