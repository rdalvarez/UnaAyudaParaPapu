namespace PapaPersonas.Core.Database;

public enum DatabaseMaintenanceFailureCode
{
    None = 0,
    InvalidPath = 1,
    SourceMissing = 2,
    SourceLocked = 3,
    SourceEmpty = 4,
    SourceNotRecognized = 5,
    UnsupportedSchema = 6,
    LiveDatabaseLocked = 7,
    OperationFailed = 8
}
