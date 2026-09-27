namespace PapaPersonas.V2.Shell;

public sealed record CleaningExecutionRequest(
    string InputWorkbookPath,
    string RejectedFolderPath,
    IReadOnlyList<string?> PlanningRawHeaders,
    IReadOnlyList<CleaningPlanStep> VisibleSteps,
    IReadOnlyList<int>? SelectedCleanColumnPositions = null);

public sealed record CleaningExecutionResult(
    string CleanOutputPath,
    string RejectedOutputPath,
    int StagedInputRowCount,
    IReadOnlyList<OmittedCleaningStep> OmittedSteps,
    IReadOnlyList<CleaningCycleResult> Cycles,
    long TotalRejectedRows,
    long RemainingRows,
    string? CleanupWarning);

public sealed class CleaningExecutionException : Exception
{
    public CleaningExecutionException(string message, Exception innerException)
        : base(message, innerException) { }

    public Exception? CleanupException { get; init; }
}

public interface ICleaningOutputPublisher
{
    (string CleanPath, string RejectedPath) Publish(
        string inputWorkbookPath,
        string rejectedFolderPath,
        string stagedCleanPath,
        string stagedRejectedPath,
        DateTime timestamp);
}

public sealed class SafeCleaningOutputPublisher : ICleaningOutputPublisher
{
    public (string CleanPath, string RejectedPath) Publish(
        string inputWorkbookPath,
        string rejectedFolderPath,
        string stagedCleanPath,
        string stagedRejectedPath,
        DateTime timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputWorkbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectedFolderPath);

        Directory.CreateDirectory(rejectedFolderPath);
        var baseName = Path.GetFileNameWithoutExtension(inputWorkbookPath);
        var stamp = timestamp.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(inputWorkbookPath))!;

        for (var suffix = 1; ; suffix++)
        {
            var marker = suffix == 1 ? string.Empty : $"_{suffix}";
            var clean = Path.Combine(sourceDirectory, $"{baseName}_limpio_{stamp}{marker}.csv");
            var rejected = Path.Combine(rejectedFolderPath, $"{baseName}_rechazados_{stamp}{marker}.csv");
            if (File.Exists(clean) || File.Exists(rejected))
                continue;

            var cleanPartial = clean + ".partial";
            var rejectedPartial = rejected + ".partial";
            var createdFinals = new List<string>(2);
            try
            {
                CopyExact(stagedCleanPath, cleanPartial);
                CopyExact(stagedRejectedPath, rejectedPartial);
                File.Move(cleanPartial, clean);
                createdFinals.Add(clean);
                File.Move(rejectedPartial, rejected);
                createdFinals.Add(rejected);
                return (clean, rejected);
            }
            catch
            {
                DeleteIfExists(cleanPartial);
                DeleteIfExists(rejectedPartial);
                foreach (var created in createdFinals)
                    DeleteIfExists(created);
                throw;
            }
        }
    }

    private static void CopyExact(string source, string destination)
    {
        using var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed class CleaningExecutionCoordinator
{
    private readonly Func<FixedTemporaryWorkspace> workspaceFactory;
    private readonly WorkbookStager stager;
    private readonly IWorkbookRowCursorFactory cursorFactory;
    private readonly CleaningCycleProcessor processor;
    private readonly CleanOutputProjector projector;
    private readonly ICleaningOutputPublisher publisher;
    private readonly Func<DateTime> clock;

    public CleaningExecutionCoordinator(
        Func<FixedTemporaryWorkspace>? workspaceFactory = null,
        WorkbookStager? stager = null,
        IWorkbookRowCursorFactory? cursorFactory = null,
        CleaningCycleProcessor? processor = null,
        CleanOutputProjector? projector = null,
        ICleaningOutputPublisher? publisher = null,
        Func<DateTime>? clock = null)
    {
        this.workspaceFactory = workspaceFactory ?? (() => new FixedTemporaryWorkspace());
        this.stager = stager ?? new WorkbookStager();
        this.cursorFactory = cursorFactory ?? new ExcelDataReaderWorkbookRowCursorFactory();
        this.processor = processor ?? new CleaningCycleProcessor();
        this.projector = projector ?? new CleanOutputProjector();
        this.publisher = publisher ?? new SafeCleaningOutputPublisher();
        this.clock = clock ?? (() => DateTime.Now);
    }

    public CleaningExecutionResult Execute(CleaningExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.VisibleSteps.Count == 0)
            throw new ArgumentException("The visible cleaning plan must contain at least one step.", nameof(request));
        var selectedPositions = request.SelectedCleanColumnPositions is null
            ? Enumerable.Range(0, request.PlanningRawHeaders.Count).ToArray()
            : CleanOutputProjector.ValidateAndNormalize(request.PlanningRawHeaders.Count, request.SelectedCleanColumnPositions);
        if (request.PlanningRawHeaders.Count == 0)
            throw new ArgumentException("Planning headers must contain at least one column.", nameof(request));
        var extension = Path.GetExtension(request.InputWorkbookPath);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The input must be an Excel workbook (.xlsx or .xls).", nameof(request));

        var workspace = workspaceFactory();
        Exception? primary = null;
        CleaningExecutionResult? result = null;
        try
        {
            workspace.Prepare();
            var staged = stager.Stage(request.InputWorkbookPath, request.PlanningRawHeaders,
                request.VisibleSteps, workspace, cursorFactory);
            var processed = processor.Process(workspace, request.PlanningRawHeaders,
                staged.NormalizedExecutablePlan, staged.StagedPath);
            var projectedCleanPath = projector.Project(workspace, processed.CurrentPath,
                request.PlanningRawHeaders, selectedPositions);
            var published = publisher.Publish(request.InputWorkbookPath, request.RejectedFolderPath,
                projectedCleanPath, processed.RejectedPath, clock());

            result = new CleaningExecutionResult(
                published.CleanPath,
                published.RejectedPath,
                staged.DataRowCount,
                staged.OmittedSteps,
                processed.Cycles,
                processed.Cycles.Sum(cycle => cycle.RejectedRows),
                processed.Cycles.Count == 0 ? staged.DataRowCount : processed.Cycles[^1].RemainingRows,
                null);
        }
        catch (Exception exception)
        {
            primary = exception;
            throw;
        }
        finally
        {
            try
            {
                workspace.Cleanup();
            }
            catch (Exception cleanupException)
            {
                if (primary is not null)
                    throw new CleaningExecutionException(
                        "Cleaning execution failed and temporary workspace cleanup also failed.", primary)
                    { CleanupException = cleanupException };

                if (result is null)
                    throw new CleaningExecutionException(
                        "Cleaning outputs were not published, and temporary workspace cleanup failed.", cleanupException);

                result = result with
                {
                    CleanupWarning = $"Cleaning outputs were published, but temporary workspace cleanup failed: {cleanupException.Message}"
                };
            }
        }

        return result!;
    }
}
