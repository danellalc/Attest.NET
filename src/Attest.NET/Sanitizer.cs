using System.Text;
using System.Text.RegularExpressions;
using Attest.Core;

namespace Attest.NET;

/// <summary>
/// Deterministic secret scanner: named patterns for common secret shapes, plus a Shannon
/// entropy check over long token-shaped runs for everything else. No network call, ever.
/// </summary>
public sealed partial class Sanitizer : ISanitizer
{
    private const int MinTokenLength = 20;

    // Hex digests (git SHAs, MD5/SHA hashes) top out at exactly 4.0 bits/char (log2(16)),
    // real base64 secrets comfortably clear it (up to log2(64) = 6.0); this sits between the
    // two so hashes routinely shown in diffs and logs don't get flagged as secrets.
    private const double EntropyThreshold = 4.5;

    private static readonly (string Category, Func<Regex> Pattern)[] PatternDetectors =
    [
        ("AwsAccessKeyId", AwsAccessKeyIdPattern),
        ("AwsSecretAccessKey", AwsSecretAccessKeyPattern),
        ("JwtToken", JwtPattern),
        ("PemPrivateKey", PemPrivateKeyPattern),
        ("PasswordInUrl", PasswordInUrlPattern),
        ("ConnectionStringPassword", ConnectionStringPasswordPattern),
    ];

    public SanitizationResult Sanitize(string content)
    {
        var matches = new List<(int Start, int Length, string Category)>();

        foreach (var (category, pattern) in PatternDetectors)
        {
            foreach (Match match in pattern().Matches(content))
                matches.Add((match.Index, match.Length, category));
        }

        matches.AddRange(FindHighEntropyTokens(content, matches).Select(t => (t.Start, t.Length, "HighEntropyToken")));

        var resolved = ResolveOverlaps(matches);
        var redactedContent = BuildRedactedContent(content, resolved);
        var findings = resolved
            .Select(m => new SecretFinding(m.Category, GetLine(content, m.Start), GetColumn(content, m.Start)))
            .ToList();

        return new SanitizationResult(redactedContent, findings);
    }

    private static List<(int Start, int Length, string Category)> ResolveOverlaps(
        List<(int Start, int Length, string Category)> matches)
    {
        var sorted = matches.OrderBy(m => m.Start).ThenByDescending(m => m.Length).ToList();
        var resolved = new List<(int Start, int Length, string Category)>();
        var lastEnd = -1;

        foreach (var match in sorted)
        {
            if (match.Start < lastEnd)
                continue;

            resolved.Add(match);
            lastEnd = match.Start + match.Length;
        }

        return resolved;
    }

    private static string BuildRedactedContent(string content, List<(int Start, int Length, string Category)> resolved)
    {
        var builder = new StringBuilder();
        var cursor = 0;

        foreach (var (start, length, category) in resolved)
        {
            builder.Append(content, cursor, start - cursor);
            builder.Append("[REDACTED:").Append(category).Append(']');
            cursor = start + length;
        }

        builder.Append(content, cursor, content.Length - cursor);
        return builder.ToString();
    }

    private static IEnumerable<(int Start, int Length)> FindHighEntropyTokens(
        string content,
        List<(int Start, int Length, string Category)> alreadyMatched)
    {
        foreach (Match candidate in TokenCandidatePattern().Matches(content))
        {
            if (candidate.Length < MinTokenLength)
                continue;

            if (alreadyMatched.Any(m => Overlaps(candidate.Index, candidate.Length, m.Start, m.Length)))
                continue;

            if (ComputeShannonEntropy(candidate.Value) >= EntropyThreshold)
                yield return (candidate.Index, candidate.Length);
        }
    }

    private static bool Overlaps(int start1, int length1, int start2, int length2) =>
        start1 < start2 + length2 && start2 < start1 + length1;

    internal static double ComputeShannonEntropy(string value)
    {
        if (value.Length == 0)
            return 0;

        var counts = new Dictionary<char, int>();
        foreach (var c in value)
            counts[c] = counts.GetValueOrDefault(c) + 1;

        var entropy = 0.0;
        foreach (var count in counts.Values)
        {
            var probability = (double)count / value.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static int GetLine(string content, int index)
    {
        var line = 1;
        var limit = Math.Min(index, content.Length);
        for (var i = 0; i < limit; i++)
        {
            if (content[i] == '\n')
                line++;
        }

        return line;
    }

    private static int GetColumn(string content, int index)
    {
        var lastNewline = content.LastIndexOf('\n', Math.Max(0, Math.Min(index, content.Length) - 1));
        return index - lastNewline;
    }

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKeyIdPattern();

    [GeneratedRegex(@"\b(?:aws_secret_access_key|aws_secret_key)\s*[=:]\s*['""]?[A-Za-z0-9/+=]{40}['""]?", RegexOptions.IgnoreCase)]
    private static partial Regex AwsSecretAccessKeyPattern();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*\b")]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]+?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PemPrivateKeyPattern();

    [GeneratedRegex(@"\b[a-zA-Z][a-zA-Z0-9+.-]*://[^\s/:@]+:[^\s/@]+@")]
    private static partial Regex PasswordInUrlPattern();

    [GeneratedRegex(@"\b(?:password|pwd)\s*=\s*[^;'""\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPasswordPattern();

    [GeneratedRegex(@"[A-Za-z0-9+/_=-]{20,}")]
    private static partial Regex TokenCandidatePattern();
}
