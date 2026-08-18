using System.Text.Json;
using System.Text.Json.Serialization;
using Attest.Core;

namespace Attest.Cli;

/// <summary>
/// SARIF 2.1.0 output, for GitHub code scanning and other SARIF consumers. Only delivered
/// properties get a result: they are the one stage with a genuine source-code anchor (the
/// killed mutant's own file and line) -- rejected and quarantined candidates describe the
/// LLM-proposed candidate itself, not a location in the repo, so there is nothing for SARIF's
/// location-based model to point at for those.
/// </summary>
internal static class SarifReportRenderer
{
    private const string RuleId = "attest-delivered-property";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static string Render(AttestRunResult result)
    {
        var results = result.Report.Delivered.Select(delivered => new SarifResult(
            RuleId,
            "note",
            new SarifMessage(
                $"{delivered.Candidate.Name}: {delivered.Candidate.Description} (killed by {delivered.Mutant.MutatorName})"),
            [
                new SarifLocation(new SarifPhysicalLocation(
                    new SarifArtifactLocation(delivered.Mutant.FilePath.Replace('\\', '/')),
                    new SarifRegion(delivered.Mutant.Line, delivered.Mutant.Column)))
            ])).ToList();

        var document = new SarifDocument(
            "2.1.0",
            [
                new SarifRun(
                    new SarifTool(new SarifToolDriver(
                        "Attest",
                        "https://github.com/danellalc/Attest.NET",
                        [new SarifRule(RuleId, new SarifMessage("A property-based test verified this code and killed a real mutant of it."))])),
                    results)
            ]);

        return JsonSerializer.Serialize(document, Options);
    }

    private sealed record SarifDocument(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("runs")] IReadOnlyList<SarifRun> Runs)
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; init; } = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json";
    }

    private sealed record SarifRun(SarifTool Tool, IReadOnlyList<SarifResult> Results);
    private sealed record SarifTool(SarifToolDriver Driver);
    private sealed record SarifToolDriver(string Name, string InformationUri, IReadOnlyList<SarifRule> Rules);
    private sealed record SarifRule(string Id, SarifMessage ShortDescription);
    private sealed record SarifResult(string RuleId, string Level, SarifMessage Message, IReadOnlyList<SarifLocation> Locations);
    private sealed record SarifMessage(string Text);
    private sealed record SarifLocation(SarifPhysicalLocation PhysicalLocation);
    private sealed record SarifPhysicalLocation(SarifArtifactLocation ArtifactLocation, SarifRegion Region);
    private sealed record SarifArtifactLocation(string Uri);
    private sealed record SarifRegion(int StartLine, int StartColumn);
}
