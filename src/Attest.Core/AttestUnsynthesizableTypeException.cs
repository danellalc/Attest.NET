namespace Attest.Core;

/// <summary>
/// Raised by the Synthesizer when it cannot construct a generator for a domain type
/// (private constructor, DI-resolved dependency, unresolvable factory). Names the type
/// and the reason so the candidate can be skipped without aborting the whole run.
/// </summary>
public sealed class AttestUnsynthesizableTypeException : AttestException
{
    /// <summary>The type no generator could be constructed for.</summary>
    public string TypeName { get; }

    /// <param name="typeName">The type no generator could be constructed for.</param>
    /// <param name="reason">Why construction was impossible.</param>
    public AttestUnsynthesizableTypeException(string typeName, string reason)
        : base($"Cannot synthesize a generator for type '{typeName}': {reason}")
    {
        TypeName = typeName;
    }
}
