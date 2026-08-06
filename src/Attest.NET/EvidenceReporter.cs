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

    public EvidenceReporter(IFalsifier falsifier)
    {
        _falsifier = falsifier;
    }

    public async Task<FunnelReport> BuildReportAsync(
        IReadOnlyList<PropertyCandidate> proposed,
        IReadOnlyList<ValidationResult> validations,
        IReadOnlyList<FalsificationResult> falsifications,
        MutationScope scope,
        CancellationToken cancellationToken)
    {
        var validationByCandidate = validations.ToDictionary(v => v.Test.Candidate);
        var falsificationByCandidate = falsifications.ToDictionary(f => f.Test.Candidate);

        var delivered = new List<DeliveredProperty>();
        var rejected = new List<RejectedCandidate>();
        var quarantined = new List<QuarantinedCandidate>();

        foreach (var candidate in proposed)
        {
            if (!validationByCandidate.TryGetValue(candidate, out var validation))
                continue;

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

            if (!falsificationByCandidate.TryGetValue(candidate, out var falsification) || falsification.KilledMutants.Count == 0)
            {
                rejected.Add(new RejectedCandidate(candidate, RejectionReason.Trivial, "Killed zero mutants."));
                continue;
            }

            var reverified = await _falsifier.FalsifyAsync(falsification.Test, scope, cancellationToken).ConfigureAwait(false);
            var stillKilled = falsification.KilledMutants.FirstOrDefault(original =>
                reverified.KilledMutants.Any(fresh => Matches(original, fresh)));

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

    private static bool Matches(MutantKill original, MutantKill fresh) =>
        original.MutatorName == fresh.MutatorName
        && original.FilePath == fresh.FilePath
        && original.Line == fresh.Line
        && original.Replacement == fresh.Replacement;
}
