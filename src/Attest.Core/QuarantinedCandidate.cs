namespace Attest.Core;

/// <summary>
/// A candidate whose seeded runs disagreed. Neither rejected nor delivered: quarantine is
/// a first-class outcome, and frequently points at real nondeterminism in the code under test.
/// </summary>
/// <param name="Candidate">The candidate that was quarantined.</param>
/// <param name="Reason">Named reason, e.g. which seed pair disagreed and how.</param>
public sealed record QuarantinedCandidate(PropertyCandidate Candidate, string Reason);
