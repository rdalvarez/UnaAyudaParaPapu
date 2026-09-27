namespace PapaPersonas.V2.Shell;

public sealed class CleaningCycleException : IOException
{
    public CleaningCycleException(string message) : base(message) { }
    public CleaningCycleException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed record CleaningCycleResult(
    int StepNumber,
    CleaningAction Action,
    string ColumnName,
    long InputRows,
    long RejectedRows,
    long RemainingRows);

public sealed record CleaningProcessingResult(
    string CurrentPath,
    string RejectedPath,
    IReadOnlyList<CleaningCycleResult> Cycles);

public sealed class CleaningCycleProcessor
{
    private static readonly string[] TechnicalHeaders = ["PP_MOTIVO", "PP_PASO", "PP_COLUMNA", "PP_VALOR"];

    public CleaningProcessingResult Process(
        FixedTemporaryWorkspace preparedWorkspace,
        IReadOnlyList<string?> sourceRawHeaders,
        IReadOnlyList<CleaningPlanStep> executableSteps,
        string stagedCurrentPath)
    {
        ArgumentNullException.ThrowIfNull(preparedWorkspace);
        ArgumentNullException.ThrowIfNull(sourceRawHeaders);
        ArgumentNullException.ThrowIfNull(executableSteps);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedCurrentPath);

        var currentPath = Path.GetFullPath(stagedCurrentPath);
        var rejectedPath = preparedWorkspace.GetPath("rejected.csv");
        var nextPath = preparedWorkspace.GetPath("next.csv");
        var cycles = new List<CleaningCycleResult>(executableSteps.Count);

        try
        {
            var sourceHeaders = sourceRawHeaders.Select(header => header ?? string.Empty).ToArray();
            var rejectedHeaders = BuildRejectedHeaders(sourceHeaders);
            using var rejectedWriter = StreamingCsvWriter.Open(rejectedPath);
            rejectedWriter.WriteRow(rejectedHeaders);

            ValidateFile(currentPath, sourceHeaders, "staged current.csv");
            foreach (var step in executableSteps)
            {
                var cycle = step.Action == CleaningAction.RemoveDuplicates
                    ? ProcessDuplicates(currentPath, nextPath, rejectedWriter, sourceHeaders, step)
                    : step.Action == CleaningAction.RemoveEmptyValues
                        ? ProcessEmpty(currentPath, nextPath, rejectedWriter, sourceHeaders, step)
                        : throw new CleaningCycleException($"Unsupported cleaning action at step {step.StepNumber}.");

                File.Move(nextPath, currentPath, overwrite: true);
                cycles.Add(cycle);
            }

            return new CleaningProcessingResult(currentPath, rejectedPath, cycles);
        }
        catch (CleaningCycleException)
        {
            DeletePartial(nextPath);
            DeletePartial(rejectedPath);
            throw;
        }
        catch (Exception exception)
        {
            DeletePartial(nextPath);
            DeletePartial(rejectedPath);
            throw new CleaningCycleException("Cleaning cycle processing failed.", exception);
        }
    }

    private static CleaningCycleResult ProcessEmpty(
        string currentPath, string nextPath, StreamingCsvWriter rejectedWriter,
        IReadOnlyList<string> sourceHeaders, CleaningPlanStep step)
    {
        long input = 0, rejected = 0, remaining = 0;
        using var reader = OpenAndValidate(currentPath, sourceHeaders, "empty-value pass");
        using var nextWriter = StreamingCsvWriter.Open(nextPath);
        nextWriter.WriteRow(sourceHeaders);
        while (reader.Read())
        {
            input++;
            var row = ValidateRow(reader.CurrentRow, sourceHeaders.Count, input, "empty-value pass");
            var original = row[step.ColumnPosition];
            if (original.Trim().Length == 0)
            {
                WriteRejected(rejectedWriter, "Valor vacío", step, original, row);
                rejected++;
            }
            else
            {
                nextWriter.WriteRow(row);
                remaining++;
            }
        }
        return new(step.StepNumber, step.Action, step.ColumnName, input, rejected, remaining);
    }

    private static CleaningCycleResult ProcessDuplicates(
        string currentPath, string nextPath, StreamingCsvWriter rejectedWriter,
        IReadOnlyList<string> sourceHeaders, CleaningPlanStep step)
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long input = 0;
        using (var reader = OpenAndValidate(currentPath, sourceHeaders, "duplicate-count pass"))
        {
            while (reader.Read())
            {
                input++;
                var row = ValidateRow(reader.CurrentRow, sourceHeaders.Count, input, "duplicate-count pass");
                var value = row[step.ColumnPosition].Trim();
                if (value.Length > 0)
                    counts[value] = counts.TryGetValue(value, out var count) ? count + 1 : 1;
            }
        }

        long rejected = 0, remaining = 0;
        using (var reader = OpenAndValidate(currentPath, sourceHeaders, "duplicate-reject pass"))
        using (var nextWriter = StreamingCsvWriter.Open(nextPath))
        {
            nextWriter.WriteRow(sourceHeaders);
            long rowNumber = 0;
            while (reader.Read())
            {
                rowNumber++;
                var row = ValidateRow(reader.CurrentRow, sourceHeaders.Count, rowNumber, "duplicate-reject pass");
                var original = row[step.ColumnPosition];
                var value = original.Trim();
                if (value.Length > 0 && counts[value] > 1)
                {
                    WriteRejected(rejectedWriter, "Valor duplicado", step, original, row);
                    rejected++;
                }
                else
                {
                    nextWriter.WriteRow(row);
                    remaining++;
                }
            }
        }
        return new(step.StepNumber, step.Action, step.ColumnName, input, rejected, remaining);
    }

    private static StreamingCsvReader OpenAndValidate(string path, IReadOnlyList<string> headers, string pass)
    {
        var reader = StreamingCsvReader.Open(path);
        try
        {
            if (!reader.Read())
                throw new CleaningCycleException($"Malformed CSV during {pass}: missing header row.");
            ValidateRow(reader.CurrentRow, headers.Count, 0, pass);
            for (var index = 0; index < headers.Count; index++)
                if (!string.Equals(reader.CurrentRow[index] ?? string.Empty, headers[index], StringComparison.Ordinal))
                    throw new CleaningCycleException($"Malformed CSV during {pass}: header differs at column {index}.");
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private static void ValidateFile(string path, IReadOnlyList<string> headers, string pass)
    {
        using var reader = OpenAndValidate(path, headers, pass);
        long row = 0;
        while (reader.Read())
            ValidateRow(reader.CurrentRow, headers.Count, ++row, pass);
    }

    private static string[] ValidateRow(IReadOnlyList<string?> row, int width, long rowNumber, string pass)
    {
        if (row.Count != width)
            throw new CleaningCycleException($"Malformed CSV during {pass}: row {rowNumber} contains {row.Count} field(s), expected {width}.");
        return row.Select(value => value ?? string.Empty).ToArray();
    }

    private static void WriteRejected(StreamingCsvWriter writer, string reason, CleaningPlanStep step, string value, IReadOnlyList<string> row) =>
        writer.WriteRow([reason, step.StepNumber.ToString(), step.ColumnName, value, .. row]);

    private static string[] BuildRejectedHeaders(IReadOnlyList<string> sourceHeaders)
    {
        var used = new HashSet<string>(TechnicalHeaders, StringComparer.OrdinalIgnoreCase);
        foreach (var header in sourceHeaders)
            if (!used.Contains(header)) used.Add(header);

        var result = new List<string>(TechnicalHeaders);
        foreach (var source in sourceHeaders)
        {
            var output = source;
            if (TechnicalHeaders.Contains(source, StringComparer.OrdinalIgnoreCase))
            {
                output = source + "__SOURCE";
                var suffix = 2;
                while (used.Contains(output)) output = source + "__SOURCE_" + suffix++;
            }
            used.Add(output);
            result.Add(output);
        }
        return result.ToArray();
    }

    private static void DeletePartial(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
