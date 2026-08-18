using System.Security.Cryptography;
using System.Text;
using Attest.Cli;
using Attest.Core;

namespace Attest.UnitTests;

public class EvidenceExporterTests
{
    private static readonly PropertyCandidate Candidate = new("Foo", "Foo never breaks.", "[Property] public bool Foo() => true;");

    [Fact]
    public async Task ExportAsync_NoEnvKey_WritesPlainSha256DigestMatchingTheFileBytes()
    {
        var originalKey = Environment.GetEnvironmentVariable("ATTEST_EVIDENCE_KEY");
        Environment.SetEnvironmentVariable("ATTEST_EVIDENCE_KEY", null);
        var path = Path.Combine(Path.GetTempPath(), $"attest-evidence-{Guid.NewGuid():N}.json");
        try
        {
            var mutant = new MutantKill("Equality mutation", "/repo/Calculator.cs", 10, 5, "!=");
            var report = new FunnelReport(1, [new DeliveredProperty(Candidate, mutant)], [], []);
            var result = new AttestRunResult(report, new LlmUsage(0, 0, 0m), FromCache: false);

            await EvidenceExporter.ExportAsync(result, traceId: null, path, CancellationToken.None);

            var fileBytes = await File.ReadAllBytesAsync(path);
            var expectedDigest = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

            var sidecar = await File.ReadAllTextAsync($"{path}.sha256");
            Assert.StartsWith(expectedDigest, sidecar);
            Assert.Contains(Path.GetFileName(path), sidecar);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATTEST_EVIDENCE_KEY", originalKey);
            File.Delete(path);
            File.Delete($"{path}.sha256");
        }
    }

    [Fact]
    public async Task ExportAsync_EnvKeySet_WritesHmacDigestNotThePlainHash()
    {
        var originalKey = Environment.GetEnvironmentVariable("ATTEST_EVIDENCE_KEY");
        const string key = "test-signing-key";
        Environment.SetEnvironmentVariable("ATTEST_EVIDENCE_KEY", key);
        var path = Path.Combine(Path.GetTempPath(), $"attest-evidence-{Guid.NewGuid():N}.json");
        try
        {
            var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(0, 0, 0m), FromCache: false);

            await EvidenceExporter.ExportAsync(result, traceId: null, path, CancellationToken.None);

            var fileBytes = await File.ReadAllBytesAsync(path);
            var hmacDigest = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), fileBytes)).ToLowerInvariant();
            var plainDigest = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

            var sidecar = await File.ReadAllTextAsync($"{path}.sha256");
            Assert.StartsWith(hmacDigest, sidecar);
            Assert.DoesNotContain(plainDigest, sidecar);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATTEST_EVIDENCE_KEY", originalKey);
            File.Delete(path);
            File.Delete($"{path}.sha256");
        }
    }

    [Fact]
    public async Task ExportAsync_WritesCompactJsonNotIndented()
    {
        // The digest is only meaningful if the file is the one canonical byte sequence -- a
        // pretty-printed copy could drift from what was actually hashed.
        var originalKey = Environment.GetEnvironmentVariable("ATTEST_EVIDENCE_KEY");
        Environment.SetEnvironmentVariable("ATTEST_EVIDENCE_KEY", null);
        var path = Path.Combine(Path.GetTempPath(), $"attest-evidence-{Guid.NewGuid():N}.json");
        try
        {
            var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(0, 0, 0m), FromCache: false);

            await EvidenceExporter.ExportAsync(result, traceId: null, path, CancellationToken.None);

            var content = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("\n", content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATTEST_EVIDENCE_KEY", originalKey);
            File.Delete(path);
            File.Delete($"{path}.sha256");
        }
    }
}
