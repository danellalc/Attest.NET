namespace Attest.Core;

/// <summary>
/// Raised by the Validator when the test run itself could not produce a result (process
/// crash, missing runtime), as opposed to the property failing normally. A missing result
/// must never be read as a pass.
/// </summary>
public sealed class AttestValidationFailedException : AttestException
{
    public string CandidateName { get; }
    public string RunOutput { get; }

    public AttestValidationFailedException(string candidateName, string runOutput)
        : base($"Candidate '{candidateName}' produced no test result when validated.")
    {
        CandidateName = candidateName;
        RunOutput = runOutput;
    }
}
