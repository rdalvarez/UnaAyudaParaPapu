using System.Runtime.InteropServices;
using PapaPersonas.Infrastructure.Database;

namespace PapaPersonas.Tests.Database;

public sealed class DatabaseExceptionPolicyTests
{
    [Theory]
    [MemberData(nameof(FatalExceptions))]
    public void IsFatal_ReturnsTrueForFatalExceptions(Exception exception)
    {
        Assert.True(DatabaseExceptionPolicy.IsFatal(exception));
    }

    [Theory]
    [MemberData(nameof(NonFatalExceptions))]
    public void IsFatal_ReturnsFalseForOrdinaryExceptions(Exception exception)
    {
        Assert.False(DatabaseExceptionPolicy.IsFatal(exception));
    }

    public static IEnumerable<object[]> FatalExceptions()
    {
        yield return [new OutOfMemoryException()];
        yield return [new StackOverflowException()];
        yield return [new AccessViolationException()];
        yield return [new SEHException()];
        yield return [new AppDomainUnloadedException()];
        yield return [new CannotUnloadAppDomainException()];
    }

    public static IEnumerable<object[]> NonFatalExceptions()
    {
        yield return [new InvalidOperationException()];
        yield return [new ArgumentException()];
        yield return [new IOException()];
        yield return [new UnauthorizedAccessException()];
    }
}
