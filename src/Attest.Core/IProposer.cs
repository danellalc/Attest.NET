namespace Attest.Core;

/// <summary>
/// The only stage allowed to call an LLM. Turns already-sanitized source into property
/// candidates for the Synthesizer. One call per batch, no retry loop: a failed or unparseable
/// response is a named failure, never silently retried into a different answer.
/// </summary>
public interface IProposer
{
    /// <summary>Proposes property candidates for the given scoped, sanitized methods.</summary>
    /// <param name="scopedMethods">Methods to propose properties for, already sanitized.</param>
    /// <param name="cancellationToken">Token to cancel the call.</param>
    /// <returns>The proposed candidates and the cost of producing them.</returns>
    /// <exception cref="AttestProposalFailedException">The model's response could not be parsed.</exception>
    Task<ProposalResult> ProposeAsync(IReadOnlyList<ScopedSource> scopedMethods, CancellationToken cancellationToken);
}
