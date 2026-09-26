using PapaPersonas.Core.Import;

namespace PapaPersonas.Core.Import.Paso2;

public sealed record Paso2OutputDirectoryPreflightResult(bool IsSuccess, string? ErrorMessage);

public static class Paso2OutputDirectoryPreflight
{
    public static Paso2OutputDirectoryPreflightResult ValidateWritable(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return new Paso2OutputDirectoryPreflightResult(false, "Se requiere la carpeta de salida.");
        }

        string? probePath = null;
        var writeSucceeded = false;

        try
        {
            Directory.CreateDirectory(outputDirectory);

            var probeName = $".pp-write-probe-{Guid.NewGuid():N}.tmp";
            probePath = Path.Combine(outputDirectory, probeName);

            File.WriteAllText(probePath, "ok");
            writeSucceeded = true;
        }
        catch (Exception ex)
        {
            return new Paso2OutputDirectoryPreflightResult(
                false,
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "La carpeta de salida seleccionada no permite escritura. Elegí otra carpeta y volvé a intentar.",
                    ex));
        }
        finally
        {
            // Cleanup is scoped to this invocation-only probe path.
            if (!string.IsNullOrWhiteSpace(probePath) && File.Exists(probePath))
            {
                try
                {
                    File.Delete(probePath);
                }
                catch
                {
                    // handled after finally via write/delete safety check.
                }
            }
        }

        if (!writeSucceeded || string.IsNullOrWhiteSpace(probePath) || File.Exists(probePath))
        {
            return new Paso2OutputDirectoryPreflightResult(
                false,
                "La carpeta de salida seleccionada no permite escritura. Elegí otra carpeta y volvé a intentar.");
        }

        return new Paso2OutputDirectoryPreflightResult(true, null);
    }
}
