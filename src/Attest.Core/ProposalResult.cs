namespace Attest.Core;

/// <summary>
/// What one Proposer call produced. <see cref="FromCache"/> distinguishes a reused proposal
/// (same content hash, zero new cost) from a fresh LLM call, so the report can show which
/// happened.
/// </summary>
/// <param name="Candidates">Property candidates the model proposed, ready for the Synthesizer.</param>
/// <param name="Usage">Cost of this call. Zero-valued when <paramref name="FromCache"/> is true.</param>
/// <param name="FromCache">Whether this result came from the content-hash cache instead of a new call.</param>
public sealed record ProposalResult(IReadOnlyList<PropertyCandidate> Candidates, LlmUsage Usage, bool FromCache);
