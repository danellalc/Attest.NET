namespace Attest.Core;

/// <summary>
/// Base type for every exception Attest raises deliberately. A raw exception from
/// Stryker, FsCheck or the compiler must never reach the user unwrapped.
/// </summary>
public abstract class AttestException : Exception
{
    protected AttestException(string message)
        : base(message)
    {
    }

    protected AttestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
