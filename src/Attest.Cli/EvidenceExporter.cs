using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Attest.Cli;

/// <summary>
/// Writes the funnel report to a file as compact JSON, plus a sidecar digest proving it was not
/// altered afterward. Compact, not indented: the digest is computed over the file's exact bytes,
/// so the file itself has to be the one unambiguous canonical form, not a second, prettier copy
/// that could drift from what was actually hashed.
/// </summary>
internal static class EvidenceExporter
{
    private const string EvidenceKeyEnvVar = "ATTEST_EVIDENCE_KEY";

    /// <summary>
    /// Writes <paramref name="path"/> (the report) and <c>&lt;path&gt;.sha256</c> (the digest,
    /// in the conventional "&lt;hex&gt;  &lt;filename&gt;" form <c>sha256sum -c</c> reads
    /// directly). HMAC-SHA256 keyed by <c>ATTEST_EVIDENCE_KEY</c> if that environment variable is
    /// set (a real signature: verifiable by anyone holding the same key, forgeable by no one
    /// else) -- otherwise plain SHA-256 (tamper-evident against accidental corruption, but not a
    /// signature: anyone can recompute a plain hash, so this alone does not prove who produced
    /// the file).
    /// </summary>
    internal static async Task ExportAsync(AttestRunResult result, string? traceId, string path, CancellationToken cancellationToken)
    {
        var reportJson = JsonReportRenderer.Render(result, traceId, indented: false);
        var reportBytes = Encoding.UTF8.GetBytes(reportJson);

        var key = Environment.GetEnvironmentVariable(EvidenceKeyEnvVar);
        var (algorithm, digestBytes) = string.IsNullOrEmpty(key)
            ? ("SHA-256", SHA256.HashData(reportBytes))
            : ("HMAC-SHA256", HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), reportBytes));

        var digestHex = Convert.ToHexString(digestBytes).ToLowerInvariant();

        await File.WriteAllBytesAsync(path, reportBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync($"{path}.sha256", $"{digestHex}  {Path.GetFileName(path)}\n", cancellationToken).ConfigureAwait(false);
    }
}
