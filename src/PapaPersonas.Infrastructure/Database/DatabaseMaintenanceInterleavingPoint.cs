namespace PapaPersonas.Infrastructure.Database;

internal enum DatabaseMaintenanceInterleavingPoint
{
    RestoreSafetySnapshotCopied,
    BackupSnapshotCopied
}
