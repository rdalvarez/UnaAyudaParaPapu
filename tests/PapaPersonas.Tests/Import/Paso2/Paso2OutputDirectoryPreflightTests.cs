using PapaPersonas.Core.Import.Paso2;

namespace PapaPersonas.Tests.Import.Paso2;

public sealed class Paso2OutputDirectoryPreflightTests
{
    [Fact]
    public void ValidateWritable_ExistingWritableDirectory_SucceedsAndCleansProbe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pp-p2-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var result = Paso2OutputDirectoryPreflight.ValidateWritable(dir);

            Assert.True(result.IsSuccess);
            Assert.Null(result.ErrorMessage);
            Assert.Empty(Directory.GetFiles(dir, ".pp-write-probe-*.tmp"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void ValidateWritable_InvalidPath_FailsWithFriendlyMessage()
    {
        var invalidPath = "C:\\bad\0path";
        var expectedTechnicalMessage = Assert.Throws<ArgumentException>(() => Directory.CreateDirectory(invalidPath)).Message;

        var result = Paso2OutputDirectoryPreflight.ValidateWritable(invalidPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            $"La carpeta de salida seleccionada no permite escritura. Elegí otra carpeta y volvé a intentar. Detalle técnico: {expectedTechnicalMessage}",
            result.ErrorMessage);
    }
}
