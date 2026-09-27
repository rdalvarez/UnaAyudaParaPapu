using System.Globalization;
using DuckDB.NET.Data;

namespace PapaPersonas.V2.Shell;

public sealed class SergioStagingException : IOException
{
    public SergioStagingException(string message) : base(message) { }
    public SergioStagingException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class DuckDbPathPolicy
{
    private readonly string? explicitPath;

    public DuckDbPathPolicy(string? explicitPath = null) => this.explicitPath = explicitPath;

    public string GetDatabasePath()
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PapaPersonas", "V2", "papapersonas-v2.duckdb");
    }
}

public sealed record SergioStagingRequest(
    string WorkbookPath,
    SergioHeaderPreflightResult AcceptedHeaders,
    string SelectedPrimaryKeyAlias);

public sealed record SergioStagingResult(
    string CandidateTableName,
    IReadOnlyList<string> Aliases,
    string SelectedPrimaryKeyAlias,
    long TotalRows,
    long EmptyPrimaryKeyRows,
    long DuplicateGroups,
    long DuplicateRows,
    bool IsValid);

public static class SergioCellTextFormatter
{
    public static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        byte number => number.ToString(CultureInfo.InvariantCulture),
        sbyte number => number.ToString(CultureInfo.InvariantCulture),
        short number => number.ToString(CultureInfo.InvariantCulture),
        ushort number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        uint number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        ulong number => number.ToString(CultureInfo.InvariantCulture),
        float number => number % 1 == 0 ? number.ToString("0", CultureInfo.InvariantCulture) : number.ToString("R", CultureInfo.InvariantCulture),
        double number => number % 1 == 0 ? number.ToString("0", CultureInfo.InvariantCulture) : number.ToString("R", CultureInfo.InvariantCulture),
        decimal number => decimal.Truncate(number) == number ? number.ToString("0", CultureInfo.InvariantCulture) : number.ToString("G29", CultureInfo.InvariantCulture),
        DateTime dateTime => FormatDateTime(dateTime),
        TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static string FormatDateTime(DateTime value) => value.TimeOfDay == TimeSpan.Zero
        ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
}

public sealed class SergioStagingAnalyzer
{
    private readonly DuckDbPathPolicy pathPolicy;
    private readonly IWorkbookRowCursorFactory cursorFactory;
    private readonly HashSet<string> ownedCandidates = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public SergioStagingAnalyzer(DuckDbPathPolicy? pathPolicy = null, IWorkbookRowCursorFactory? cursorFactory = null)
    {
        this.pathPolicy = pathPolicy ?? new DuckDbPathPolicy();
        this.cursorFactory = cursorFactory ?? new ExcelDataReaderWorkbookRowCursorFactory();
    }

    public SergioStagingResult Analyze(SergioStagingRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var databasePath = pathPolicy.GetDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        string? candidate = null;

        try
        {
            using var cursor = cursorFactory.Open(request.WorkbookPath);
            ValidateHeaders(cursor.Headers, request.AcceptedHeaders);
            var aliases = request.AcceptedHeaders.Columns.Select(column => column.ProposedAlias).ToArray();
            candidate = CreateCandidateName();
            using var connection = Open(databasePath);
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, $"CREATE TABLE {Quote(candidate)} ({string.Join(", ", aliases.Select(alias => $"{Quote(alias)} VARCHAR"))})");

            using (var appender = connection.CreateAppender(candidate))
            {
                while (cursor.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (cursor.CurrentRow.Count > aliases.Length)
                        throw new SergioStagingException($"A workbook data row contains more fields than accepted headers.");

                    var values = new object?[aliases.Length];
                    for (var index = 0; index < values.Length; index++)
                        values[index] = index < cursor.CurrentRow.Count ? SergioCellTextFormatter.Format(cursor.CurrentRow[index]) : null;
                    appender.AppendRow(row =>
                    {
                        foreach (var value in values)
                            row.AppendValue(value as string);
                    });
                }
                appender.Close();
            }

            var metrics = ReadMetrics(connection, transaction, candidate, request.SelectedPrimaryKeyAlias);
            if (!metrics.IsValid)
            {
                Execute(connection, transaction, $"DROP TABLE {Quote(candidate)}");
                transaction.Commit();
                candidate = null;
            }
            else
            {
                transaction.Commit();
                lock (sync) ownedCandidates.Add(candidate);
            }

            return metrics with { CandidateTableName = candidate ?? string.Empty, Aliases = aliases, SelectedPrimaryKeyAlias = request.SelectedPrimaryKeyAlias };
        }
        catch (OperationCanceledException exception)
        {
            DropAttemptCandidate(databasePath, candidate);
            throw new SergioStagingException("Sergio staging was canceled.", exception);
        }
        catch (SergioStagingException)
        {
            DropAttemptCandidate(databasePath, candidate);
            throw;
        }
        catch (Exception exception)
        {
            DropAttemptCandidate(databasePath, candidate);
            throw new SergioStagingException("Sergio staging failed.", exception);
        }
    }

    public void Cleanup(string candidateTableName)
    {
        if (!IsOwnedName(candidateTableName)) return;
        try
        {
            using var connection = Open(pathPolicy.GetDatabasePath());
            Execute(connection, null, $"DROP TABLE IF EXISTS {Quote(candidateTableName)}");
            lock (sync) ownedCandidates.Remove(candidateTableName);
        }
        catch (Exception exception) { throw new SergioStagingException("Sergio staging cleanup failed.", exception); }
    }

    public void CleanupAll()
    {
        string[] candidates;
        lock (sync) candidates = ownedCandidates.ToArray();
        foreach (var candidate in candidates) Cleanup(candidate);
    }

    private string CreateCandidateName()
    {
        string candidate;
        do candidate = $"sergio_staging_{Guid.NewGuid():N}"; while (IsTableNameTaken(candidate));
        return candidate;
    }

    private bool IsTableNameTaken(string name)
    {
        using var connection = Open(pathPolicy.GetDatabasePath());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = ?";
        command.Parameters.Add(new DuckDBParameter { Value = name });
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static SergioStagingResult ReadMetrics(DuckDBConnection connection, DuckDBTransaction transaction, string table, string key)
    {
        var quotedKey = Quote(key);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) AS total, SUM(CASE WHEN {quotedKey} IS NULL OR TRIM({quotedKey}) = '' THEN 1 ELSE 0 END) AS empty_rows, COUNT(*) FILTER (WHERE {quotedKey} IS NOT NULL AND TRIM({quotedKey}) <> '' AND TRIM({quotedKey}) IN (SELECT TRIM({quotedKey}) FROM {Quote(table)} WHERE {quotedKey} IS NOT NULL AND TRIM({quotedKey}) <> '' GROUP BY TRIM({quotedKey}) HAVING COUNT(*) > 1)) AS duplicate_rows, (SELECT COUNT(*) FROM (SELECT TRIM({quotedKey}) FROM {Quote(table)} WHERE {quotedKey} IS NOT NULL AND TRIM({quotedKey}) <> '' GROUP BY TRIM({quotedKey}) HAVING COUNT(*) > 1)) AS duplicate_groups FROM {Quote(table)}";
        using var reader = command.ExecuteReader();
        reader.Read();
        var total = reader.GetInt64(0);
        var empty = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
        var duplicateRows = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
        var groups = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
        return new SergioStagingResult(table, [], key, total, empty, groups, duplicateRows, empty == 0 && groups == 0);
    }

    private static void ValidateRequest(SergioStagingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.WorkbookPath) || !File.Exists(request.WorkbookPath)) throw new SergioStagingException("The Sergio workbook must be an existing file.");
        if (request.AcceptedHeaders.Columns.Count == 0 || request.AcceptedHeaders.HasIssues) throw new SergioStagingException("Accepted Sergio headers must be non-empty and globally unique.");
        if (request.AcceptedHeaders.Columns.Count(column => string.Equals(column.ProposedAlias, request.SelectedPrimaryKeyAlias, StringComparison.Ordinal)) != 1) throw new SergioStagingException("The selected Sergio primary key must match exactly one accepted alias.");
    }

    private static void ValidateHeaders(IReadOnlyList<string?> actual, SergioHeaderPreflightResult accepted)
    {
        var expected = accepted.Columns.Select(column => column.RawName).ToArray();
        if (actual.Count != expected.Length || actual.Where((value, index) => !Equals(value, expected[index])).Any())
            throw new SergioStagingException("The workbook headers changed after acceptance.");
    }

    private static DuckDBConnection Open(string path) { var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString); connection.Open(); return connection; }
    private static void Execute(DuckDBConnection connection, DuckDBTransaction? transaction, string sql) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.ExecuteNonQuery(); }
    private static void DropAttemptCandidate(string databasePath, string? candidate)
    {
        if (candidate is null) return;
        try
        {
            using var connection = Open(databasePath);
            Execute(connection, null, $"DROP TABLE IF EXISTS {Quote(candidate)}");
        }
        catch { }
    }
    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
    private static bool IsOwnedName(string name) => name.StartsWith("sergio_staging_", StringComparison.Ordinal) && Guid.TryParseExact(name[15..], "N", out _);
}
