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

    [Fact]
    public async Task SynthesizeAsync_ConcurrentCallsForDifferentCandidatesAgainstTheSameTargetProject_AllSucceed()
    {
        // Different candidates hash to DIFFERENT scratch directories (unlike the test above),
        // so the scratch-directory lock alone gives them no contention -- but their generated
        // .csproj files all ProjectReference the SAME PriceCalculatorFixture, and `dotnet build`
        // resolves that reference by building the target project in place, into its own obj/bin.
        // Found in a pre-launch adversarial review (and previously reproduced by this exact
        // mechanism inside this repo's own test suite, see PLANO.md): without a lock on the
        // target project path itself, concurrent builds racing on that shared obj/bin corrupt
        // each other. This drives real concurrency across genuinely different candidates to
        // prove the fix, not just repeat the identical-candidate case above.
        var synthesizer = new Synthesizer();
        const int concurrentCandidates = 6;

        var candidates = Enumerable.Range(0, concurrentCandidates)
            .Select(i => new PropertyCandidate(
                Name: $"ConcurrentTargetRaceCandidate{i}",
                Description: "Different candidates, same target project, synthesized concurrently on purpose.",
                SourceCode: $$"""
                    [Property]
                    public bool ConcurrentTargetRaceCandidate{{i}}(decimal price, decimal percent)
                    {
                        if (price < 0 || percent < 0 || percent > 100)
                            return true;

                        var result = PriceCalculatorFixture.PriceCalculator.ApplyDiscount(price, percent);
                        return result <= price;
                    }
                    """))
            .ToList();

        var tasks = candidates.Select(candidate => synthesizer.SynthesizeAsync(candidate, TargetProjectPath, CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        _output.WriteLine($"All {concurrentCandidates} concurrent candidates against the shared target project succeeded.");

        Assert.Equal(concurrentCandidates, results.Select(r => r.ScratchProjectPath).Distinct().Count());
        Assert.All(results, result => Assert.True(File.Exists(result.ScratchProjectPath)));
    }
}
