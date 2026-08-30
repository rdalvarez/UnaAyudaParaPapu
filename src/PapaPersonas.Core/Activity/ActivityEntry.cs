namespace PapaPersonas.Core.Activity;

public sealed record ActivityEntry(
    DateTimeOffset Timestamp,
    ActivitySeverity Severity,
    string Phase,
    string Message);
