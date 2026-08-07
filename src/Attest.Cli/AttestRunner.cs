using Attest.Core;

namespace Attest.Cli;

/// <summary>
/// Wires the seven stages together for one `attest --diff` run. Not itself a stage: the
/// pipeline stops at Evidence, this is the thing that calls each stage in order.
/// </summary>
internal sealed class AttestRunner
{
    private readonly IDiffScope _diffScope;
    private readonly ISanitizer _sanitizer;
    private readonly IProposer _proposer;
    private readonly ISynthesizer _synthesizer;
    private readonly IValidator _validator;
    private readonly IFalsifier _falsifier;
    private readonly IEvidenceReporter _evidenceReporter;

    internal AttestRunner(
        IDiffScope diffScope,
        ISanitizer sanitizer,
        IProposer proposer,
        ISynthesizer synthesizer,
        IValidator validator,
        IFalsifier falsifier,
        IEvidenceReporter evidenceReporter)
    {
        _diffScope = diffScope;
        _sanitizer = sanitizer;
        _proposer = proposer;
        _synthesizer = synthesizer;
        _validator = validator;
        _falsifier = falsifier;
        _evidenceReporter = evidenceReporter;
    }

    internal async Task<AttestRunResult> RunAsync(
        string repositoryRoot,
        string targetProjectPath,
        string baseRef,
        int maxMutants,
        CancellationToken cancellationToken)
    {
        var scope = await _diffScope.ComputeScopeAsync(repositoryRoot, baseRef, cancellationToken).ConfigureAwait(false);
        if (scope.ChangedMethods.Count == 0)
            return new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(0, 0, 0m), FromCache: false);

        var scopedSources = new List<ScopedSource>();
        foreach (var method in scope.ChangedMethods)
        {
            var sourceCode = await File.ReadAllTextAsync(method.FilePath, cancellationToken).ConfigureAwait(false);
            var sanitized = _sanitizer.Sanitize(sourceCode);
            scopedSources.Add(new ScopedSource(method.ContainingType, method.MethodName, sanitized.RedactedContent));
        }

        var proposal = await _proposer.ProposeAsync(scopedSources, cancellationToken).ConfigureAwait(false);

        var mutationScope = new MutationScope(
            scope.ChangedMethods.Select(m => m.FilePath).Concat(scope.CallerMethods.Select(m => m.FilePath)).Distinct().ToList(),
            maxMutants);

        // A candidate that never gets a SynthesizedTest (failed to compile) or never gets a
        // ValidationResult (the test run itself crashed) cannot flow through EvidenceReporter,
        // which requires both for every candidate it is given. Both are pulled out here and
        // folded back into the report by name, using the reasons Attest.Core already defines
        // for exactly these two cases, rather than either aborting the whole run or silently
        // dropping the candidate from the funnel.
        var deliverableCandidates = new List<PropertyCandidate>();
        var preFilteredRejections = new List<RejectedCandidate>();
        var validations = new List<ValidationResult>();
        var falsifications = new List<FalsificationResult>();

        foreach (var candidate in proposal.Candidates)
        {
            SynthesizedTest synthesized;
            try
            {
                synthesized = await _synthesizer.SynthesizeAsync(candidate, targetProjectPath, cancellationToken).ConfigureAwait(false);
            }
            catch (AttestSynthesisFailedException ex)
            {
                preFilteredRejections.Add(new RejectedCandidate(candidate, RejectionReason.Unsynthesizable, ex.BuildOutput));
                continue;
            }
            catch (AttestUnsynthesizableTypeException ex)
            {
                preFilteredRejections.Add(new RejectedCandidate(candidate, RejectionReason.Unsynthesizable, ex.Message));
                continue;
            }

            ValidationResult validation;
            try
            {
                validation = await _validator.ValidateAsync(synthesized, cancellationToken).ConfigureAwait(false);
            }
            catch (AttestValidationFailedException ex)
            {
                preFilteredRejections.Add(new RejectedCandidate(candidate, RejectionReason.ValidationFailed, ex.RunOutput));
                continue;
            }

            deliverableCandidates.Add(candidate);
            validations.Add(validation);

            if (validation.Outcome != ValidationOutcome.Valid)
                continue;

            try
            {
                falsifications.Add(await _falsifier.FalsifyAsync(synthesized, mutationScope, cancellationToken).ConfigureAwait(false));
            }
            catch (AttestFalsificationFailedException)
            {
                // No falsification result recorded; EvidenceReporter already treats that as a
                // trivial rejection, same as a genuine zero-kill result. See the flaky-candidate
                // risk in PLANO.md for why this can happen to an otherwise fine candidate.
            }
        }

        var coreReport = await _evidenceReporter
            .BuildReportAsync(deliverableCandidates, validations, falsifications, mutationScope, cancellationToken)
            .ConfigureAwait(false);

        var report = new FunnelReport(
            proposal.Candidates.Count,
            coreReport.Delivered,
            [.. coreReport.Rejected, .. preFilteredRejections],
            coreReport.Quarantined);

        return new AttestRunResult(report, proposal.Usage, proposal.FromCache);
    }
}

internal sealed record AttestRunResult(FunnelReport Report, LlmUsage Usage, bool FromCache);
