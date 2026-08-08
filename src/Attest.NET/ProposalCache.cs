using System.Security.Cryptography;
using System.Text;
using Attest.Core;

namespace Attest.NET;

internal static class ProposalCache
{
    internal static string ComputeCacheKey(IReadOnlyList<ScopedSource> scopedMethods, string providerIdentity)
    {
        // Each field is hashed to a fixed 32-byte digest before being combined, not joined as
        // text with a delimiter: SanitizedSourceCode is real multi-line C# source that can
        // itself contain the delimiter, so two structurally different method lists could
        // otherwise flatten to the identical string and collide on the same cache key.
        using var combined = new MemoryStream();
        combined.Write(SHA256.HashData(Encoding.UTF8.GetBytes(providerIdentity)));
        foreach (var method in scopedMethods)
        {
            combined.Write(SHA256.HashData(Encoding.UTF8.GetBytes(method.ContainingType)));
            combined.Write(SHA256.HashData(Encoding.UTF8.GetBytes(method.MethodName)));
            combined.Write(SHA256.HashData(Encoding.UTF8.GetBytes(method.SanitizedSourceCode)));
        }

        var hash = SHA256.HashData(combined.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string? TryRead(string cacheKey)
    {
        var path = CachePath(cacheKey);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another attest process is writing this exact key at the same moment (same diff
            // proposed concurrently); treat the race as a cache miss rather than crash the run
            // over what is purely a performance optimization.
            return null;
        }
    }

    internal static void Write(string cacheKey, string rawResponse)
    {
        var path = CachePath(cacheKey);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, rawResponse);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another attest process already won the write for this exact key; losing this
            // write just means no reuse next time, not a wrong result.
        }
    }

    private static string CachePath(string cacheKey) =>
        Path.Combine(Path.GetTempPath(), "attest-proposal-cache", $"{cacheKey}.json");
}
