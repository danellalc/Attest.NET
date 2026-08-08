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

    // Realistic short secrets (15-16 char API keys, for instance) never even become a candidate
    // to entropy-check under the unconditional 20-char floor. Only lowered when a nearby
    // keyword already flags the line as secret-suggestive; unconditionally, 20 stays the floor
    // so ordinary short identifiers don't start getting entropy-checked in bulk.
    private const int MinTokenLengthWithContext = 12;

    // Hex digests (git SHAs, MD5/SHA hashes) top out at exactly 4.0 bits/char (log2(16)); this
    // sits above that so hashes routinely shown in diffs and logs don't get flagged. Applies
    // only when nothing nearby suggests the value is actually meant to be secret; see
    // EntropyThresholdWithContext for when it does.
    private const double EntropyThreshold = 4.5;

    // A high-entropy token sitting right after a name like "secret" or "apiKey" is far more
    // likely to be a real credential than the same entropy value with no such context, so the
    // bar drops here. Calibrated empirically: realistic 20-30 character secrets (Stripe-,
    // GitHub-, and Slack-shaped keys) measure 4.3-4.5 bits/char, comfortably above this: the
    // higher unconditional threshold above was missing that whole class of secret at plausible
    // lengths, since the true entropy ceiling for a string that short rarely reaches 4.5 even
    // when genuinely random.
    private const double EntropyThresholdWithContext = 3.5;

    private static readonly (string Category, Func<Regex> Pattern)[] PatternDetectors =
    [
        ("AwsAccessKeyId", AwsAccessKeyIdPattern),
        ("AwsSecretAccessKey", AwsSecretAccessKeyPattern),
        ("JwtToken", JwtPattern),
        ("PemPrivateKey", PemPrivateKeyPattern),
        ("PasswordInUrl", PasswordInUrlPattern),
        ("ConnectionStringPassword", ConnectionStringPasswordPattern),
    ];

    /// <inheritdoc/>
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

        foreach (var match in sorted)
        {
            var matchEnd = match.Start + match.Length;

            if (resolved.Count > 0)
            {
                var last = resolved[^1];
                var lastEnd = last.Start + last.Length;

                if (match.Start < lastEnd)
                {
                    // Overlaps the previous match: redact the union, not just whichever span
                    // was kept. A pattern with a fixed-length quantifier can match only part
                    // of a longer real secret; dropping the rest here would leave it sitting
                    // in plain text right next to a tag that looks like it was handled.
                    if (matchEnd > lastEnd)
                        resolved[^1] = (last.Start, matchEnd - last.Start, last.Category);
                    continue;
                }
            }

            resolved.Add(match);
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
            var candidateEnd = candidate.Index + candidate.Length;

            // Skip only when an existing match already covers this candidate completely; a
            // candidate that merely overlaps (extends past a shorter pattern match) still
            // needs to be considered, or a real secret's tail never gets flagged at all. See
            // ResolveOverlaps for how a partial overlap becomes one redaction covering both.
            if (alreadyMatched.Any(m => candidate.Index >= m.Start && candidateEnd <= m.Start + m.Length))
                continue;

            var hasContext = HasSecretContext(content, candidate.Index);

            var minLength = hasContext ? MinTokenLengthWithContext : MinTokenLength;
            if (candidate.Length < minLength)
                continue;

            var threshold = hasContext ? EntropyThresholdWithContext : EntropyThreshold;
            if (ComputeShannonEntropy(candidate.Value) >= threshold)
                yield return (candidate.Index, candidate.Length);
        }
    }

    // How many full lines above the candidate's own line still count as context. A wrapped
    // assignment or object-initializer property routinely puts the keyword and the value on
    // different lines (`var clientSecret =\n    "value";`); a strictly same-line check missed
    // that entirely. Still bounded, not "anywhere above": a keyword mentioned many lines away
    // (a comment at the top of a long method, say) should not tag an unrelated value below it.
    private const int SecretContextLookbackLines = 2;

    /// <summary>
    /// True when a name like "secret" or "apiKey" appears on the candidate's own line, or up to
    /// <see cref="SecretContextLookbackLines"/> lines above it, before the candidate token.
    /// </summary>
    private static bool HasSecretContext(string content, int candidateStart)
    {
        var searchPosition = candidateStart;
        var windowStart = 0;

        for (var line = 0; line <= SecretContextLookbackLines; line++)
        {
            var newlineIndex = content.LastIndexOf('\n', Math.Max(0, searchPosition - 1));
            windowStart = newlineIndex + 1;
            if (newlineIndex < 0)
                break;

            // Search strictly before this newline next time; searching AT it again (windowStart
            // - 1) would just re-find the same one, since LastIndexOf's start index is inclusive.
            searchPosition = newlineIndex;
        }

        var window = content[windowStart..candidateStart];
        return SecretContextKeywordPattern().IsMatch(window);
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

    [GeneratedRegex(@"\b(?:aws_secret_access_key|aws_secret_key)\s*[=:]\s*['""]?[A-Za-z0-9/+=]{40,}['""]?", RegexOptions.IgnoreCase)]
    private static partial Regex AwsSecretAccessKeyPattern();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*\b")]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]+?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PemPrivateKeyPattern();

    [GeneratedRegex(@"\b[a-zA-Z][a-zA-Z0-9+.-]*://[^\s/:@]+:[^\s/@]+@")]
    private static partial Regex PasswordInUrlPattern();

    // A quoted value can legitimately contain the SAME quote character, escaped by doubling it
    // (standard ADO.NET connection-string quoting): 'it''s' means the literal value it's. The
    // naive '[^']*' alternative stops at that first doubled quote, leaking everything after it.
    //
    // Two alternatives, split on whether whitespace surrounds the '=', because that turned out
    // to be the real signal separating connection-string syntax from C# code, not any specific
    // excluded character (tried that first, excluding '.'/'('/')': it broke on a password value
    // that legitimately contained a dot, and still didn't stop `Password == null` from matching,
    // since '=' itself was never excluded). A connection string never has a space before '=';
    // ordinary C# style always does. So: 'password=value' (zero space before '=') accepts a
    // broad unquoted charset, since that shape cannot be a C# assignment or comparison at all;
    // 'password = value' (space allowed either side) requires the value to be quoted, since an
    // unquoted, spaced right-hand side is a C# expression (an assignment, or the first '=' of a
    // '==' comparison), never a literal secret value.
    [GeneratedRegex(
        @"\b(?:password|pwd)\s*=\s*(?:'(?:[^']|'')*'|""(?:[^""]|"""")*"")|\b(?:password|pwd)=[^;'""\s]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPasswordPattern();

    // Floor matches MinTokenLengthWithContext, the shorter of the two length gates; the
    // unconditional, no-context MinTokenLength is enforced separately in FindHighEntropyTokens.
    [GeneratedRegex(@"[A-Za-z0-9+/_=-]{12,}")]
    private static partial Regex TokenCandidatePattern();

    // Deliberately more specific than bare "key" or "token": both are extremely common in
    // ordinary code (dictionary keys, CancellationToken, SyntaxToken) and would turn the
    // lowered threshold above into a much bigger false-positive source than it is trying to
    // fix. Word-bounded so "secretary" or "keychain" cannot match on the "secret"/"key"
    // substring alone the way a plain Contains() check would.
    [GeneratedRegex(
        @"\b(?:secret|password|pwd|apikey|api_key|api-key|accesstoken|access_token|access-token|" +
        @"authtoken|auth_token|credential|privatekey|private_key|clientsecret|client_secret|bearer)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretContextKeywordPattern();
}
