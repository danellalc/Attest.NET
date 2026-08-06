namespace Attest.Core;

/// <summary>
/// Raised by the Falsifier when Stryker itself could not produce a mutation report (process
/// crash, missing runtime), as opposed to the candidate legitimately killing zero mutants.
/// </summary>
public sealed class AttestFalsificationFailedException : AttestException
{
    public string CandidateName { get; }
    public string RunOutput { get; }

    public AttestFalsificationFailedException(string candidateName, string runOutput)
        : base($"Candidate '{candidateName}' produced no mutation report when falsified.")
    {
        CandidateName = candidateName;
        RunOutput = runOutput;
    }
}
