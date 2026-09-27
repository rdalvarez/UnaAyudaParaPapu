namespace PapaPersonas.V2.Shell;

public sealed record SergioDuckDbFooterPresentation(
    string StatusText,
    string DetailText,
    string PathText,
    string PathToolTip,
    string ResetButtonText,
    bool CanReset);

public static class SergioDuckDbFooterPresenter
{
    private const string ResetLabel = "Reiniciar base";

    public static SergioDuckDbFooterPresentation Initializing(string databasePath, bool isAppBusy, bool isStatusBusy)
    {
        return new SergioDuckDbFooterPresentation(
            "Base local: inicializando DuckDB...",
            "Se está validando la base local y su estado de Sergio.",
            CompactPath(databasePath),
            databasePath,
            ResetLabel,
            CanReset(isAppBusy, isStatusBusy));
    }

    public static SergioDuckDbFooterPresentation FromStatus(
        SergioDuckDbLifecycleStatus status,
        bool isAppBusy,
        bool isStatusBusy)
    {
        ArgumentNullException.ThrowIfNull(status);

        var warningSuffix = status.HasTransientSergioTablesWarning
            ? " Advertencia: se detectaron tablas transitorias de Sergio."
            : string.Empty;

        var (headline, detail) = status.Kind switch
        {
            SergioDuckDbLifecycleKind.ReadyWithoutOfficialLoad =>
                (
                    "Base local lista sin carga oficial de Sergio.",
                    "Todavía no existe una carga oficial; valide y publique un archivo de Sergio para crearla."
                ),
            SergioDuckDbLifecycleKind.ReadyWithOfficialLoad =>
                (
                    "Base local lista con carga oficial de Sergio.",
                    $"Archivo oficial: {SafeFileName(status.SourceFileName)}. Filas oficiales: {status.OfficialRowCount ?? 0}."
                ),
            SergioDuckDbLifecycleKind.Inconsistent =>
                (
                    "Base local en estado inconsistente.",
                    "Revise la última carga de Sergio o use Reiniciar base para crear una base limpia."
                ),
            SergioDuckDbLifecycleKind.Unavailable =>
                (
                    "Base local no disponible.",
                    "No se pudo abrir o inspeccionar la base local. Cierre procesos que la estén usando y vuelva a intentar."
                ),
            _ =>
                (
                    "Base local en estado desconocido.",
                    "Actualice el estado o reinicie la base local."
                )
        };

        return new SergioDuckDbFooterPresentation(
            headline,
            detail + warningSuffix,
            CompactPath(status.DatabasePath),
            status.DatabasePath,
            ResetLabel,
            CanReset(isAppBusy, isStatusBusy));
    }

    public static string CompactPath(string path, int maxChars = 72)
    {
        if (string.IsNullOrWhiteSpace(path)) return "(sin ruta)";
        var normalized = Path.GetFullPath(path);
        if (normalized.Length <= maxChars || maxChars < 8) return normalized;
        var side = (maxChars - 1) / 2;
        var tail = maxChars - 1 - side;
        return $"{normalized[..side]}…{normalized[^tail..]}";
    }

    private static bool CanReset(bool isAppBusy, bool isStatusBusy) => !isAppBusy && !isStatusBusy;

    private static string SafeFileName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "(sin archivo informado)";
        return Path.GetFileName(source);
    }
}
