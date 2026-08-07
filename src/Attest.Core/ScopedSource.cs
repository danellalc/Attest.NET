namespace Attest.Core;

/// <summary>
/// One changed method's already-sanitized source, ready to hand to the Proposer. Pairs
/// DiffScope's identification of a method with the Sanitizer's redacted view of it, so the
/// Proposer never receives anything that has not already passed the privacy boundary.
/// </summary>
/// <param name="ContainingType">Fully qualified name of the declaring type.</param>
/// <param name="MethodName">The method's name.</param>
/// <param name="SanitizedSourceCode">The method's source after redaction, safe to send to an LLM.</param>
public sealed record ScopedSource(string ContainingType, string MethodName, string SanitizedSourceCode);
