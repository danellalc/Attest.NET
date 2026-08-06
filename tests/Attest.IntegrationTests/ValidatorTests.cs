using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

[Trait("Category", "Integration")]
public class ValidatorTests
{
    private static readonly string TargetProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "PriceCalculatorFixture", "PriceCalculatorFixture.csproj"));

    [Fact]
    public async Task ValidateAsync_PropertyThatHolds_ReturnsValid()
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

        var validator = new Validator();
        var result = await validator.ValidateAsync(synthesized, CancellationToken.None);

        Assert.Equal(ValidationOutcome.Valid, result.Outcome);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task ValidateAsync_PropertyThatIsWrong_ReturnsFailsOnCurrentCode()
    {
        var candidate = new PropertyCandidate(
            Name: "DiscountAlwaysReducesPrice",
            Description: "Wrong on purpose: a zero percent discount does not reduce the price.",
            SourceCode: """
                [Property]
                public bool DiscountAlwaysReducesPrice(decimal price, decimal percent)
                {
                    if (price < 0 || percent < 0 || percent > 100)
                        return true;

                    var result = PriceCalculatorFixture.PriceCalculator.ApplyDiscount(price, percent);
                    return result < price;
                }
                """);

        var synthesizer = new Synthesizer();
        var synthesized = await synthesizer.SynthesizeAsync(candidate, TargetProjectPath, CancellationToken.None);

        var validator = new Validator();
        var result = await validator.ValidateAsync(synthesized, CancellationToken.None);

        Assert.Equal(ValidationOutcome.FailsOnCurrentCode, result.Outcome);
        Assert.NotNull(result.Detail);
    }
}
