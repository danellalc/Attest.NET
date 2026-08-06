using Attest.Core;
using Attest.NET;
using Xunit.Abstractions;

namespace Attest.IntegrationTests;

/// <summary>
/// Deliberately drives concurrent Synthesizer calls for the identical candidate, which hash
/// to the identical scratch directory on purpose (see Synthesizer's own doc comment). Without
/// ScratchDirectoryLocks this races two writers on the same files; this test exists so that
/// race is caught by something other than unrelated test classes accidentally colliding.
/// </summary>
[Trait("Category", "Integration")]
public class ScratchDirectoryLocksTests
{
    private readonly ITestOutputHelper _output;

    public ScratchDirectoryLocksTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly string TargetProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "PriceCalculatorFixture", "PriceCalculatorFixture.csproj"));

    [Fact]
    public async Task SynthesizeAsync_ConcurrentCallsForIdenticalCandidate_AllSucceedWithTheSameScratchProject()
    {
        var candidate = new PropertyCandidate(
            Name: "ConcurrentDedupCandidate",
            Description: "Same candidate synthesized concurrently on purpose, to force the shared scratch directory.",
            SourceCode: """
                [Property]
                public bool ConcurrentDedupCandidate(decimal price, decimal percent)
                {
                    if (price < 0 || percent < 0 || percent > 100)
                        return true;

                    var result = PriceCalculatorFixture.PriceCalculator.ApplyDiscount(price, percent);
                    return result <= price;
                }
                """);

        const int concurrentCallers = 8;
        var synthesizer = new Synthesizer();

        var tasks = Enumerable.Range(0, concurrentCallers)
            .Select(_ => synthesizer.SynthesizeAsync(candidate, TargetProjectPath, CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        _output.WriteLine($"All {concurrentCallers} concurrent calls returned scratch project: {results[0].ScratchProjectPath}");

        Assert.All(results, result => Assert.Equal(results[0].ScratchProjectPath, result.ScratchProjectPath));
        Assert.True(File.Exists(results[0].ScratchProjectPath));
    }
}
