namespace Attest.Core;

/// <summary>
/// Raised by the Synthesizer when the generated scratch project does not compile. Distinct
/// from <see cref="AttestUnsynthesizableTypeException"/>: this is a build failure in the
/// generated test itself, not a missing generator for a domain type.
/// </summary>
public sealed class AttestSynthesisFailedException : AttestException
{
    public string CandidateName { get; }
    public string BuildOutput { get; }

    public AttestSynthesisFailedException(string candidateName, string buildOutput)
        : base($"Candidate '{candidateName}' failed to compile in its scratch project.")
    {
        CandidateName = candidateName;
        BuildOutput = buildOutput;
    }
}
