namespace Attest.Core;

/// <summary>
/// Base type for every exception Attest raises deliberately. A raw exception from
/// Stryker, FsCheck or the compiler must never reach the user unwrapped.
/// </summary>
public abstract class AttestException : Exception
{
    /// <summary>
    /// Creates the exception with a message naming the artifact that failed.
    /// </summary>
    /// <param name="message">An actionable message naming the file, method, or type involved.</param>
    protected AttestException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message naming the artifact that failed and the underlying cause.
    /// </summary>
    /// <param name="message">An actionable message naming the file, method, or type involved.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    protected AttestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
