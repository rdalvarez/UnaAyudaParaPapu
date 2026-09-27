namespace PapaPersonas.V2.Shell;

public sealed class CleanOutputProjectionException : IOException
{
    public CleanOutputProjectionException(string message) : base(message) { }
    public CleanOutputProjectionException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class CleanOutputProjector
{
    public string Project(
        FixedTemporaryWorkspace workspace,
        string processedCurrentPath,
        IReadOnlyList<string?> sourceHeaders,
        IReadOnlyList<int> selectedPositions)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(processedCurrentPath);
        ArgumentNullException.ThrowIfNull(sourceHeaders);
        ArgumentNullException.ThrowIfNull(selectedPositions);

        var positions = ValidateAndNormalize(sourceHeaders.Count, selectedPositions);
        var projectedPath = workspace.GetPath("clean-projected.csv");
        try
        {
            using var reader = StreamingCsvReader.Open(processedCurrentPath);
            if (!reader.Read())
                throw new CleanOutputProjectionException("Clean output is missing its header row.");

            ValidateRow(reader.CurrentRow, sourceHeaders.Count, "header");
            for (var index = 0; index < sourceHeaders.Count; index++)
            {
                var expected = sourceHeaders[index] ?? string.Empty;
                if (!string.Equals(reader.CurrentRow[index] ?? string.Empty, expected, StringComparison.Ordinal))
                    throw new CleanOutputProjectionException($"Clean output header differs at column {index}.");
            }

            using var writer = StreamingCsvWriter.Open(projectedPath);
            writer.WriteRow(positions.Select(position => reader.CurrentRow[position]));
            var rowNumber = 0L;
            while (reader.Read())
            {
                rowNumber++;
                ValidateRow(reader.CurrentRow, sourceHeaders.Count, $"row {rowNumber}");
                writer.WriteRow(positions.Select(position => reader.CurrentRow[position]));
            }

            return projectedPath;
        }
        catch (CleanOutputProjectionException)
        {
            DeleteIfExists(projectedPath);
            throw;
        }
        catch (Exception exception)
        {
            DeleteIfExists(projectedPath);
            throw new CleanOutputProjectionException("Clean output projection failed.", exception);
        }
    }

    public static IReadOnlyList<int> ValidateAndNormalize(int headerCount, IReadOnlyList<int> selectedPositions)
    {
        if (selectedPositions.Count == 0)
            throw new ArgumentException("At least one clean output column must be selected.", nameof(selectedPositions));

        var normalized = selectedPositions.OrderBy(position => position).ToArray();
        if (normalized.Any(position => position < 0 || position >= headerCount))
            throw new ArgumentException("A selected clean output column is outside the planning header range.", nameof(selectedPositions));
        if (normalized.Zip(normalized.Skip(1)).Any(pair => pair.First == pair.Second))
            throw new ArgumentException("Selected clean output columns must be unique.", nameof(selectedPositions));
        return normalized;
    }

    private static void ValidateRow(IReadOnlyList<string?> row, int width, string location)
    {
        if (row.Count != width)
            throw new CleanOutputProjectionException($"Malformed clean output during projection: {location} contains {row.Count} field(s), expected {width}.");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
