using Attest.Core;

namespace Attest.Cli;

internal sealed class AttestCliException : AttestException
{
    internal AttestCliException(string message)
        : base(message)
    {
    }
}
