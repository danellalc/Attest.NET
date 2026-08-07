using System.Security.Cryptography;
using System.Text;
using Attest.Core;

namespace Attest.NET;

internal static class ProposalCache
{
    internal static string ComputeCacheKey(IReadOnlyList<ScopedSource> scopedMethods)
    {
        var parts = scopedMethods.SelectMany(m => new[] { m.ContainingType, m.MethodName, m.SanitizedSourceCode });
        var combined = string.Join('\n', parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string? TryRead(string cacheKey)
    {
        var path = CachePath(cacheKey);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    internal static void Write(string cacheKey, string rawResponse)
    {
        var path = CachePath(cacheKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, rawResponse);
    }

    private static string CachePath(string cacheKey) =>
        Path.Combine(Path.GetTempPath(), "attest-proposal-cache", $"{cacheKey}.json");
}
