namespace PapaPersonas.Core.Import;

public static class UserFacingExceptionMessage
{
    public static string WithTechnicalDetail(string spanishContext, Exception exception) =>
        $"{spanishContext} Detalle técnico: {exception.Message}";
}
