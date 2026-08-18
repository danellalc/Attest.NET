using System.Text.Json;
using Attest.Cli;
using Attest.Core;

namespace Attest.UnitTests;

public class JsonReportRendererTests
{
    private static readonly PropertyCandidate Candidate = new("Foo", "Foo never breaks.", "[Property] public bool Foo() => true;");

    [Fact]
    public void Render_DeliveredCandidate_ProducesValidJsonWithMutantEvidence()
    {
        var mutant = new MutantKill("Equality mutation", "/repo/Calculator.cs", 10, 5, "!=");
        var report = new FunnelReport(1, [new DeliveredProperty(Candidate, mutant)], [], []);
        var result = new AttestRunResult(report, new LlmUsage(100, 50, 0.01m), FromCache: false);

        var rendered = JsonReportRenderer.Render(result);
        using var document = JsonDocument.Parse(rendered); // throws if malformed

        var delivered = document.RootElement.GetProperty("delivered")[0];
        Assert.Equal("Foo", delivered.GetProperty("name").GetString());
        Assert.Equal("Equality mutation", delivered.GetProperty("killedBy").GetProperty("mutatorName").GetString());
        Assert.Equal(10, delivered.GetProperty("killedBy").GetProperty("line").GetInt32());
    }

    [Fact]
    public void Render_TraceIdProvided_AttachedToEveryDeliveredProperty()
    {
        var mutant = new MutantKill("Equality mutation", "/repo/Calculator.cs", 10, 5, "!=");
        var report = new FunnelReport(1, [new DeliveredProperty(Candidate, mutant)], [], []);
        var result = new AttestRunResult(report, new LlmUsage(0, 0, 0m), FromCache: false);

        var rendered = JsonReportRenderer.Render(result, traceId: "JIRA-123");
        using var document = JsonDocument.Parse(rendered);

        Assert.Equal("JIRA-123", document.RootElement.GetProperty("delivered")[0].GetProperty("traceId").GetString());
    }

    [Fact]
    public void Render_NoTraceIdProvided_OmitsTheFieldEntirely()
    {
        var mutant = new MutantKill("Equality mutation", "/repo/Calculator.cs", 10, 5, "!=");
        var report = new FunnelReport(1, [new DeliveredProperty(Candidate, mutant)], [], []);
        var result = new AttestRunResult(report, new LlmUsage(0, 0, 0m), FromCache: false);

        var rendered = JsonReportRenderer.Render(result);

        Assert.DoesNotContain("traceId", rendered);
    }

    [Fact]
    public void Render_RejectedAndQuarantined_IncludesReasonsByName()
    {
        var report = new FunnelReport(
            2,
            [],
            [new RejectedCandidate(Candidate, RejectionReason.Trivial, "killed zero mutants")],
            [new QuarantinedCandidate(Candidate, "seeds disagreed")]);
        var result = new AttestRunResult(report, new LlmUsage(0, 0, 0m), FromCache: false);

        var rendered = JsonReportRenderer.Render(result);
        using var document = JsonDocument.Parse(rendered);

        Assert.Equal("Trivial", document.RootElement.GetProperty("rejected")[0].GetProperty("reason").GetString());
        Assert.Equal("seeds disagreed", document.RootElement.GetProperty("quarantined")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public void Render_NoPricingConfigured_EstimatedUsdIsNullNotZero()
    {
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(100, 50, null), FromCache: false);

        var rendered = JsonReportRenderer.Render(result);
        using var document = JsonDocument.Parse(rendered);

        var llmCost = document.RootElement.GetProperty("llmCost");
        Assert.Equal(JsonValueKind.Null, llmCost.GetProperty("estimatedUsd").ValueKind);
        Assert.Equal(100, llmCost.GetProperty("inputTokens").GetInt32());
    }
}
