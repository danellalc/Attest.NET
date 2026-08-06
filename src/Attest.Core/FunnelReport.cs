namespace Attest.Core;

/// <summary>
/// The full outcome of a run: every stage's counts are always present, even when
/// <see cref="Delivered"/> is empty. That is what distinguishes "nothing testable in this
/// diff" from "the tool failed" — the report renders the whole funnel either way.
/// </summary>
/// <param name="ProposedCount">How many candidates entered the loop.</param>
/// <param name="Delivered">Properties that survived both gates, each with its killed mutant as evidence.</param>
/// <param name="Rejected">Candidates that failed the Validator or the Falsifier, with a named reason.</param>
/// <param name="Quarantined">Candidates whose seeded runs disagreed.</param>
public sealed record FunnelReport(
    int ProposedCount,
    IReadOnlyList<DeliveredProperty> Delivered,
    IReadOnlyList<RejectedCandidate> Rejected,
    IReadOnlyList<QuarantinedCandidate> Quarantined);
