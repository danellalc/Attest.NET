using System.Text;
using System.Text.RegularExpressions;
using Attest.Core;

namespace Attest.NET;

internal static partial class GitDiffParser
{
    /// <summary>
    /// Resolves any directory inside a git repository to that repository's true top-level
    /// directory, the same base every path in this class's output is built from. Callers that
    /// also need to walk the filesystem themselves (DiffScope's own project discovery) must
    /// resolve through this once and reuse the result, rather than each independently
    /// re-deriving the root: a caller-supplied path is explicitly allowed to be a
    /// subdirectory, so anything that re-globs from the raw parameter instead of this resolved
    /// root silently only sees part of the repository.
    /// </summary>
    internal static async Task<string> ResolveRepositoryRootAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var toplevelResult = await ProcessRunner.RunAsync(
            "git",
            ["rev-parse", "--show-toplevel"],
            repositoryRoot,
            cancellationToken).ConfigureAwait(false);

        if (!toplevelResult.Succeeded)
            throw new AttestDiffScopeFailedException(repositoryRoot, $"'{repositoryRoot}' is not inside a git repository: {toplevelResult.CombinedOutput}");

        return toplevelResult.StandardOutput.Trim().Replace('/', Path.DirectorySeparatorChar);
    }

    internal static async Task<IReadOnlyDictionary<string, IReadOnlyList<(int Start, int End)>>> GetChangedLineRangesAsync(
        string realRoot,
        string baseRef,
        CancellationToken cancellationToken)
    {
        var diffResult = await ProcessRunner.RunAsync(
            "git",
            ["diff", "--unified=0", "--no-color", baseRef, "--", "*.cs"],
            realRoot,
            cancellationToken).ConfigureAwait(false);

        if (!diffResult.Succeeded)
            throw new AttestDiffScopeFailedException(realRoot, $"'git diff {baseRef}' failed: {diffResult.CombinedOutput}");

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
        var path = UnquoteGitPath(plusPlusPlusLine["+++ ".Length..]);
        if (path == "/dev/null")
            return null;

        if (path.StartsWith("b/", StringComparison.Ordinal))
            path = path[2..];

        return Path.GetFullPath(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// git wraps a path in double quotes and escapes it (backslash/quote plus octal-per-byte
    /// for anything non-ASCII) whenever core.quotePath applies, which is the default and
    /// covers every path with a non-ASCII character. Without this, a changed file named with
    /// an accented or CJK character never resolves to a real path and silently drops out of
    /// scope instead of failing loudly.
    /// </summary>
    internal static string UnquoteGitPath(string raw)
    {
        if (raw.Length < 2 || raw[0] != '"' || raw[^1] != '"')
            return raw;

        var inner = raw[1..^1];
        var bytes = new List<byte>(inner.Length);

        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] != '\\')
            {
                bytes.Add((byte)inner[i]);
                continue;
            }

            i++;
            if (i >= inner.Length)
                break;

            switch (inner[i])
            {
                case '\\': bytes.Add((byte)'\\'); break;
                case '"': bytes.Add((byte)'"'); break;
                case 'a': bytes.Add(0x07); break;
                case 'b': bytes.Add(0x08); break;
                case 'f': bytes.Add(0x0C); break;
                case 'n': bytes.Add(0x0A); break;
                case 'r': bytes.Add(0x0D); break;
                case 't': bytes.Add(0x09); break;
                case 'v': bytes.Add(0x0B); break;
                default:
                    if (i + 2 < inner.Length && IsOctalDigit(inner[i]) && IsOctalDigit(inner[i + 1]) && IsOctalDigit(inner[i + 2]))
                    {
                        var value = (inner[i] - '0') * 64 + (inner[i + 1] - '0') * 8 + (inner[i + 2] - '0');
                        bytes.Add((byte)value);
                        i += 2;
                    }
                    else
                    {
                        bytes.Add((byte)inner[i]);
                    }
                    break;
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool IsOctalDigit(char c) => c is >= '0' and <= '7';

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,(?<count>\d+))? @@")]
    private static partial Regex HunkHeaderPattern();
}
