namespace Attest.Core;

/// <summary>
/// Records that a secret was found and redacted, never what it was. The value itself must
/// never survive into a finding, a log, or an exception message.
/// </summary>
/// <param name="Category">Which detector matched, e.g. "AwsAccessKeyId" or "HighEntropyToken".</param>
/// <param name="Line">1-based line the secret started on.</param>
/// <param name="Column">1-based column the secret started on.</param>
public sealed record SecretFinding(string Category, int Line, int Column);
