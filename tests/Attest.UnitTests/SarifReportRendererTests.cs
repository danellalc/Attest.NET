using System.Text.Json;
using Attest.Cli;
using Attest.Core;

namespace Attest.UnitTests;

public class SarifReportRendererTests
{
    private static readonly PropertyCandidate Candidate = new("Foo", "Foo never breaks.", "[Property] public bool Foo() => true;");

    [Fact]
    public void Render_DeliveredCandidate_ProducesValidSarifWithLocation()
    {
        var mutant = new MutantKill("Equality mutation", "/repo/Calculator.cs", 10, 5, "!=");
        var report = new FunnelReport(1, [new DeliveredProperty(Candidate, mutant)], [], []);
        var result = new AttestRunResult(report, new LlmUsage(0, 0, 0m), FromCache: false);

        var rendered = SarifReportRenderer.Render(result);
        using var document = JsonDocument.Parse(rendered); // throws if malformed

        Assert.Equal("2.1.0", document.RootElement.GetProperty("version").GetString());
        var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
        Assert.Equal(1, results.GetArrayLength());

        var location = results[0].GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal("/repo/Calculator.cs", location.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(10, location.GetProperty("region").GetProperty("startLine").GetInt32());
        Assert.Contains("Foo", results[0].GetProperty("message").GetProperty("text").GetString());
    }

    [Fact]
    public void Render_RejectedAndQuarantinedCandidates_ProduceNoResults()
    {
        // Neither has a real source-code location to anchor a SARIF result at -- both describe
        // the LLM-proposed candidate itself, not a place in the repo.
        var report = new FunnelReport(
            2,
            [],
            [new RejectedCandidate(Candidate, RejectionReason.Trivial, "killed zero mutants")],
            [new QuarantinedCandidate(Candidate, "seeds disagreed")]);
        var result = new AttestRunResult(report, new LlmUsage(0, 0, 0m), FromCache: false);

        var rendered = SarifReportRenderer.Render(result);
        using var document = JsonDocument.Parse(rendered);

        Assert.Equal(0, document.RootElement.GetProperty("runs")[0].GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void Render_EmptyReport_ProducesValidSarifWithNoResults()
    {
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(0, 0, 0m), FromCache: false);

        var rendered = SarifReportRenderer.Render(result);
        using var document = JsonDocument.Parse(rendered);

        Assert.Equal("Attest", document.RootElement.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
    }
}
