using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class EvidenceReporterTests
{
    private sealed class NeverCalledFalsifier : IFalsifier
    {
        public Task<FalsificationResult> FalsifyAsync(SynthesizedTest test, MutationScope scope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Should never be called: no candidate in this test reaches re-verification.");

        public Task<CompareSuiteResult> CompareSuiteAsync(string testProjectPath, MutationScope scope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Should never be called: EvidenceReporter never calls CompareSuiteAsync.");
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
    // "this candidate was rejected in isolation with nothing else to affect". The exception to
    // throw is a factory, not fixed, so the same fake covers every sibling AttestException the
    // re-verification call can actually raise (AttestFalsificationFailedException,
    // AttestMutantCeilingExceededException, AttestMutantCountMismatchException).
    private sealed class SelectivelyThrowingFalsifier : IFalsifier
    {
        private readonly string _throwForCandidateName;
        private readonly Func<string, AttestException> _exceptionFactory;
        private readonly IReadOnlyDictionary<string, MutantKill> _reproducedKillByCandidateName;

        public SelectivelyThrowingFalsifier(
            string throwForCandidateName,
            IReadOnlyDictionary<string, MutantKill> reproducedKillByCandidateName,
            Func<string, AttestException>? exceptionFactory = null)
        {
            _throwForCandidateName = throwForCandidateName;
            _reproducedKillByCandidateName = reproducedKillByCandidateName;
            _exceptionFactory = exceptionFactory ?? (name => new AttestFalsificationFailedException(name, "stryker crashed mid-run"));
        }

        public Task<FalsificationResult> FalsifyAsync(SynthesizedTest test, MutationScope scope, CancellationToken cancellationToken)
        {
            if (test.Candidate.Name == _throwForCandidateName)
                throw _exceptionFactory(test.Candidate.Name);

            return Task.FromResult(new FalsificationResult(test, [_reproducedKillByCandidateName[test.Candidate.Name]]));
        }

        public Task<CompareSuiteResult> CompareSuiteAsync(string testProjectPath, MutationScope scope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Should never be called: EvidenceReporter never calls CompareSuiteAsync.");
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

    [Theory]
    [MemberData(nameof(SiblingFalsificationExceptions))]
    public async Task BuildReportAsync_ReVerificationRaisesASiblingException_RejectsOnlyThatCandidateAndStillDeliversTheOther(
        RejectionReason expectedReason,
        Func<string, AttestException> exceptionFactory)
    {
        // AttestMutantCeilingExceededException and AttestMutantCountMismatchException are
        // siblings of AttestFalsificationFailedException (all three derive directly from
        // AttestException, none from one another); a catch narrowed to only the latter let
        // either of the other two propagate uncaught out of BuildReportAsync, discarding every
        // other candidate's report entry, delivered or not, along with it.
        var flakyCandidate = new PropertyCandidate("Flaky", "Kill exists, but re-verification itself raises a sibling exception.", "body");
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
            new Dictionary<string, MutantKill> { ["Healthy"] = healthyKill },
            exceptionFactory));
        var scope = new MutationScope([], MaxMutants: 200);

        var report = await reporter.BuildReportAsync(
            proposed: [flakyCandidate, healthyCandidate],
            validations: [flakyValidation, healthyValidation],
            falsifications: [flakyFalsification, healthyFalsification],
            scope,
            CancellationToken.None);

        var rejection = Assert.Single(report.Rejected);
        Assert.Equal("Flaky", rejection.Candidate.Name);
        Assert.Equal(expectedReason, rejection.Reason);

        var delivered = Assert.Single(report.Delivered);
        Assert.Equal("Healthy", delivered.Candidate.Name);
    }

    public static IEnumerable<object[]> SiblingFalsificationExceptions()
    {
        yield return
        [
            RejectionReason.FalsificationFailed,
            (Func<string, AttestException>)(name => new AttestMutantCountMismatchException(expectedCount: 3, actualCount: 7)),
        ];
        yield return
        [
            RejectionReason.MutantCeilingExceeded,
            (Func<string, AttestException>)(name => new AttestMutantCeilingExceededException(maxMutants: 200, actualCount: 250)),
        ];
    }
}
