namespace Attest.Core;

/// <summary>
/// Raised by the Synthesizer when it cannot construct a generator for a domain type
/// (private constructor, DI-resolved dependency, unresolvable factory). Names the type
/// and the reason so the candidate can be skipped without aborting the whole run.
/// </summary>
public sealed class AttestUnsynthesizableTypeException : AttestException
{
    public string TypeName { get; }

    public AttestUnsynthesizableTypeException(string typeName, string reason)
        : base($"Cannot synthesize a generator for type '{typeName}': {reason}")
    {
        TypeName = typeName;
    }
}
