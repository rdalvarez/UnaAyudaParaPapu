using System.Globalization;
using DuckDB.NET.Data;

namespace PapaPersonas.V2.Shell;

public sealed class SergioStagingException : IOException
{
    public Exception? CleanupException { get; }

    public SergioStagingException(string message) : base(message) { }
    public SergioStagingException(string message, Exception innerException) : base(message, innerException) { }
    public SergioStagingException(string message, Exception innerException, Exception cleanupException)
        : base(message, innerException) => CleanupException = cleanupException;
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
    string TechnicalReasonColumnName,
    long TotalRows,
    long ValidRows,
    long EmptyPrimaryKeyRows,
    long DuplicateGroups,
    long DuplicateRows,
    bool IsValid,
    bool HasRejections);

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
    private readonly Dictionary<string, CandidateDescriptor> ownedCandidates = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public SergioStagingAnalyzer(DuckDbPathPolicy? pathPolicy = null, IWorkbookRowCursorFactory? cursorFactory = null)
    {
        this.pathPolicy = pathPolicy ?? new DuckDbPathPolicy();
        this.cursorFactory = cursorFactory ?? new ExcelDataReaderWorkbookRowCursorFactory();
    }

    public SergioStagingResult Analyze(SergioStagingRequest request, CancellationToken cancellationToken = default)
    {
        lock (sync) return AnalyzeCore(request, cancellationToken);
    }

    private SergioStagingResult AnalyzeCore(SergioStagingRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        CleanupStaleCandidatesOnce();
        var databasePath = pathPolicy.GetDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        string? candidate = null;

        try
        {
            using var cursor = cursorFactory.Open(request.WorkbookPath);
            ValidateHeaders(cursor.Headers, request.AcceptedHeaders);
            var acceptedAliases = request.AcceptedHeaders.Columns.Select(column => column.ProposedAlias).ToArray();
            var acceptedColumns = request.AcceptedHeaders.Columns.ToDictionary(column => column.ProposedAlias, StringComparer.Ordinal);
            var reasonColumn = SergioSchemaNames.RejectionReason;
            var schemaAliases = acceptedAliases.Contains(reasonColumn, StringComparer.Ordinal) ? acceptedAliases : [.. acceptedAliases, reasonColumn];
            var aliases = acceptedAliases.Where(alias => !string.Equals(alias, reasonColumn, StringComparison.Ordinal)).ToArray();
            candidate = CreateCandidateName();
            lock (sync) ownedCandidates.Add(candidate, new CandidateDescriptor(
                Path.GetFullPath(request.WorkbookPath), acceptedAliases, request.SelectedPrimaryKeyAlias, reasonColumn));
            using var connection = Open(databasePath);
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, $"CREATE TABLE {Quote(candidate)} ({string.Join(", ", schemaAliases.Select(alias => $"{Quote(alias)} VARCHAR"))})");

            using (var appender = connection.CreateAppender(candidate))
            {
                while (cursor.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (cursor.CurrentRow.Count > acceptedAliases.Length)
                        throw new SergioStagingException($"A workbook data row contains more fields than accepted headers.");

                    var values = new object?[schemaAliases.Length];
                    for (var index = 0; index < values.Length; index++)
                    {
                        if (string.Equals(schemaAliases[index], reasonColumn, StringComparison.Ordinal))
                        {
                            values[index] = null;
                            continue;
                        }

                        var sourcePosition = acceptedColumns[schemaAliases[index]].Position;
                        values[index] = sourcePosition < cursor.CurrentRow.Count ? SergioCellTextFormatter.Format(cursor.CurrentRow[sourcePosition]) : null;
                    }
                    appender.AppendRow(row =>
                    {
                        foreach (var value in values)
                        {
                            if (value is null) row.AppendNullValue(); else row.AppendValue((string)value);
                        }
                    });
                }
                appender.Close();
            }

            ClassifyRows(connection, transaction, candidate, request.SelectedPrimaryKeyAlias, reasonColumn);
            var metrics = ReadMetrics(connection, transaction, candidate, request.SelectedPrimaryKeyAlias, reasonColumn);
            transaction.Commit();

            return metrics with { CandidateTableName = candidate, Aliases = aliases, SelectedPrimaryKeyAlias = request.SelectedPrimaryKeyAlias, TechnicalReasonColumnName = reasonColumn };
        }
        catch (OperationCanceledException exception)
        {
            var cleanupException = DropAttemptCandidate(databasePath, candidate);
            throw cleanupException is null
                ? new SergioStagingException("Sergio staging was canceled.", exception)
                : new SergioStagingException("Sergio staging was canceled.", exception, cleanupException);
        }
        catch (SergioStagingException exception)
        {
            var cleanupException = DropAttemptCandidate(databasePath, candidate);
            if (cleanupException is null) throw;
            throw new SergioStagingException(exception.Message, exception, cleanupException);
        }
        catch (Exception exception)
        {
            var cleanupException = DropAttemptCandidate(databasePath, candidate);
            throw cleanupException is null
                ? new SergioStagingException("Sergio staging failed.", exception)
                : new SergioStagingException("Sergio staging failed.", exception, cleanupException);
        }
    }

    public void Cleanup(string candidateTableName)
    {
        lock (sync) CleanupCore(candidateTableName);
    }

    private void CleanupCore(string candidateTableName)
    {
        if (!ownedCandidates.TryGetValue(candidateTableName, out var descriptor) || descriptor.IsLastImport) return;
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
        lock (sync)
        {
            string[] candidates = ownedCandidates.Keys.ToArray();
            foreach (var candidate in candidates) CleanupCore(candidate);
        }
    }

    internal IDisposable AcquirePromotionLock() => new PromotionLock(sync);
    internal bool OwnsCandidate(string candidateTableName, string selectedPrimaryKeyAlias)
    {
        lock (sync)
            return ownedCandidates.TryGetValue(candidateTableName, out var descriptor) &&
                string.Equals(descriptor.SelectedPrimaryKeyAlias, selectedPrimaryKeyAlias, StringComparison.Ordinal);
    }

    internal CandidateDescriptor GetOwnedCandidate(string candidateTableName, string workbookPath)
    {
        lock (sync)
        {
            if (!ownedCandidates.TryGetValue(candidateTableName, out var descriptor) ||
                !string.Equals(descriptor.WorkbookPath, Path.GetFullPath(workbookPath), StringComparison.Ordinal))
                throw new InvalidOperationException("The Sergio candidate is unknown, not owned by this analyzer, or belongs to a different workbook.");
            return descriptor;
        }
    }
    internal CandidateDescriptor GetOwnedCandidateForKey(string candidateTableName, string selectedPrimaryKeyAlias)
    {
        lock (sync)
        {
            if (!ownedCandidates.TryGetValue(candidateTableName, out var descriptor) ||
                !string.Equals(descriptor.SelectedPrimaryKeyAlias, selectedPrimaryKeyAlias, StringComparison.Ordinal))
                throw new InvalidOperationException("The Sergio candidate is unknown or was staged with a different primary key.");
            return descriptor;
        }
    }

    internal AnalyzerOwnershipTransition PrepareLastImportTransition(string candidateTableName, string? selectedPrimaryKeyAlias)
    {
        if (string.Equals(candidateTableName, SergioLoadPromoter.LastImportTableName, StringComparison.Ordinal))
            throw new InvalidOperationException("The Sergio candidate cannot already be the canonical last import.");

        lock (sync)
        {
            if (!ownedCandidates.TryGetValue(candidateTableName, out var candidate) ||
                (selectedPrimaryKeyAlias is not null && !string.Equals(candidate.SelectedPrimaryKeyAlias, selectedPrimaryKeyAlias, StringComparison.Ordinal)))
                throw new InvalidOperationException("The Sergio candidate is no longer owned by this analyzer.");

            ownedCandidates.TryGetValue(SergioLoadPromoter.LastImportTableName, out var previousLastImport);
            // Preparation owns the canonical mapping before Commit. The promotion lock keeps this
            // invisible to CleanupAll/export while the database transaction is still provisional.
            var prepared = candidate with { IsLastImport = true };
            ownedCandidates[SergioLoadPromoter.LastImportTableName] = prepared;
            return new AnalyzerOwnershipTransition(this, candidateTableName, candidate, previousLastImport);
        }
    }

    internal sealed class AnalyzerOwnershipTransition : IDisposable
    {
        private readonly SergioStagingAnalyzer analyzer;
        private readonly string candidateName;
        private readonly CandidateDescriptor candidate;
        private readonly CandidateDescriptor? previousLastImport;
        private bool completed;

        internal AnalyzerOwnershipTransition(SergioStagingAnalyzer analyzer, string candidateName,
            CandidateDescriptor candidate, CandidateDescriptor? previousLastImport)
        {
            this.analyzer = analyzer;
            this.candidateName = candidateName;
            this.candidate = candidate;
            this.previousLastImport = previousLastImport;
        }

        // Completion is deliberately only dictionary removal: no allocation, validation, or throw
        // is allowed after the official DuckDB transaction has committed.
        internal void Complete()
        {
            if (completed) return;
            analyzer.ownedCandidates.Remove(candidateName);
            completed = true;
        }

        internal void Rollback()
        {
            if (completed) return;
            analyzer.ownedCandidates[candidateName] = candidate;
            if (previousLastImport is null) analyzer.ownedCandidates.Remove(SergioLoadPromoter.LastImportTableName);
            else analyzer.ownedCandidates[SergioLoadPromoter.LastImportTableName] = previousLastImport;
            completed = true;
        }

        public void Dispose()
        {
            if (!completed) Rollback();
        }
    }
    internal string DatabasePath => pathPolicy.GetDatabasePath();
    internal sealed record CandidateDescriptor(string WorkbookPath, IReadOnlyList<string> AcceptedAliases, string SelectedPrimaryKeyAlias, string ReasonColumn, bool IsLastImport = false);
    private sealed class PromotionLock : IDisposable
    {
        private readonly object gate;
        public PromotionLock(object gate) { this.gate = gate; Monitor.Enter(gate); }
        public void Dispose() => Monitor.Exit(gate);
    }

    private string CreateCandidateName()
    {
        string candidate;
        do candidate = $"sergio_staging_{Guid.NewGuid():N}"; while (IsTableNameTaken(candidate));
        return candidate;
    }

    private void CleanupStaleCandidatesOnce()
    {
        lock (sync)
        {
            if (staleCleanupCompleted) return;

            var databasePath = pathPolicy.GetDatabasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            using var connection = Open(databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT table_name FROM information_schema.tables";
            using var reader = command.ExecuteReader();
            var staleCandidates = new List<string>();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (IsReservedCandidateName(name) && !ownedCandidates.ContainsKey(name)) staleCandidates.Add(name);
            }

            foreach (var candidate in staleCandidates)
                Execute(connection, null, $"DROP TABLE {Quote(candidate)}");

            staleCleanupCompleted = true;
        }
    }

    private bool IsTableNameTaken(string name)
    {
        using var connection = Open(pathPolicy.GetDatabasePath());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = ?";
        command.Parameters.Add(new DuckDBParameter { Value = name });
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static void ClassifyRows(DuckDBConnection connection, DuckDBTransaction transaction, string table, string key, string reason)
    {
        var qKey = Quote(key);
        var qReason = Quote(reason);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {Quote(table)} SET {qReason} = ? WHERE {qKey} IS NULL OR TRIM({qKey}) = ''";
        command.Parameters.Add(new DuckDBParameter { Value = "CLAVE_VACIA" });
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = $"UPDATE {Quote(table)} SET {qReason} = ? WHERE {qReason} IS NULL AND {qKey} IS NOT NULL AND TRIM({qKey}) <> '' AND TRIM({qKey}) IN (SELECT TRIM({qKey}) FROM {Quote(table)} WHERE {qKey} IS NOT NULL AND TRIM({qKey}) <> '' GROUP BY TRIM({qKey}) HAVING COUNT(*) > 1)";
        command.Parameters.Add(new DuckDBParameter { Value = "CLAVE_DUPLICADA" });
        command.ExecuteNonQuery();
    }

    private static SergioStagingResult ReadMetrics(DuckDBConnection connection, DuckDBTransaction transaction, string table, string key, string reason)
    {
        var quotedKey = Quote(key);
        var quotedReason = Quote(reason);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*), COUNT(*) FILTER (WHERE {quotedReason} IS NULL), COUNT(*) FILTER (WHERE {quotedReason} = ?), COUNT(*) FILTER (WHERE {quotedReason} = ?), (SELECT COUNT(*) FROM (SELECT TRIM({quotedKey}) FROM {Quote(table)} WHERE {quotedKey} IS NOT NULL AND TRIM({quotedKey}) <> '' GROUP BY TRIM({quotedKey}) HAVING COUNT(*) > 1)) FROM {Quote(table)}";
        command.Parameters.Add(new DuckDBParameter { Value = "CLAVE_VACIA" });
        command.Parameters.Add(new DuckDBParameter { Value = "CLAVE_DUPLICADA" });
        using var reader = command.ExecuteReader();
        reader.Read();
        var total = reader.GetInt64(0);
        var valid = reader.GetInt64(1);
        var empty = reader.GetInt64(2);
        var duplicateRows = reader.GetInt64(3);
        var groups = reader.GetInt64(4);
        var isValid = total > 0 && empty == 0 && duplicateRows == 0;
        return new SergioStagingResult(table, [], key, string.Empty, total, valid, empty, groups, duplicateRows, isValid, total != valid);
    }

    private static void ValidateRequest(SergioStagingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.WorkbookPath) || !File.Exists(request.WorkbookPath)) throw new SergioStagingException("The Sergio workbook must be an existing file.");
        var columns = request.AcceptedHeaders.Columns;
        if (columns.Count == 0) throw new SergioStagingException("Accepted Sergio headers must contain at least one column.");
        if (columns.Any(column => string.IsNullOrWhiteSpace(column.ProposedAlias))) throw new SergioStagingException("Accepted Sergio aliases must be non-empty.");
        if (columns.Select(column => column.ProposedAlias).Distinct(StringComparer.Ordinal).Count() != columns.Count) throw new SergioStagingException("Accepted Sergio aliases must be globally unique.");
        if (!columns.Select(column => column.Position).OrderBy(position => position).SequenceEqual(Enumerable.Range(0, columns.Count))) throw new SergioStagingException("Accepted Sergio header positions must cover each column exactly once.");
        if (columns.Count(column => string.Equals(column.ProposedAlias, request.SelectedPrimaryKeyAlias, StringComparison.Ordinal)) != 1) throw new SergioStagingException("The selected Sergio primary key must match exactly one accepted alias.");
        if (string.Equals(request.SelectedPrimaryKeyAlias, SergioSchemaNames.RejectionReason, StringComparison.Ordinal)) throw new SergioStagingException("The reserved Sergio rejection reason column cannot be selected as the primary key.");
    }

    private static void ValidateHeaders(IReadOnlyList<string?> actual, SergioHeaderPreflightResult accepted)
    {
        var expected = accepted.Columns.Select(column => column.RawName).ToArray();
        if (actual.Count != expected.Length || actual.Where((value, index) => !Equals(value, expected[index])).Any())
            throw new SergioStagingException("The workbook headers changed after acceptance.");
    }

    private static DuckDBConnection Open(string path) { var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString); connection.Open(); return connection; }
    private static void Execute(DuckDBConnection connection, DuckDBTransaction? transaction, string sql) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.ExecuteNonQuery(); }
    private Exception? DropAttemptCandidate(string databasePath, string? candidate)
    {
        if (candidate is null || !IsOwnedName(candidate)) return null;
        try
        {
            using var connection = Open(databasePath);
            Execute(connection, null, $"DROP TABLE IF EXISTS {Quote(candidate)}");
            lock (sync) ownedCandidates.Remove(candidate);
            return null;
        }
        catch (Exception exception) { return exception; }
    }
    internal static string Quote(string identifier) => SergioSql.Quote(identifier);
    private bool staleCleanupCompleted;
    private static bool IsReservedCandidateName(string name) => name.StartsWith("sergio_staging_", StringComparison.Ordinal) && name.Length == 47 && name[15..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private bool IsOwnedName(string name)
    {
        lock (sync) return ownedCandidates.ContainsKey(name);
    }
}

internal static class SergioSql
{
    internal static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
