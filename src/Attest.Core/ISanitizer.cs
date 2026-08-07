namespace Attest.Core;

/// <summary>
/// Scans text for secrets and redacts them, purely by pattern and entropy: no network call,
/// no LLM. Runs before source ever reaches the Proposer, and again over the rendered report
/// before it is shown, since a PR comment is public and permanent.
/// </summary>
public interface ISanitizer
{
    /// <summary>Redacts every detected secret in <paramref name="content"/>.</summary>
    /// <param name="content">Text to scan, e.g. source code or a rendered report.</param>
    /// <returns>The redacted content and what was found.</returns>
    SanitizationResult Sanitize(string content);
}
