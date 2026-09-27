namespace PapaPersonas.V2.Shell;

[Flags]
public enum SergioHeaderIssueKind
{
    None = 0,
    Empty = 1,
    Duplicate = 2
}

public sealed record SergioHeaderColumn(
    int Position,
    string? RawName,
    string ProposedAlias,
    SergioHeaderIssueKind IssueKind);

public sealed record SergioHeaderPreflightResult(
    IReadOnlyList<SergioHeaderColumn> Columns,
    int EmptyHeaderCount,
    IReadOnlyList<IReadOnlyList<int>> DuplicateGroups)
{
    public bool HasIssues => Columns.Any(column => column.IssueKind != SergioHeaderIssueKind.None);
    public int DuplicateOccurrenceCount => DuplicateGroups.Sum(group => Math.Max(0, group.Count - 1));
}

public static class SergioHeaderPreflight
{
    public static SergioHeaderPreflightResult Analyze(IReadOnlyList<string?> rawHeaders)
    {
        ArgumentNullException.ThrowIfNull(rawHeaders);

        var trimmed = rawHeaders.Select(header => header?.Trim() ?? string.Empty).ToArray();
        var groups = trimmed
            .Select((name, position) => (name, position))
            .Where(item => item.name.Length > 0)
            .GroupBy(item => item.name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.Select(item => item.position).ToArray(), StringComparer.Ordinal);

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedAliases = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<SergioHeaderColumn>(rawHeaders.Count);
        var emptyOrdinal = 0;

        for (var position = 0; position < rawHeaders.Count; position++)
        {
            var baseName = trimmed[position];
            var issueKind = SergioHeaderIssueKind.None;
            string alias;
            if (baseName.Length == 0)
            {
                issueKind |= SergioHeaderIssueKind.Empty;
                alias = emptyOrdinal == 0 ? "Columna Vacia" : $"Columna Vacia ({emptyOrdinal})";
                emptyOrdinal++;
            }
            else
            {
                if (groups.ContainsKey(baseName))
                    issueKind |= SergioHeaderIssueKind.Duplicate;

                occurrences.TryGetValue(baseName, out var occurrence);
                alias = occurrence == 0 ? baseName : $"{baseName} ({occurrence})";
                occurrences[baseName] = occurrence + 1;
            }

            if (!usedAliases.Add(alias))
            {
                var suffix = 1;
                var collisionBase = alias;
                do
                {
                    alias = $"{collisionBase} ({suffix++})";
                } while (!usedAliases.Add(alias));
            }

            columns.Add(new SergioHeaderColumn(position, rawHeaders[position], alias, issueKind));
        }

        return new SergioHeaderPreflightResult(
            columns,
            columns.Count(column => column.IssueKind.HasFlag(SergioHeaderIssueKind.Empty)),
            groups.Values.Select(group => (IReadOnlyList<int>)group).ToArray());
    }
}

public static class SergioPrimaryKeySelection
{
    public static string? DefaultAlias(IEnumerable<SergioHeaderColumn> columns) =>
        columns.FirstOrDefault(column => string.Equals(column.ProposedAlias, "CUIL", StringComparison.Ordinal))?.ProposedAlias;
}
