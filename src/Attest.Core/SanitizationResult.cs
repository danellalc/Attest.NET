namespace Attest.Core;

/// <summary>
/// The outcome of scanning content for secrets: the same content with every match replaced
/// by a placeholder naming only its category, plus what was found.
/// </summary>
/// <param name="RedactedContent">The input with every detected secret replaced by a redaction placeholder.</param>
/// <param name="Findings">What was found and where, never what it was.</param>
public sealed record SanitizationResult(string RedactedContent, IReadOnlyList<SecretFinding> Findings);
