using System.Globalization;
using System.Text;
using Attest.Core;

namespace Attest.Cli;

internal static class ReportRenderer
{
    private const string Reset = "\u001b[0m";
    private const string Bold = "\u001b[1m";
    private const string Green = "\u001b[32m";
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Cyan = "\u001b[36m";

    internal static string Render(AttestRunResult result, bool useColor = false)
    {
        var report = result.Report;
        var builder = new StringBuilder();

        builder.AppendLine(Colorize(
            $"Attest: {report.ProposedCount} proposed, {report.Delivered.Count} delivered, " +
            $"{report.Rejected.Count} rejected, {report.Quarantined.Count} quarantined.",
            Bold, useColor));
        builder.AppendLine(RenderFunnelBar(report, useColor));
        builder.AppendLine();

        if (report.Delivered.Count > 0)
        {
            builder.AppendLine(Colorize("Delivered:", Bold, useColor));
            foreach (var delivered in report.Delivered)
            {
                var okMarker = Colorize("[OK]", Green, useColor);
                builder.AppendLine($"  {okMarker} {delivered.Candidate.Name}: {delivered.Candidate.Description}");
                builder.AppendLine(
                    $"       killed by {delivered.Mutant.MutatorName} at " +
                    $"{Path.GetFileName(delivered.Mutant.FilePath)}:{delivered.Mutant.Line}");
            }

            builder.AppendLine();
        }

        if (report.Rejected.Count > 0)
        {
            builder.AppendLine(Colorize("Rejected:", Bold, useColor));
            foreach (var rejected in report.Rejected)
            {
                var reasonMarker = Colorize($"[{rejected.Reason}]", Red, useColor);
                builder.AppendLine($"  {reasonMarker} {rejected.Candidate.Name}: {rejected.Detail}");
            }

            builder.AppendLine();
        }

        if (report.Quarantined.Count > 0)
        {
            builder.AppendLine(Colorize("Quarantined:", Bold, useColor));
            foreach (var quarantined in report.Quarantined)
            {
                var quarantineMarker = Colorize("[?]", Yellow, useColor);
                builder.AppendLine($"  {quarantineMarker} {quarantined.Candidate.Name}: {quarantined.Reason}");
            }

            builder.AppendLine();
        }

        // Invariant culture, not the host machine's own: a USD amount must render the same
        // way in every report regardless of where attest runs.
        var costUsd = result.Usage.EstimatedCostUsd.ToString("0.0000", CultureInfo.InvariantCulture);
        var costLine = result.FromCache
            ? "LLM cost: $0.0000 (cached proposal, no call made)"
            : $"LLM cost: ${costUsd} ({result.Usage.InputTokens} in / {result.Usage.OutputTokens} out)";
        builder.AppendLine(Colorize(costLine, Cyan, useColor));

        return builder.ToString();
    }

    private static string RenderFunnelBar(FunnelReport report, bool useColor)
    {
        if (report.ProposedCount == 0)
            return Colorize("[no candidates to show]", Cyan, useColor);

        const int width = 30;
        var delivered = BarSegment(report.Delivered.Count, report.ProposedCount, width, Green, useColor);
        var rejected = BarSegment(report.Rejected.Count, report.ProposedCount, width, Red, useColor);
        var quarantined = BarSegment(report.Quarantined.Count, report.ProposedCount, width, Yellow, useColor);

        return $"[{delivered}{rejected}{quarantined}]";
    }

    private static string BarSegment(int count, int total, int width, string color, bool useColor)
    {
        var segmentWidth = (int)Math.Round((double)count / total * width);
        return Colorize(new string('#', segmentWidth), color, useColor);
    }

    private static string Colorize(string text, string color, bool useColor) =>
        useColor ? $"{color}{text}{Reset}" : text;
}
