using System.Runtime.InteropServices;

namespace PapaPersonas.Infrastructure.Database;

// TODO(database-maintenance-hardening): This is public only for current accessibility/testing convenience; make it internal once callers/tests are adjusted to avoid an unintended public API.
public static class DatabaseExceptionPolicy
{
    public static bool IsFatal(Exception exception)
    {
        return exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or SEHException
            or AppDomainUnloadedException
            or CannotUnloadAppDomainException;
    }
}
