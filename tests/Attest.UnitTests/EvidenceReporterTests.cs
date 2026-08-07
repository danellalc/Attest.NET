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

    private sealed class ThrowingFalsifier : IFalsifier
    {
        public Task<FalsificationResult> FalsifyAsync(SynthesizedTest test, MutationScope scope, CancellationToken cancellationToken) =>
            throw new AttestFalsificationFailedException(test.Candidate.Name, "stryker crashed mid-run");
    }

    [Fact]
    public async Task BuildReportAsync_ReVerificationRunFails_RejectsOnlyThatCandidateInsteadOfAbortingTheWholeReport()
    {
        var candidate = new PropertyCandidate("Flaky", "Kill exists, but re-verification itself crashes.", "body");
        var test = new SynthesizedTest(candidate, "dummy.csproj", "Dummy.First", "Dummy.Second");
        var validation = new ValidationResult(test, ValidationOutcome.Valid, null);
        var falsification = new FalsificationResult(test, [new MutantKill("Arithmetic", "Add.cs", 10, 5, "a - b")]);
        var reporter = new EvidenceReporter(new ThrowingFalsifier());
        var scope = new MutationScope([], MaxMutants: 200);

        var report = await reporter.BuildReportAsync(
            proposed: [candidate],
            validations: [validation],
            falsifications: [falsification],
            scope,
            CancellationToken.None);

        Assert.Empty(report.Delivered);
        var rejection = Assert.Single(report.Rejected);
        Assert.Equal(RejectionReason.Trivial, rejection.Reason);
    }
}
