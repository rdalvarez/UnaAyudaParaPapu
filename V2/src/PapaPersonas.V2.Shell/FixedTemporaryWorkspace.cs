namespace PapaPersonas.V2.Shell;

public sealed class TemporaryWorkspaceException : IOException
{
    public TemporaryWorkspaceException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class FixedTemporaryWorkspace : IDisposable
{
    public static string ProductionRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PapaPersonas", "V2", "temp");

    public FixedTemporaryWorkspace(string? rootOverride = null)
    {
        RootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(rootOverride) ? ProductionRoot : rootOverride);
    }

    public string RootPath { get; }

    public void Prepare()
    {
        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);

            Directory.CreateDirectory(RootPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new TemporaryWorkspaceException(
                $"Unable to reset the temporary workspace '{RootPath}'.", exception);
        }
    }

    public string GetPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
            throw new ArgumentException("The workspace child name must be a non-empty relative path.", nameof(fileName));

        var childPath = Path.GetFullPath(Path.Combine(RootPath, fileName));
        var rootWithSeparator = RootPath.EndsWith(Path.DirectorySeparatorChar)
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar;
        if (!childPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The workspace child name must remain inside the workspace root.", nameof(fileName));

        return childPath;
    }

    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new TemporaryWorkspaceException(
                $"Unable to clean up the temporary workspace '{RootPath}'.", exception);
        }
    }

    public void Dispose() => Cleanup();
}
