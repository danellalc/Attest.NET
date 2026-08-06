using Attest.Core;
using Attest.NET;
using FsCheck;
using FsCheck.Xunit;

namespace Attest.PropertyTests;

/// <summary>
/// The property the whole trust boundary rests on: no candidate can ever be delivered unless
/// a kill reproduces on live re-verification. These exercise BuildReportAsync itself (via a
/// fake IFalsifier standing in for Stryker), not just the one-line IsTrivial decision, so a
/// bug in the actual delivery-routing logic is caught, not just a bug in the trivial check.
/// </summary>
public class EvidenceReporterProperties
{
    private static readonly MutationScope DummyScope = new([], MaxMutants: 200);

    [Property]
    public async Task<bool> ZeroKillsIsAlwaysRejectedAsTrivial()
    {
        var (candidate, test) = BuildDummy();
        var validation = new ValidationResult(test, ValidationOutcome.Valid, null);
        var falsification = new FalsificationResult(test, []);
        var reporter = new EvidenceReporter(new FakeFalsifier(_ => []));

        var report = await reporter.BuildReportAsync([candidate], [validation], [falsification], DummyScope, CancellationToken.None);

        return report.Delivered.Count == 0
            && report.Rejected.Count == 1
            && report.Rejected[0].Reason == RejectionReason.Trivial;
    }

    [Property]
    public async Task<bool> KillThatReproducesOnReVerificationIsAlwaysDelivered(NonEmptyArray<MutantKill> kills)
    {
        var (candidate, test) = BuildDummy();
        var validation = new ValidationResult(test, ValidationOutcome.Valid, null);
        var falsification = new FalsificationResult(test, kills.Get);
        // The fake re-verification finds exactly the same kills: they reproduce.
        var reporter = new EvidenceReporter(new FakeFalsifier(_ => kills.Get));

        var report = await reporter.BuildReportAsync([candidate], [validation], [falsification], DummyScope, CancellationToken.None);

        return report.Delivered.Count == 1 && report.Rejected.Count == 0;
    }

    [Property]
    public async Task<bool> KillThatDoesNotReproduceOnReVerificationIsNeverDelivered(NonEmptyArray<MutantKill> kills)
    {
        var (candidate, test) = BuildDummy();
        var validation = new ValidationResult(test, ValidationOutcome.Valid, null);
        var falsification = new FalsificationResult(test, kills.Get);
        // The fake re-verification finds nothing: none of the original kills reproduce.
        var reporter = new EvidenceReporter(new FakeFalsifier(_ => []));

        var report = await reporter.BuildReportAsync([candidate], [validation], [falsification], DummyScope, CancellationToken.None);

        return report.Delivered.Count == 0
            && report.Rejected.Count == 1
            && report.Rejected[0].Reason == RejectionReason.Trivial;
    }

    private static (PropertyCandidate Candidate, SynthesizedTest Test) BuildDummy()
    {
        var candidate = new PropertyCandidate("Dummy", "Dummy candidate for the meta-property test.", "Dummy");
        var test = new SynthesizedTest(candidate, "dummy.csproj", "Dummy.First", "Dummy.Second");
        return (candidate, test);
    }
}
