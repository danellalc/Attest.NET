namespace Attest.Core;

/// <summary>
/// Raised by the Validator when the test run itself could not produce a result (process
/// crash, missing runtime), as opposed to the property failing normally. A missing result
/// must never be read as a pass.
/// </summary>
public sealed class AttestValidationFailedException : AttestException
{
    /// <summary>Name of the candidate whose validation run produced no result.</summary>
    public string CandidateName { get; }

    /// <summary>The test run's own output, for diagnosis.</summary>
    public string RunOutput { get; }

    /// <param name="candidateName">Name of the candidate whose validation run produced no result.</param>
    /// <param name="runOutput">The test run's own output, for diagnosis.</param>
    public AttestValidationFailedException(string candidateName, string runOutput)
        : base($"Candidate '{candidateName}' produced no test result when validated.")
    {
        CandidateName = candidateName;
        RunOutput = runOutput;
    }
}
