namespace PapaPersonas.Core.Database;

public enum DatabaseMaintenanceOperation
{
    Restore,
    Reset
}

public static class DatabaseMaintenanceRefreshPolicy
{
    public static bool RequestsPendingDialog(DatabaseMaintenanceOperation operation) =>
        operation == DatabaseMaintenanceOperation.Restore;
}
