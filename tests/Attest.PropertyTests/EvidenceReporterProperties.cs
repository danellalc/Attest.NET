using Attest.Core;
using Attest.NET;
using FsCheck;
using FsCheck.Xunit;

namespace Attest.PropertyTests;

/// <summary>
/// The property the whole trust boundary rests on: no candidate can ever be treated as
/// non-trivial (and therefore be eligible for delivery) unless it killed at least one
/// mutant. No Stryker call involved: this exercises the pure decision, not the mutation run.
/// </summary>
public class EvidenceReporterProperties
{
    [Property]
    public bool ZeroKillsIsAlwaysTrivial()
    {
        var falsification = BuildFalsificationResult([]);
        return EvidenceReporter.IsTrivial(falsification);
    }

    [Property]
    public bool AtLeastOneKillIsNeverTrivial(NonEmptyArray<MutantKill> kills)
    {
        var falsification = BuildFalsificationResult(kills.Get);
        return !EvidenceReporter.IsTrivial(falsification);
    }

    private static FalsificationResult BuildFalsificationResult(MutantKill[] kills)
    {
        var candidate = new PropertyCandidate("Dummy", "Dummy candidate for the meta-property test.", "Dummy");
        var test = new SynthesizedTest(candidate, "dummy.csproj", "Dummy.First", "Dummy.Second");
        return new FalsificationResult(test, kills);
    }
}
