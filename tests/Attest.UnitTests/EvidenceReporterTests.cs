using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class EvidenceReporterTests
{
    private sealed class NeverCalledFalsifier : IFalsifier
    {
        public Task<FalsificationResult> FalsifyAsync(SynthesizedTest test, MutationScope scope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Should never be called: no candidate in this test reaches re-verification.");
    }

    [Fact]
    public async Task BuildReportAsync_CandidateWithNoMatchingValidation_ThrowsNamedException()
    {
        var candidate = new PropertyCandidate("Orphan", "Never got a validation result.", "body");
        var reporter = new EvidenceReporter(new NeverCalledFalsifier());
        var scope = new MutationScope([], MaxMutants: 200);

        var exception = await Assert.ThrowsAsync<AttestUnaccountedCandidateException>(() => reporter.BuildReportAsync(
            proposed: [candidate],
            validations: [],
            falsifications: [],
            scope,
            CancellationToken.None));

        Assert.Equal("Orphan", exception.CandidateName);
    }

    // Throws only for the named candidate; every other candidate's re-verification "succeeds"
    // by echoing back the exact kill it was originally given, as a real reproduction would. A
    // single-candidate test cannot prove candidates are isolated from each other's failures: it
    // cannot tell "this candidate was rejected, and everyone else was unaffected" apart from
    // "this candidate was rejected in isolation with nothing else to affect".
    private sealed class SelectivelyThrowingFalsifier : IFalsifier
    {
        private readonly string _throwForCandidateName;
        private readonly IReadOnlyDictionary<string, MutantKill> _reproducedKillByCandidateName;

        public SelectivelyThrowingFalsifier(string throwForCandidateName, IReadOnlyDictionary<string, MutantKill> reproducedKillByCandidateName)
        {
            _throwForCandidateName = throwForCandidateName;
            _reproducedKillByCandidateName = reproducedKillByCandidateName;
        }

        public Task<FalsificationResult> FalsifyAsync(SynthesizedTest test, MutationScope scope, CancellationToken cancellationToken)
        {
            if (test.Candidate.Name == _throwForCandidateName)
                throw new AttestFalsificationFailedException(test.Candidate.Name, "stryker crashed mid-run");

            return Task.FromResult(new FalsificationResult(test, [_reproducedKillByCandidateName[test.Candidate.Name]]));
        }
    }

    [Fact]
    public async Task BuildReportAsync_ReVerificationRunFails_RejectsOnlyThatCandidateAndStillDeliversTheOther()
    {
        var flakyCandidate = new PropertyCandidate("Flaky", "Kill exists, but re-verification itself crashes.", "body");
        var flakyTest = new SynthesizedTest(flakyCandidate, "dummy.csproj", "Flaky.First", "Flaky.Second");
        var flakyValidation = new ValidationResult(flakyTest, ValidationOutcome.Valid, null);
        var flakyKill = new MutantKill("Arithmetic", "Flaky.cs", 10, 5, "a - b");
        var flakyFalsification = new FalsificationResult(flakyTest, [flakyKill]);

        var healthyCandidate = new PropertyCandidate("Healthy", "Kill exists and reproduces cleanly.", "body");
        var healthyTest = new SynthesizedTest(healthyCandidate, "dummy.csproj", "Healthy.First", "Healthy.Second");
        var healthyValidation = new ValidationResult(healthyTest, ValidationOutcome.Valid, null);
        var healthyKill = new MutantKill("Arithmetic", "Healthy.cs", 20, 5, "a - b");
        var healthyFalsification = new FalsificationResult(healthyTest, [healthyKill]);

        var reporter = new EvidenceReporter(new SelectivelyThrowingFalsifier(
            "Flaky",
            new Dictionary<string, MutantKill> { ["Healthy"] = healthyKill }));
        var scope = new MutationScope([], MaxMutants: 200);

        var report = await reporter.BuildReportAsync(
            proposed: [flakyCandidate, healthyCandidate],
            validations: [flakyValidation, healthyValidation],
            falsifications: [flakyFalsification, healthyFalsification],
            scope,
            CancellationToken.None);

        var rejection = Assert.Single(report.Rejected);
        Assert.Equal("Flaky", rejection.Candidate.Name);
        Assert.Equal(RejectionReason.FalsificationFailed, rejection.Reason);

        var delivered = Assert.Single(report.Delivered);
        Assert.Equal("Healthy", delivered.Candidate.Name);
    }
}
