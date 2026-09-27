using System.Globalization;
using DuckDB.NET.Data;

namespace PapaPersonas.V2.Shell;

public sealed record SergioRejectedExportResult(long RejectedCount, string? OutputPath)
{
    public bool HasOutput => OutputPath is not null;
}

public interface ISergioRejectedFilePublisher
{
    string Publish(string workbookPath, DateTime timestamp, Action<string> writePartial);
}

public sealed class SergioRejectedFilePublisher : ISergioRejectedFilePublisher
{
    public string Publish(string workbookPath, DateTime timestamp, Action<string> writePartial)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(workbookPath))!;
        var rejectedDirectory = Path.Combine(directory, "rech");
        var baseName = Path.GetFileNameWithoutExtension(workbookPath);
        var stamp = timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        for (var suffix = 1; ; suffix++)
        {
            var marker = suffix == 1 ? string.Empty : $"_{suffix}";
            var finalPath = Path.Combine(rejectedDirectory, $"{baseName}_rechazados_sergio_{stamp}{marker}.csv");
            var partialPath = finalPath + ".partial";
            if (File.Exists(finalPath) || File.Exists(partialPath)) continue;

            try
            {
                Directory.CreateDirectory(rejectedDirectory);
                writePartial(partialPath);
                File.Move(partialPath, finalPath);
                return finalPath;
            }
            catch
            {
                DeleteIfExists(partialPath);
                throw;
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed class SergioRejectedExporter
{
    private readonly SergioStagingAnalyzer analyzer;
    private readonly ISergioRejectedFilePublisher publisher;
    private readonly Func<DateTime> clock;

    public SergioRejectedExporter(SergioStagingAnalyzer analyzer,
        ISergioRejectedFilePublisher? publisher = null, Func<DateTime>? clock = null)
    {
        this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        this.publisher = publisher ?? new SergioRejectedFilePublisher();
        this.clock = clock ?? (() => DateTime.Now);
    }

    public SergioRejectedExportResult Export(string candidateTableName, string workbookPath, CancellationToken cancellationToken = default)
    {
        using var operation = analyzer.AcquirePromotionLock();
        var descriptor = analyzer.GetOwnedCandidate(candidateTableName, workbookPath);
        using var connection = Open(analyzer.DatabasePath);
        var aliases = descriptor.AcceptedAliases
            .Where(alias => !string.Equals(alias, descriptor.ReasonColumn, StringComparison.Ordinal)).ToArray();
        ValidateSchema(connection, candidateTableName, descriptor);
        var reason = SergioSql.Quote(descriptor.ReasonColumn);
        var table = SergioSql.Quote(candidateTableName);
        var projection = string.Join(", ", aliases.Select(SergioSql.Quote));
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {reason} IS NOT NULL";
        var rejectedCount = Convert.ToInt64(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (rejectedCount == 0) return new SergioRejectedExportResult(0, null);

        var output = publisher.Publish(workbookPath, clock(), partialPath =>
        {
            using var writer = StreamingCsvWriter.OpenNew(partialPath);
            writer.WriteRow(new[] { "PP_MOTIVO" }.Concat(aliases));
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {reason}{(aliases.Length == 0 ? string.Empty : ", " + projection)} FROM {table} WHERE {reason} IS NOT NULL";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = new string?[reader.FieldCount];
                for (var index = 0; index < row.Length; index++)
                    row[index] = reader.IsDBNull(index) ? null : reader.GetString(index);
                writer.WriteRow(row);
            }
        });
        return new SergioRejectedExportResult(rejectedCount, output);
    }

    private static void ValidateSchema(DuckDBConnection connection, string table, SergioStagingAnalyzer.CandidateDescriptor descriptor)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name = ? ORDER BY ordinal_position";
        command.Parameters.Add(new DuckDBParameter { Value = table });
        using var reader = command.ExecuteReader();
        var actual = new List<string>();
        while (reader.Read()) actual.Add(reader.GetString(0));
        var expected = descriptor.AcceptedAliases.Contains(descriptor.ReasonColumn, StringComparer.Ordinal)
            ? descriptor.AcceptedAliases
            : descriptor.AcceptedAliases.Append(descriptor.ReasonColumn).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException("The Sergio candidate schema no longer matches its analyzer-owned descriptor.");
    }

    private static DuckDBConnection Open(string path)
    {
        var connection = new DuckDBConnection(new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();
        return connection;
    }
}
