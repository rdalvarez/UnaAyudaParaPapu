namespace PapaPersonas.Core.Import.Paso2;

public sealed class Process2ImportSessionState
{
    private Guid? _pendingImportId;
    private string? _analyzedSourcePath;
    private DateOnly? _analyzedImportDate;
    private bool _isBusy;

    public Guid? PendingImportId => _pendingImportId;
    public string? AnalyzedSourcePath => _analyzedSourcePath;
    public DateOnly? AnalyzedImportDate => _analyzedImportDate;
    public bool IsBusy => _isBusy;

    public bool CanAnalyze => !_isBusy;
    public bool CanApply => !_isBusy && _pendingImportId.HasValue && !string.IsNullOrWhiteSpace(_analyzedSourcePath) && _analyzedImportDate.HasValue;

    public void BeginWork() => _isBusy = true;

    public void EndWork() => _isBusy = false;

    public void SetPreviewResult(SergioStagePreviewResult result, string currentSourcePath, DateOnly importDate)
    {
        // Invariant: preview is not apply. Only a ready-for-confirmation result
        // owns an import_id + source file intent for a future explicit apply click.
        if (result.IsSuccess
            && result.Status == SergioStagePreviewStatus.ReadyForConfirmation
            && result.ImportId.HasValue)
        {
            var normalized = NormalizePath(currentSourcePath);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                _pendingImportId = result.ImportId;
                _analyzedSourcePath = normalized;
                _analyzedImportDate = importDate;
                return;
            }
        }

        ClearOwnership();
    }

    public bool IsOwnedImportForPathAndDate(Guid importId, string currentSourcePath, DateOnly importDate)
    {
        var normalized = NormalizePath(currentSourcePath);
        return _pendingImportId.HasValue
            && _pendingImportId.Value == importId
            && !string.IsNullOrWhiteSpace(_analyzedSourcePath)
            && _analyzedImportDate.HasValue
            && !string.IsNullOrWhiteSpace(normalized)
            && string.Equals(_analyzedSourcePath, normalized, StringComparison.OrdinalIgnoreCase)
            && _analyzedImportDate.Value == importDate;
    }

    public bool InvalidateIfSourcePathOrImportDateChanged(string currentSourcePath, DateOnly? importDate)
    {
        if (!_pendingImportId.HasValue || string.IsNullOrWhiteSpace(_analyzedSourcePath) || !_analyzedImportDate.HasValue)
        {
            return false;
        }

        var normalized = NormalizePath(currentSourcePath);
        if (string.IsNullOrWhiteSpace(normalized)
            || !string.Equals(_analyzedSourcePath, normalized, StringComparison.OrdinalIgnoreCase)
            || !importDate.HasValue
            || _analyzedImportDate.Value != importDate.Value)
        {
            ClearOwnership();
            return true;
        }

        return false;
    }

    public void MarkApplied(Guid importId)
    {
        if (_pendingImportId.HasValue && _pendingImportId.Value == importId)
        {
            ClearOwnership();
        }
    }

    public void MarkFailedApply(Guid importId)
    {
        if (_pendingImportId.HasValue && _pendingImportId.Value == importId)
        {
            // Intent guard: after failed apply, force a new analyze so user confirms fresh state.
            ClearOwnership();
        }
    }

    public void ClearOwnership()
    {
        _pendingImportId = null;
        _analyzedSourcePath = null;
        _analyzedImportDate = null;
    }

    public void Reset()
    {
        ClearOwnership();
        _isBusy = false;
    }

    private static string? NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path.Trim())
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Trim();

            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}
