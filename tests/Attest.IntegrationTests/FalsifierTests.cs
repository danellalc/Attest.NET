using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

public class FalsifierTests
{
    private static readonly string TargetProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "PriceCalculatorFixture", "PriceCalculatorFixture.csproj"));

    private static readonly string TargetSourcePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "PriceCalculatorFixture", "PriceCalculator.cs"));

    [Fact]
    public async Task FalsifyAsync_StrongProperty_KillsAtLeastOneMutant()
    {
        var candidate = new PropertyCandidate(
            Name: "DiscountNeverExceedsOriginalPrice",
            Description: "Applying a discount never returns a price higher than the original.",
            SourceCode: """
                [Property]
                public bool DiscountNeverExceedsOriginalPrice(decimal price, decimal percent)
                {
                    if (price < 0 || percent < 0 || percent > 100)
                        return true;

                    var result = PriceCalculatorFixture.PriceCalculator.ApplyDiscount(price, percent);
                    return result <= price;
                }
                """);

        var synthesizer = new Synthesizer();
        var synthesized = await synthesizer.SynthesizeAsync(candidate, TargetProjectPath, CancellationToken.None);

        var falsifier = new Falsifier();
        var scope = new MutationScope(FilePaths: [TargetSourcePath], MaxMutants: 200);

        var result = await falsifier.FalsifyAsync(synthesized, scope, CancellationToken.None);

        Assert.NotEmpty(result.KilledMutants);
        Assert.All(result.KilledMutants, kill => Assert.Equal("PriceCalculator.cs", kill.FilePath));
    }

    [Fact]
    public async Task FalsifyAsync_TrivialProperty_KillsNoMutants()
    {
        var candidate = new PropertyCandidate(
            Name: "ApplyDiscountReturnsADecimal",
            Description: "Trivial on purpose: asserts nothing about the actual computed value, and swallows exceptions so an exception-throwing mutant is not enough to kill it.",
            SourceCode: """
                [Property]
                public bool ApplyDiscountReturnsADecimal(decimal price, decimal percent)
                {
                    try
                    {
                        _ = PriceCalculatorFixture.PriceCalculator.ApplyDiscount(price, percent);
                    }
                    catch
                    {
                    }

                    return true;
                }
                """);

        var synthesizer = new Synthesizer();
        var synthesized = await synthesizer.SynthesizeAsync(candidate, TargetProjectPath, CancellationToken.None);

        var falsifier = new Falsifier();
        var scope = new MutationScope(FilePaths: [TargetSourcePath], MaxMutants: 200);

        var result = await falsifier.FalsifyAsync(synthesized, scope, CancellationToken.None);

        Assert.Empty(result.KilledMutants);
    }
}
