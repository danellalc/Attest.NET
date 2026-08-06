namespace Attest.Core;

/// <summary>
/// Raised by the Falsifier when Stryker itself could not produce a mutation report (process
/// crash, missing runtime), as opposed to the candidate legitimately killing zero mutants.
/// </summary>
public sealed class AttestFalsificationFailedException : AttestException
{
    /// <summary>Name of the candidate whose falsification run produced no report.</summary>
    public string CandidateName { get; }

    /// <summary>The mutation run's own output, for diagnosis.</summary>
    public string RunOutput { get; }

    /// <param name="candidateName">Name of the candidate whose falsification run produced no report.</param>
    /// <param name="runOutput">The mutation run's own output, for diagnosis.</param>
    public AttestFalsificationFailedException(string candidateName, string runOutput)
        : base($"Candidate '{candidateName}' produced no mutation report when falsified.")
    {
        CandidateName = candidateName;
        RunOutput = runOutput;
    }
}
