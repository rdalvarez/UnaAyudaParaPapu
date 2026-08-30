using PapaPersonas.Core.Import;

namespace PapaPersonas.Tests.Import;

public sealed class UserFacingExceptionMessageTests
{
    [Fact]
    public void WithTechnicalDetail_PreservesOriginalMessageExactlyAfterSpanishContext()
    {
        const string context = "No se pudo completar la operación.";
        const string originalMessage = "DuckDB: columna 'á' inválida\r\nCódigo=0xE1";

        var result = UserFacingExceptionMessage.WithTechnicalDetail(
            context,
            new InvalidOperationException(originalMessage));

        Assert.Equal($"{context} Detalle técnico: {originalMessage}", result);
        Assert.StartsWith(context + " ", result, StringComparison.Ordinal);
        Assert.EndsWith(originalMessage, result, StringComparison.Ordinal);
    }

    [Fact]
    public void WithTechnicalDetail_DoesNotAddExceptionTypeOrStackTrace()
    {
        var result = UserFacingExceptionMessage.WithTechnicalDetail(
            "No se pudo leer el archivo.",
            new IOException("archivo bloqueado"));

        Assert.Equal("No se pudo leer el archivo. Detalle técnico: archivo bloqueado", result);
        Assert.DoesNotContain(nameof(IOException), result, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", result, StringComparison.Ordinal);
    }
}
