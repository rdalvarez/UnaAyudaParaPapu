using PapaPersonas.Core.Import.Paso2;

namespace PapaPersonas.Tests.Import.Paso2;

public sealed class Process2ImportSessionStateTests
{
    private const string PathA = @"C:\data\sergio\return.xlsx";
    private static readonly DateOnly ImportDateA = new(2026, 8, 10);
    private static readonly DateOnly ImportDateB = new(2026, 8, 11);

    [Fact]
    public void SetPreviewResult_ReadyForConfirmation_OwnsImportIdAndEnablesApply()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        var result = SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1));

        state.SetPreviewResult(result, PathA, ImportDateA);

        Assert.True(state.CanApply);
        Assert.True(state.IsOwnedImportForPathAndDate(importId, PathA, ImportDateA));
    }

    [Fact]
    public void SetPreviewResult_Failure_ClearsOwnershipAndDisablesApply()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        state.SetPreviewResult(SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1)), PathA, ImportDateA);

        state.SetPreviewResult(SergioStagePreviewResult.Failed("failed", TimeSpan.FromSeconds(1)), PathA, ImportDateA);

        Assert.False(state.CanApply);
        Assert.False(state.IsOwnedImportForPathAndDate(importId, PathA, ImportDateA));
    }

    [Fact]
    public void MarkApplied_MatchingImport_ClearsOwnership()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        state.SetPreviewResult(SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1)), PathA, ImportDateA);

        state.MarkApplied(importId);

        Assert.False(state.CanApply);
        Assert.Null(state.AnalyzedSourcePath);
        Assert.Null(state.AnalyzedImportDate);
    }

    [Fact]
    public void BusyFlag_TogglesActionAvailability()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        state.SetPreviewResult(SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1)), PathA, ImportDateA);

        state.BeginWork();
        Assert.False(state.CanAnalyze);
        Assert.False(state.CanApply);

        state.EndWork();
        Assert.True(state.CanAnalyze);
        Assert.True(state.CanApply);
    }

    [Fact]
    public void InvalidateIfSourcePathChanged_SamePath_DoesNotInvalidate()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        state.SetPreviewResult(SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1)), PathA, ImportDateA);

        var changed = state.InvalidateIfSourcePathOrImportDateChanged(PathA, ImportDateA);

        Assert.False(changed);
        Assert.True(state.CanApply);
        Assert.True(state.IsOwnedImportForPathAndDate(importId, PathA, ImportDateA));
    }

    [Fact]
    public void InvalidateIfSourcePathChanged_DifferentPath_InvalidatesOwnership()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        state.SetPreviewResult(SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1)), PathA, ImportDateA);

        var changed = state.InvalidateIfSourcePathOrImportDateChanged(@"C:\data\sergio\other.xlsx", ImportDateA);

        Assert.True(changed);
        Assert.False(state.CanApply);
        Assert.False(state.IsOwnedImportForPathAndDate(importId, PathA, ImportDateA));
    }

    [Fact]
    public void IsOwnedImportForPath_IsCaseInsensitiveOnWindowsStylePaths()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        state.SetPreviewResult(SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1)), @"C:\DATA\SERGIO\RETURN.XLSX", ImportDateA);

        Assert.True(state.IsOwnedImportForPathAndDate(importId, @"c:\data\sergio\return.xlsx", ImportDateA));
    }

    [Fact]
    public void InvalidateIfSourcePathOrImportDateChanged_DateChanged_InvalidatesOwnership()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        state.SetPreviewResult(SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1)), PathA, ImportDateA);

        var changed = state.InvalidateIfSourcePathOrImportDateChanged(PathA, ImportDateB);

        Assert.True(changed);
        Assert.False(state.CanApply);
        Assert.False(state.IsOwnedImportForPathAndDate(importId, PathA, ImportDateA));
    }

    [Fact]
    public void InvalidateIfSourcePathOrImportDateChanged_InvalidOrMissingDate_InvalidatesOwnershipImmediately()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        state.SetPreviewResult(SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1)), PathA, ImportDateA);

        var changed = state.InvalidateIfSourcePathOrImportDateChanged(PathA, importDate: null);

        Assert.True(changed);
        Assert.False(state.CanApply);
        Assert.False(state.IsOwnedImportForPathAndDate(importId, PathA, ImportDateA));
    }

    [Fact]
    public void MarkFailedApply_MatchingImport_ClearsOwnership()
    {
        var state = new Process2ImportSessionState();
        var importId = Guid.NewGuid();

        state.SetPreviewResult(SergioStagePreviewResult.Success(
            importId,
            new SergioStagePreviewSummary(1, 1, 0, 0, 0, 0, 1, 0, 0),
            notices: [],
            presentCanonicalFields: ["cuil"],
            elapsed: TimeSpan.FromSeconds(1)), PathA, ImportDateA);

        state.MarkFailedApply(importId);

        Assert.False(state.CanApply);
        Assert.Null(state.PendingImportId);
        Assert.Null(state.AnalyzedSourcePath);
        Assert.Null(state.AnalyzedImportDate);
    }
}
