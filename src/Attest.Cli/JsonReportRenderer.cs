using System.Text.Json;
using System.Text.Json.Serialization;
using Attest.Core;

namespace Attest.Cli;

/// <summary>
/// Machine-readable form of the same funnel report ReportRenderer prints as text -- for
/// downstream tooling to consume without parsing the human-facing output. `traceId`, when set,
/// is pure pass-through (a requirement/ticket tag) attached to every delivered property; it adds
/// no verification of its own.
/// </summary>
internal static class JsonReportRenderer
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static string Render(AttestRunResult result, string? traceId = null, bool indented = true)
    {
        var report = result.Report;

        var dto = new ReportDto(
            report.ProposedCount,
            report.Delivered.Select(d => new DeliveredDto(
                d.Candidate.Name,
                d.Candidate.Description,
                traceId,
                new MutantKillDto(d.Mutant.MutatorName, d.Mutant.FilePath, d.Mutant.Line, d.Mutant.Column, d.Mutant.Replacement))).ToList(),
            report.Rejected.Select(r => new RejectedDto(r.Candidate.Name, r.Reason.ToString(), r.Detail)).ToList(),
            report.Quarantined.Select(q => new QuarantinedDto(q.Candidate.Name, q.Reason)).ToList(),
            new LlmCostDto(
                result.FromCache,
                result.Usage.EstimatedCostUsd,
                result.Usage.InputTokens,
                result.Usage.OutputTokens));

        return JsonSerializer.Serialize(dto, indented ? IndentedOptions : CompactOptions);
    }

    internal sealed record ReportDto(
        int ProposedCount,
        IReadOnlyList<DeliveredDto> Delivered,
        IReadOnlyList<RejectedDto> Rejected,
        IReadOnlyList<QuarantinedDto> Quarantined,
        LlmCostDto LlmCost);

    internal sealed record DeliveredDto(
        string Name,
        string Description,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TraceId,
        MutantKillDto KilledBy);

    internal sealed record MutantKillDto(string MutatorName, string FilePath, int Line, int Column, string Replacement);

    internal sealed record RejectedDto(string Name, string Reason, string Detail);

    internal sealed record QuarantinedDto(string Name, string Reason);

    internal sealed record LlmCostDto(bool FromCache, decimal? EstimatedUsd, int InputTokens, int OutputTokens);
}
