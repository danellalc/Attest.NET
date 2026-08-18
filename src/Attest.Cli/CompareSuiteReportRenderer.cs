using Attest.Core;

namespace Attest.Cli;

/// <summary>
/// Renders a <see cref="CompareSuiteResult"/>: "do your existing tests kill mutants?" answered
/// with a number, plus exactly which mutants they missed -- the actionable part.
/// </summary>
internal static class CompareSuiteReportRenderer
{
    private const string Green = "[32m";
    private const string Red = "[31m";
    private const string Bold = "[1m";
    private const string Reset = "[0m";

    internal static string Render(CompareSuiteResult result, bool useColor = false)
    {
        var builder = new System.Text.StringBuilder();

        if (result.TestedMutants == 0)
        {
            builder.AppendLine(Colorize("compare-suite: 0 mutants tested in this diff's scope.", Bold, useColor));
            return builder.ToString().TrimEnd('\n');
        }

        var killRate = (double)result.KilledMutants.Count / result.TestedMutants;
        var summary = $"compare-suite: your tests killed {result.KilledMutants.Count}/{result.TestedMutants} mutants in this diff ({killRate:P0}).";
        builder.AppendLine(Colorize(summary, Bold, useColor));

        if (result.SurvivedMutants.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(Colorize("Survived:", Bold, useColor));
            foreach (var mutant in result.SurvivedMutants)
            {
                var marker = Colorize("[Survived]", Red, useColor);
                builder.AppendLine($"  {marker} {mutant.MutatorName} at {Path.GetFileName(mutant.FilePath)}:{mutant.Line}");
            }
        }

        if (result.KilledMutants.Count == result.TestedMutants)
            builder.AppendLine().Append(Colorize("Every mutant in this diff was caught.", Green, useColor));

        return builder.ToString().TrimEnd('\n');
    }

    private static string Colorize(string text, string color, bool useColor) =>
        useColor ? $"{color}{text}{Reset}" : text;
}
