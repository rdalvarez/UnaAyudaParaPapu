namespace PapaPersonas.Core.Database;

public static class DatabaseMaintenanceConfirmation
{
    public static readonly string ExternalToolsWarning =
        "Antes de continuar, cierre DBeaver y cualquier otra herramienta externa que tenga abierta la base DuckDB. La operación necesita acceso exclusivo y no cerrará programas automáticamente.";

    public static readonly string BackupPrompt =
        ExternalToolsWarning + Environment.NewLine + Environment.NewLine +
        "Se creará una copia únicamente del archivo DuckDB. ¿Desea continuar?";

    public static readonly string RestorePrompt =
        ExternalToolsWarning + Environment.NewLine + Environment.NewLine +
        "La restauración reemplazará TODO el estado operativo: personas, importaciones, stock, ventas y extracciones. Se conservará un resguardo automático de la base actual. ¿Desea continuar?";
}
