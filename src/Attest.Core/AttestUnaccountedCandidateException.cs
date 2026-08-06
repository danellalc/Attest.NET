namespace Attest.Core;

/// <summary>
/// Raised by the Evidence stage when a proposed candidate has no matching validation result.
/// Every proposed candidate must land in exactly one of Delivered, Rejected or Quarantined;
/// silently skipping one would break that guarantee instead of failing loudly.
/// </summary>
public sealed class AttestUnaccountedCandidateException : AttestException
{
    /// <summary>Name of the candidate that was proposed but never validated.</summary>
    public string CandidateName { get; }

    /// <param name="candidateName">Name of the candidate that was proposed but never validated.</param>
    public AttestUnaccountedCandidateException(string candidateName)
        : base($"Candidate '{candidateName}' was proposed but has no validation result; the funnel cannot account for it.")
    {
        CandidateName = candidateName;
    }
}
