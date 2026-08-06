namespace Attest.Core;

/// <summary>
/// Re-verifies every candidate kill live before it is allowed into the report, then builds
/// the funnel. A kill that no longer reproduces is dropped, logged loudly, and the candidate
/// is rejected as trivial rather than delivered on stale evidence.
/// </summary>
public interface IEvidenceReporter
{
    Task<FunnelReport> BuildReportAsync(
        IReadOnlyList<PropertyCandidate> proposed,
        IReadOnlyList<ValidationResult> validations,
        IReadOnlyList<FalsificationResult> falsifications,
        CancellationToken cancellationToken);
}
