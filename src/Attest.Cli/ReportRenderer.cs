using System.Globalization;
using System.Text;
using Attest.Core;

namespace Attest.Cli;

/// <summary>
/// Plain-text rendering only. The funnel-as-marketing terminal output belongs to Fase 3.
/// </summary>
internal static class ReportRenderer
{
    internal static string Render(AttestRunResult result)
    {
        var report = result.Report;
        var builder = new StringBuilder();

        builder.AppendLine(
            $"Attest: {report.ProposedCount} proposed, {report.Delivered.Count} delivered, " +
            $"{report.Rejected.Count} rejected, {report.Quarantined.Count} quarantined.");
        builder.AppendLine();

        if (report.Delivered.Count > 0)
        {
            builder.AppendLine("Delivered:");
            foreach (var delivered in report.Delivered)
            {
                builder.AppendLine(
                    $"  [OK] {delivered.Candidate.Name}: {delivered.Candidate.Description}");
                builder.AppendLine(
                    $"       killed by {delivered.Mutant.MutatorName} at " +
                    $"{Path.GetFileName(delivered.Mutant.FilePath)}:{delivered.Mutant.Line}");
            }

            builder.AppendLine();
        }

        if (report.Rejected.Count > 0)
        {
            builder.AppendLine("Rejected:");
            foreach (var rejected in report.Rejected)
                builder.AppendLine($"  [{rejected.Reason}] {rejected.Candidate.Name}: {rejected.Detail}");

            builder.AppendLine();
        }

        if (report.Quarantined.Count > 0)
        {
            builder.AppendLine("Quarantined:");
            foreach (var quarantined in report.Quarantined)
                builder.AppendLine($"  [?] {quarantined.Candidate.Name}: {quarantined.Reason}");

            builder.AppendLine();
        }

        // Invariant culture, not the host machine's: a USD amount must render the same way
        // in every report regardless of where attest runs.
        builder.AppendLine(result.FromCache
            ? "LLM cost: $0.0000 (cached proposal, no call made)"
            : $"LLM cost: ${result.Usage.EstimatedCostUsd.ToString("0.0000", CultureInfo.InvariantCulture)} " +
              $"({result.Usage.InputTokens} in / {result.Usage.OutputTokens} out)");

        return builder.ToString();
    }
}
