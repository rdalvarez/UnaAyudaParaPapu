using System.Globalization;

namespace PapaPersonas.V2.Shell;

public sealed class WorkbookStagingException : IOException
{
    public WorkbookStagingException(string message) : base(message) { }
    public WorkbookStagingException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed record WorkbookStagingResult(
    string StagedPath,
    int DataRowCount,
    IReadOnlyList<CleaningPlanStep> NormalizedExecutablePlan,
    IReadOnlyList<OmittedCleaningStep> OmittedSteps);

public sealed class WorkbookStager
{
    public WorkbookStagingResult Stage(
        string inputPath,
        IReadOnlyList<string?> planningHeaders,
        IReadOnlyList<CleaningPlanStep> visibleSteps,
        FixedTemporaryWorkspace preparedWorkspace,
        IWorkbookRowCursorFactory cursorFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(planningHeaders);
        ArgumentNullException.ThrowIfNull(visibleSteps);
        ArgumentNullException.ThrowIfNull(preparedWorkspace);
        ArgumentNullException.ThrowIfNull(cursorFactory);

        var currentPath = preparedWorkspace.GetPath("current.csv");
        try
        {
            using var cursor = cursorFactory.Open(inputPath);
            var preflight = CleaningExecutionPreflight.Prepare(
                planningHeaders,
                cursor.Headers,
                visibleSteps);

            if (!preflight.IsValid)
                throw new WorkbookStagingException(preflight.FailureReason ?? "Workbook staging preflight failed.");

            var rowCount = 0;
            using (var writer = StreamingCsvWriter.Open(currentPath))
            {
                writer.WriteRow(cursor.Headers);
                while (cursor.Read())
                {
                    if (cursor.CurrentRow.Count > planningHeaders.Count)
                    {
                        throw new WorkbookStagingException(
                            $"Workbook data row {rowCount + 1} contains {cursor.CurrentRow.Count} field(s), " +
                            $"but the header contains {planningHeaders.Count}.");
                    }

                    writer.WriteRow(FormatRow(cursor.CurrentRow, planningHeaders.Count));
                    rowCount++;
                }
            }

            return new WorkbookStagingResult(
                currentPath,
                rowCount,
                preflight.ExecutableSteps.ToArray(),
                preflight.OmittedSteps.ToArray());
        }
        catch (WorkbookStagingException)
        {
            DeletePartial(currentPath);
            throw;
        }
        catch (Exception exception)
        {
            DeletePartial(currentPath);
            throw new WorkbookStagingException("Workbook staging failed.", exception);
        }
    }

    private static IEnumerable<string?> FormatRow(IReadOnlyList<object?> row, int width)
    {
        for (var index = 0; index < width; index++)
            yield return index < row.Count ? FormatValue(row[index]) : string.Empty;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static void DeletePartial(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
