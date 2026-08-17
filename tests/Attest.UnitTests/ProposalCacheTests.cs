using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class ProposalCacheTests
{
    [Fact]
    public void ComputeCacheKey_FieldBoundaryAmbiguity_DoesNotCollide()
    {
        // Two methods with short bodies vs. one method whose source happens to contain the
        // same tokens the other fields would have contributed, laid out on separate lines: a
        // naive '\n'-joined-text hash would flatten both to the identical string.
        var twoMethods = new[]
        {
            new ScopedSource("Foo", "Bar", "return 1;"),
            new ScopedSource("Baz", "Qux", "return 2;"),
        };
        var oneMethod = new[]
        {
            new ScopedSource("Foo", "Bar", "return 1;\nBaz\nQux\nreturn 2;"),
        };

        var keyA = ProposalCache.ComputeCacheKey(twoMethods, "fake:test-model", "TargetProject");
        var keyB = ProposalCache.ComputeCacheKey(oneMethod, "fake:test-model", "TargetProject");

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void ComputeCacheKey_SameInput_ProducesSameKey()
    {
        var methods = new[] { new ScopedSource("Foo", "Bar", "return 1;") };

        var keyA = ProposalCache.ComputeCacheKey(methods, "fake:test-model", "TargetProject");
        var keyB = ProposalCache.ComputeCacheKey(methods, "fake:test-model", "TargetProject");

        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void ComputeCacheKey_SameMethodsDifferentProviderIdentity_ProducesDifferentKey()
    {
        // Caught testing against real code with a real Anthropic key: switching attest.json
        // from Ollama to Anthropic on the exact same diff silently reused an old Ollama-authored
        // proposal instead of calling the new provider at all, because the cache key was pure
        // content hash with no notion of who answered it. A stale, already-known-bad proposal
        // from one model kept being served forever as if it were a fresh answer from another.
        var methods = new[] { new ScopedSource("Foo", "Bar", "return 1;") };

        var ollamaKey = ProposalCache.ComputeCacheKey(methods, "ollama:qwen2.5-coder:14b", "TargetProject");
        var anthropicKey = ProposalCache.ComputeCacheKey(methods, "anthropic:claude-sonnet-5", "TargetProject");

        Assert.NotEqual(ollamaKey, anthropicKey);
    }

    [Fact]
    public void ComputeCacheKey_SameMethodsDifferentTargetProject_ProducesDifferentKey()
    {
        // targetProjectName is embedded in the prompt text itself (Proposer.BuildUserPrompt), not
        // just metadata: the exact same diff re-run against a different --project target is a
        // materially different prompt, and must be a cache miss like any other prompt change.
        var methods = new[] { new ScopedSource("Foo", "Bar", "return 1;") };

        var keyA = ProposalCache.ComputeCacheKey(methods, "anthropic:claude-sonnet-5", "ProjectA");
        var keyB = ProposalCache.ComputeCacheKey(methods, "anthropic:claude-sonnet-5", "ProjectB");

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void WriteThenTryRead_RoundTripsTheRawResponse()
    {
        var cacheKey = Guid.NewGuid().ToString("N");
        const string response = """[{"name":"Foo","description":"d","sourceCode":"code"}]""";

        ProposalCache.Write(cacheKey, response);
        var read = ProposalCache.TryRead(cacheKey);

        Assert.Equal(response, read);
    }

    [Fact]
    public void TryRead_UnknownKey_ReturnsNull()
    {
        var cacheKey = Guid.NewGuid().ToString("N");

        Assert.Null(ProposalCache.TryRead(cacheKey));
    }
}
