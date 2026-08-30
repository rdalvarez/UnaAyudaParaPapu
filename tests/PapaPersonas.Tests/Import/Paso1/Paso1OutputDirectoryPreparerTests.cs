using PapaPersonas.Core.Import.Paso1;

namespace PapaPersonas.Tests.Import.Paso1;

public sealed class Paso1OutputDirectoryPreparerTests
{
    [Fact]
    public void TryPrepare_WhenCreateDirectorySucceeds_ReturnsSuccess()
    {
        var result = Paso1OutputDirectoryPreparer.TryPrepare("ignored", _ => { });

        Assert.True(result.IsSuccess);
        Assert.Null(result.StatusMessage);
        Assert.Null(result.SummaryMessage);
    }

    [Fact]
    public void TryPrepare_WhenUnauthorizedAccess_ReturnsFriendlyMessage()
    {
        var result = Paso1OutputDirectoryPreparer.TryPrepare("ignored", _ => throw new UnauthorizedAccessException("write denied"));

        Assert.False(result.IsSuccess);
        Assert.Equal("La carpeta de salida no permite escritura. Detalle técnico: write denied", result.StatusMessage);
        Assert.Equal("Seleccioná otra carpeta de salida con permisos de escritura y ejecutá Paso 1 nuevamente. Detalle técnico: write denied", result.SummaryMessage);
    }

    [Fact]
    public void TryPrepare_WhenInvalidPath_ReturnsFriendlyMessage()
    {
        var result = Paso1OutputDirectoryPreparer.TryPrepare("ignored", _ => throw new ArgumentException("bad path"));

        Assert.False(result.IsSuccess);
        Assert.Equal("La ruta de la carpeta de salida no es válida. Detalle técnico: bad path", result.StatusMessage);
    }

    [Fact]
    public void TryPrepare_WhenPathTooLong_ReturnsFriendlyMessage()
    {
        var result = Paso1OutputDirectoryPreparer.TryPrepare("ignored", _ => throw new PathTooLongException("path limit exceeded"));

        Assert.False(result.IsSuccess);
        Assert.Equal("La ruta de la carpeta de salida es demasiado larga. Detalle técnico: path limit exceeded", result.StatusMessage);
    }

    [Fact]
    public void TryPrepare_WhenIoFailure_ReturnsFriendlyMessage()
    {
        var result = Paso1OutputDirectoryPreparer.TryPrepare("ignored", _ => throw new IOException("locked"));

        Assert.False(result.IsSuccess);
        Assert.Equal("La carpeta de salida no está disponible. Detalle técnico: locked", result.StatusMessage);
    }
}
