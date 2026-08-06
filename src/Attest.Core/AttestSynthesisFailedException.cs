namespace Attest.Core;

/// <summary>
/// Raised by the Synthesizer when the generated scratch project does not compile. Distinct
/// from <see cref="AttestUnsynthesizableTypeException"/>: this is a build failure in the
/// generated test itself, not a missing generator for a domain type.
/// </summary>
public sealed class AttestSynthesisFailedException : AttestException
{
    /// <summary>Name of the candidate whose scratch project failed to compile.</summary>
    public string CandidateName { get; }

    /// <summary>The compiler's own output, for diagnosis.</summary>
    public string BuildOutput { get; }

    /// <param name="candidateName">Name of the candidate whose scratch project failed to compile.</param>
    /// <param name="buildOutput">The compiler's own output, for diagnosis.</param>
    public AttestSynthesisFailedException(string candidateName, string buildOutput)
        : base($"Candidate '{candidateName}' failed to compile in its scratch project.")
    {
        CandidateName = candidateName;
        BuildOutput = buildOutput;
    }
}
