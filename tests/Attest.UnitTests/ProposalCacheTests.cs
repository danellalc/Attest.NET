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

        var keyA = ProposalCache.ComputeCacheKey(twoMethods);
        var keyB = ProposalCache.ComputeCacheKey(oneMethod);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void ComputeCacheKey_SameInput_ProducesSameKey()
    {
        var methods = new[] { new ScopedSource("Foo", "Bar", "return 1;") };

        var keyA = ProposalCache.ComputeCacheKey(methods);
        var keyB = ProposalCache.ComputeCacheKey(methods);

        Assert.Equal(keyA, keyB);
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
