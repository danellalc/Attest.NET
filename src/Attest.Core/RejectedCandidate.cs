namespace Attest.Core;

/// <param name="Candidate">The candidate that was rejected.</param>
/// <param name="Reason">Which gate rejected it.</param>
/// <param name="Detail">Human-readable explanation shown in the funnel report.</param>
public sealed record RejectedCandidate(PropertyCandidate Candidate, RejectionReason Reason, string Detail);
