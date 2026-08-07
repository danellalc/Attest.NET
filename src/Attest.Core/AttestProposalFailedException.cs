namespace Attest.Core;

/// <summary>
/// Raised by the Proposer when the model's response could not be parsed into property
/// candidates. Distinct from proposing zero candidates: that is a valid, quiet outcome, this
/// is the model returning something the parser could not read at all.
/// </summary>
public sealed class AttestProposalFailedException : AttestException
{
    /// <summary>The model's raw response, for diagnosis.</summary>
    public string RawResponse { get; }

    /// <param name="reason">Why the response could not be parsed.</param>
    /// <param name="rawResponse">The model's raw response, for diagnosis.</param>
    public AttestProposalFailedException(string reason, string rawResponse)
        : base($"Could not parse the model's proposal response: {reason}")
    {
        RawResponse = rawResponse;
    }
}
