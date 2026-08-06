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
}
