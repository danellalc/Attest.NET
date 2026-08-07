using Attest.Core;

namespace Attest.NET;

/// <summary>
/// Turns Validator and Falsifier outcomes into a funnel report. Every kill is re-verified,
/// live, by re-running the Falsifier immediately before a candidate is allowed into
/// <see cref="FunnelReport.Delivered"/>: a kill that does not reproduce is dropped and the
/// candidate is rejected as trivial instead, never delivered on stale evidence.
/// </summary>
public sealed class EvidenceReporter : IEvidenceReporter
{
    private readonly IFalsifier _falsifier;

    /// <summary>
    /// Creates the reporter over the given falsifier, used to re-verify every kill live before
    /// a candidate reaches <see cref="FunnelReport.Delivered"/>.
    /// </summary>
    /// <param name="falsifier">The falsifier to re-run against each surviving candidate.</param>
    public EvidenceReporter(IFalsifier falsifier)
    {
        _falsifier = falsifier;
    }

    /// <inheritdoc/>
    public async Task<FunnelReport> BuildReportAsync(
        IReadOnlyList<PropertyCandidate> proposed,
        IReadOnlyList<ValidationResult> validations,
        IReadOnlyList<FalsificationResult> falsifications,
        MutationScope scope,
        CancellationToken cancellationToken)
    {
        // GroupBy + first, not ToDictionary: two value-equal candidates (the same proposal
        // appearing twice) are an expected input, not an error, given the Synthesizer's own
        // scratch-directory dedup is built around identical candidates colliding on purpose.
        var validationByCandidate = validations
            .GroupBy(v => v.Test.Candidate)
            .ToDictionary(g => g.Key, g => g.First());
        var falsificationByCandidate = falsifications
            .GroupBy(f => f.Test.Candidate)
            .ToDictionary(g => g.Key, g => g.First());

        var delivered = new List<DeliveredProperty>();
        var rejected = new List<RejectedCandidate>();
        var quarantined = new List<QuarantinedCandidate>();

        foreach (var candidate in proposed)
        {
            if (!validationByCandidate.TryGetValue(candidate, out var validation))
                throw new AttestUnaccountedCandidateException(candidate.Name);

            if (validation.Outcome == ValidationOutcome.Inconsistent)
            {
                quarantined.Add(new QuarantinedCandidate(
                    candidate,
                    validation.Detail ?? "Seeded runs disagreed."));
                continue;
            }

            if (validation.Outcome == ValidationOutcome.FailsOnCurrentCode)
            {
                rejected.Add(new RejectedCandidate(
                    candidate,
                    RejectionReason.Wrong,
                    validation.Detail ?? "Fails on the current, working code."));
                continue;
            }

            if (!falsificationByCandidate.TryGetValue(candidate, out var falsification) || IsTrivial(falsification))
            {
                rejected.Add(new RejectedCandidate(candidate, RejectionReason.Trivial, "Killed zero mutants."));
                continue;
            }

            FalsificationResult reverified;
            try
            {
                reverified = await _falsifier.FalsifyAsync(falsification.Test, scope, cancellationToken).ConfigureAwait(false);
            }
            catch (AttestFalsificationFailedException)
            {
                // A transient failure on the re-verification run itself (not a real "the kill
                // didn't reproduce" result) still must not deliver on stale evidence, and must
                // not abort every other candidate's report either.
                rejected.Add(new RejectedCandidate(
                    candidate,
                    RejectionReason.Trivial,
                    "Re-verification run failed; stale proof dropped rather than delivered unverified."));
                continue;
            }

            var stillKilled = falsification.KilledMutants.FirstOrDefault(reverified.KilledMutants.Contains);

            if (stillKilled is null)
            {
                rejected.Add(new RejectedCandidate(
                    candidate,
                    RejectionReason.Trivial,
                    "Kill did not reproduce on re-verification; stale proof dropped."));
                continue;
            }

            delivered.Add(new DeliveredProperty(candidate, stillKilled));
        }

        return new FunnelReport(proposed.Count, delivered, rejected, quarantined);
    }

    /// <summary>
    /// Exposed so the Falsifier's own property test can assert, without running Stryker,
    /// that this decision holds for every possible set of kills: zero kills is always trivial.
    /// </summary>
    internal static bool IsTrivial(FalsificationResult falsification) => falsification.KilledMutants.Count == 0;
}
