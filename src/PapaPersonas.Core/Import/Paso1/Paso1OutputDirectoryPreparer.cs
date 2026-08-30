using PapaPersonas.Core.Import;

namespace PapaPersonas.Core.Import.Paso1;

public sealed record DirectoryPreparationResult(bool IsSuccess, string? StatusMessage = null, string? SummaryMessage = null);

public static class Paso1OutputDirectoryPreparer
{
    public static DirectoryPreparationResult TryPrepare(string outputDirectory, Action<string>? createDirectory = null)
    {
        var createDirectoryAction = createDirectory ?? (path => Directory.CreateDirectory(path));

        try
        {
            createDirectoryAction(outputDirectory);
            return new DirectoryPreparationResult(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new DirectoryPreparationResult(
                false,
                UserFacingExceptionMessage.WithTechnicalDetail("La carpeta de salida no permite escritura.", ex),
                UserFacingExceptionMessage.WithTechnicalDetail("Seleccioná otra carpeta de salida con permisos de escritura y ejecutá Paso 1 nuevamente.", ex));
        }
        catch (ArgumentException ex)
        {
            return new DirectoryPreparationResult(
                false,
                UserFacingExceptionMessage.WithTechnicalDetail("La ruta de la carpeta de salida no es válida.", ex),
                UserFacingExceptionMessage.WithTechnicalDetail("Ingresá una ruta válida para la carpeta de salida y ejecutá Paso 1 nuevamente.", ex));
        }
        catch (NotSupportedException ex)
        {
            return new DirectoryPreparationResult(
                false,
                UserFacingExceptionMessage.WithTechnicalDetail("La ruta de la carpeta de salida no es válida.", ex),
                UserFacingExceptionMessage.WithTechnicalDetail("Ingresá una ruta válida para la carpeta de salida y ejecutá Paso 1 nuevamente.", ex));
        }
        catch (PathTooLongException ex)
        {
            return new DirectoryPreparationResult(
                false,
                UserFacingExceptionMessage.WithTechnicalDetail("La ruta de la carpeta de salida es demasiado larga.", ex),
                UserFacingExceptionMessage.WithTechnicalDetail("Usá una ruta más corta y ejecutá Paso 1 nuevamente.", ex));
        }
        catch (IOException ex)
        {
            return new DirectoryPreparationResult(
                false,
                UserFacingExceptionMessage.WithTechnicalDetail("La carpeta de salida no está disponible.", ex),
                UserFacingExceptionMessage.WithTechnicalDetail("Cerrá otras aplicaciones que puedan bloquearla o elegí otra ubicación, y ejecutá Paso 1 nuevamente.", ex));
        }
    }
}
