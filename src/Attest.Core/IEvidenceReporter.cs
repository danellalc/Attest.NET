namespace Attest.Core;

/// <summary>
/// Re-verifies every candidate kill live before it is allowed into the report, then builds
/// the funnel. A kill that no longer reproduces is dropped, logged loudly, and the candidate
/// is rejected as trivial rather than delivered on stale evidence.
/// </summary>
public interface IEvidenceReporter
{
    /// <summary>Builds the funnel report, re-verifying every kill live before delivery.</summary>
    /// <param name="proposed">Every candidate that entered the loop, delivered or not.</param>
    /// <param name="validations">The Validator's outcome for each candidate.</param>
    /// <param name="falsifications">The Falsifier's outcome for each candidate that reached it.</param>
    /// <param name="scope">The mutation scope, needed to re-run the Falsifier for re-verification.</param>
    /// <param name="cancellationToken">Token to cancel re-verification.</param>
    /// <returns>The full funnel: delivered, rejected and quarantined candidates.</returns>
    /// <exception cref="AttestUnaccountedCandidateException">A proposed candidate has no matching validation result.</exception>
    Task<FunnelReport> BuildReportAsync(
        IReadOnlyList<PropertyCandidate> proposed,
        IReadOnlyList<ValidationResult> validations,
        IReadOnlyList<FalsificationResult> falsifications,
        MutationScope scope,
        CancellationToken cancellationToken);
}
