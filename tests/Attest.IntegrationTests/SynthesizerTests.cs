using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

public class SynthesizerTests
{
    private static readonly string TargetProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "PriceCalculatorFixture", "PriceCalculatorFixture.csproj"));

    [Fact]
    public async Task SynthesizeAsync_ValidProperty_ProducesBuildableScratchProject()
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

        var result = await synthesizer.SynthesizeAsync(candidate, TargetProjectPath, CancellationToken.None);

        Assert.True(File.Exists(result.ScratchProjectPath));
        Assert.Equal(candidate, result.Candidate);
        Assert.Equal("Scratch_DiscountNeverExceedsOriginalPrice.DiscountNeverExceedsOriginalPriceTests_Seed1.DiscountNeverExceedsOriginalPrice", result.FirstSeedTestName);
        Assert.Equal("Scratch_DiscountNeverExceedsOriginalPrice.DiscountNeverExceedsOriginalPriceTests_Seed2.DiscountNeverExceedsOriginalPrice", result.SecondSeedTestName);
        Assert.NotEqual(result.FirstSeedTestName, result.SecondSeedTestName);
    }

    [Fact]
    public async Task SynthesizeAsync_PropertyThatDoesNotCompile_ThrowsAttestSynthesisFailedException()
    {
        var candidate = new PropertyCandidate(
            Name: "BrokenProperty",
            Description: "Deliberately invalid C# to prove synthesis failures are named, not thrown raw.",
            SourceCode: """
                [Property]
                public bool BrokenProperty(decimal price)
                {
                    return this is not even valid csharp;
                }
                """);

        var synthesizer = new Synthesizer();

        var exception = await Assert.ThrowsAsync<AttestSynthesisFailedException>(
            () => synthesizer.SynthesizeAsync(candidate, TargetProjectPath, CancellationToken.None));

        Assert.Equal("BrokenProperty", exception.CandidateName);
        Assert.NotEmpty(exception.BuildOutput);
    }
}
