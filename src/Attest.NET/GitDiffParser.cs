using System.Text.RegularExpressions;
using Attest.Core;

namespace Attest.NET;

internal static partial class GitDiffParser
{
    internal static async Task<IReadOnlyDictionary<string, IReadOnlyList<(int Start, int End)>>> GetChangedLineRangesAsync(
        string repositoryRoot,
        string baseRef,
        CancellationToken cancellationToken)
    {
        var toplevelResult = await ProcessRunner.RunAsync(
            "git",
            ["rev-parse", "--show-toplevel"],
            repositoryRoot,
            cancellationToken).ConfigureAwait(false);

        if (!toplevelResult.Succeeded)
            throw new AttestDiffScopeFailedException(repositoryRoot, $"'{repositoryRoot}' is not inside a git repository: {toplevelResult.CombinedOutput}");

        var realRoot = toplevelResult.StandardOutput.Trim().Replace('/', Path.DirectorySeparatorChar);

        var diffResult = await ProcessRunner.RunAsync(
            "git",
            ["diff", "--unified=0", "--no-color", baseRef, "--", "*.cs"],
            realRoot,
            cancellationToken).ConfigureAwait(false);

        if (!diffResult.Succeeded)
            throw new AttestDiffScopeFailedException(repositoryRoot, $"'git diff {baseRef}' failed: {diffResult.CombinedOutput}");

        return ParseUnifiedDiff(diffResult.StandardOutput, realRoot);
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<(int Start, int End)>> ParseUnifiedDiff(string diffOutput, string repositoryRoot)
    {
        var result = new Dictionary<string, List<(int Start, int End)>>();
        string? currentFile = null;

        foreach (var rawLine in diffOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                currentFile = ResolveNewFilePath(line, repositoryRoot);
                if (currentFile is not null && !result.ContainsKey(currentFile))
                    result[currentFile] = [];
                continue;
            }

            if (currentFile is null || !line.StartsWith("@@", StringComparison.Ordinal))
                continue;

            var match = HunkHeaderPattern().Match(line);
            if (!match.Success)
                continue;

            var newStart = int.Parse(match.Groups["start"].Value);
            var newCount = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value) : 1;

            var range = newCount == 0
                ? (Start: Math.Max(1, newStart), End: Math.Max(1, newStart))
                : (Start: newStart, End: newStart + newCount - 1);

            result[currentFile].Add(range);
        }

        return result.ToDictionary(
            entry => entry.Key,
            IReadOnlyList<(int Start, int End)> (entry) => entry.Value);
    }

    private static string? ResolveNewFilePath(string plusPlusPlusLine, string repositoryRoot)
    {
        var path = plusPlusPlusLine["+++ ".Length..];
        if (path == "/dev/null")
            return null;

        if (path.StartsWith("b/", StringComparison.Ordinal))
            path = path[2..];

        return Path.GetFullPath(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,(?<count>\d+))? @@")]
    private static partial Regex HunkHeaderPattern();
}
