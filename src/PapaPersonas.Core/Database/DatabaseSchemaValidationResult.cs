namespace PapaPersonas.Core.Database;

public enum DatabaseSchemaValidationCode
{
    MissingTable,
    UnexpectedTable,
    MissingColumn,
    UnexpectedColumn,
    ColumnTypeMismatch,
    ColumnNullabilityMismatch,
    PrimaryKeyMismatch,
    UniqueConstraintMismatch,
    ForeignKeyMismatch,
    CheckConstraintMismatch
}

public sealed record DatabaseSchemaValidationIssue(
    DatabaseSchemaValidationCode Code,
    string TableName,
    string Message,
    string? ColumnName = null,
    string? Expected = null,
    string? Actual = null);

public sealed record DatabaseSchemaValidationResult(
    bool IsSuccess,
    IReadOnlyList<DatabaseSchemaValidationIssue> Issues)
{
    public static DatabaseSchemaValidationResult Success() => new(true, Array.Empty<DatabaseSchemaValidationIssue>());

    public static DatabaseSchemaValidationResult Failure(IReadOnlyList<DatabaseSchemaValidationIssue> issues) =>
        new(false, issues);

    public DatabaseSchemaValidationIssue? FirstIssue => Issues.Count == 0 ? null : Issues[0];

    public string Message => IsSuccess
        ? "El esquema de la base es válido."
        : Issues.Count == 1
            ? Issues[0].Message
            : Issues.Count == 2
                ? $"{Issues[0].Message} (y 1 problema adicional)."
                : $"{Issues[0].Message} (y {Issues.Count - 1} problemas adicionales).";
}
