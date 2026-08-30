namespace PapaPersonas.Core.Import.Paso2;

public static class SergioUnknownHeadersConfirmation
{
    /// <summary>Construye el aviso estable que explica que las columnas desconocidas no se importarán.</summary>
    public static string BuildMessage(IReadOnlyList<string> unknownHeaders)
    {
        ArgumentNullException.ThrowIfNull(unknownHeaders);

        var headers = string.Join(Environment.NewLine, unknownHeaders.Select(header => $"• {header}"));
        return "El archivo contiene columnas desconocidas. Se ignorarán y no se guardarán en la base, el staging ni los metadatos de importación." +
               Environment.NewLine + Environment.NewLine +
               "Columnas que se ignorarán:" + Environment.NewLine +
               headers + Environment.NewLine + Environment.NewLine +
               "Seleccione Continuar para procesar únicamente las columnas reconocidas o Cancelar para detener el análisis.";
    }
}
