using Attest.Cli;
using Attest.Core;

namespace Attest.UnitTests;

public class ReportRendererTests
{
    private static readonly PropertyCandidate Candidate = new("Foo", "Foo never breaks.", "[Property] public bool Foo() => true;");

    [Fact]
    public void Render_EmptyReport_ShowsZeroCounts()
    {
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(0, 0, 0m), FromCache: false);

        var rendered = ReportRenderer.Render(result);

        Assert.Contains("0 proposed, 0 delivered, 0 rejected, 0 quarantined", rendered);
    }

    [Fact]
    public void Render_DeliveredCandidate_ShowsNameAndMutant()
    {
        var mutant = new MutantKill("Equality mutation", "/repo/Calculator.cs", 10, 5, "!=");
        var report = new FunnelReport(1, [new DeliveredProperty(Candidate, mutant)], [], []);
        var result = new AttestRunResult(report, new LlmUsage(100, 50, 0.01m), FromCache: false);

        var rendered = ReportRenderer.Render(result);

        Assert.Contains("[OK] Foo: Foo never breaks.", rendered);
        Assert.Contains("Equality mutation at Calculator.cs:10", rendered);
    }

    [Fact]
    public void Render_RejectedCandidate_ShowsReasonAndDetail()
    {
        var report = new FunnelReport(1, [], [new RejectedCandidate(Candidate, RejectionReason.Unsynthesizable, "did not compile")], []);
        var result = new AttestRunResult(report, new LlmUsage(0, 0, 0m), FromCache: false);

        var rendered = ReportRenderer.Render(result);

        Assert.Contains("[Unsynthesizable] Foo: did not compile", rendered);
    }

    [Fact]
    public void Render_QuarantinedCandidate_ShowsReason()
    {
        var report = new FunnelReport(1, [], [], [new QuarantinedCandidate(Candidate, "seeds disagreed")]);
        var result = new AttestRunResult(report, new LlmUsage(0, 0, 0m), FromCache: false);

        var rendered = ReportRenderer.Render(result);

        Assert.Contains("[?] Foo: seeds disagreed", rendered);
    }

    [Fact]
    public void Render_FromCache_ShowsZeroCostRegardlessOfUsage()
    {
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(999, 999, 5.00m), FromCache: true);

        var rendered = ReportRenderer.Render(result);

        Assert.Contains("$0.0000 (cached proposal, no call made)", rendered);
    }

    [Fact]
    public void Render_NotFromCache_ShowsActualCostAndTokens()
    {
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(100, 50, 0.0025m), FromCache: false);

        var rendered = ReportRenderer.Render(result);

        Assert.Contains("$0.0025 (100 in / 50 out)", rendered);
    }
}
