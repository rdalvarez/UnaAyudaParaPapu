namespace PapaPersonas.V2.Shell;

public sealed record OmittedCleaningStep(
    CleaningPlanStep Step,
    string Reason);

public sealed record CleaningExecutionPreflightResult(
    bool IsValid,
    string? FailureReason,
    IReadOnlyList<CleaningPlanStep> ExecutableSteps,
    IReadOnlyList<OmittedCleaningStep> OmittedSteps);

public static class CleaningExecutionPreflight
{
    public static CleaningExecutionPreflightResult Prepare(
        IReadOnlyList<string?> planningHeaders,
        IReadOnlyList<string?> currentHeaders,
        IReadOnlyList<CleaningPlanStep> visibleSteps)
    {
        var headerFailure = CompareHeaders(planningHeaders, currentHeaders);
        if (headerFailure is not null)
        {
            return Failure(headerFailure);
        }

        for (var index = 0; index < visibleSteps.Count; index++)
        {
            var step = visibleSteps[index];
            if (step.ColumnPosition < 0 || step.ColumnPosition >= planningHeaders.Count)
            {
                return Failure(
                    $"Cleaning step {step.StepNumber} references column position {step.ColumnPosition}, " +
                    $"but the header snapshot contains {planningHeaders.Count} column(s).");
            }
        }

        var executable = new List<CleaningPlanStep>(visibleSteps.Count);
        var omitted = new List<OmittedCleaningStep>();
        var seen = new HashSet<(CleaningAction Action, int ColumnPosition)>();

        foreach (var step in visibleSteps)
        {
            if (!seen.Add((step.Action, step.ColumnPosition)))
            {
                omitted.Add(new OmittedCleaningStep(
                    step,
                    "Repeated action and column position; the first occurrence is retained."));
                continue;
            }

            executable.Add(step with { StepNumber = executable.Count + 1 });
        }

        return new CleaningExecutionPreflightResult(true, null, executable, omitted);
    }

    private static string? CompareHeaders(
        IReadOnlyList<string?> planningHeaders,
        IReadOnlyList<string?> currentHeaders)
    {
        if (planningHeaders.Count != currentHeaders.Count)
        {
            return $"Header snapshot mismatch: planning contains {planningHeaders.Count} column(s), " +
                $"but the current headers contain {currentHeaders.Count}.";
        }

        for (var index = 0; index < planningHeaders.Count; index++)
        {
            if (!string.Equals(planningHeaders[index], currentHeaders[index], StringComparison.Ordinal))
            {
                return $"Header snapshot mismatch at column position {index}: " +
                    $"planning value {FormatHeader(planningHeaders[index])} differs from " +
                    $"current value {FormatHeader(currentHeaders[index])}.";
            }
        }

        return null;
    }

    private static string FormatHeader(string? value) => value is null ? "<null>" : $"'{value}'";

    private static CleaningExecutionPreflightResult Failure(string reason) =>
        new(false, reason, Array.Empty<CleaningPlanStep>(), Array.Empty<OmittedCleaningStep>());
}
