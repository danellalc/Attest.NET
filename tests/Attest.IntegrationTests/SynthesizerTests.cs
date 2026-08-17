using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

[Trait("Category", "Integration")]
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
    public async Task SynthesizeAsync_DomainTypeWithNoPublicConstructor_ValidatesOnlyWithACustomGeneratorRegistered()
    {
        // The "custom-generator escape hatch" ARCHITECTURE.md calls v1 scope, not a nice-to-have:
        // FsCheck's own reflection-based default cannot construct a type with no public
        // constructor (a private constructor plus a static factory -- the exact shape of a real
        // domain type, and of an EF-attached entity). Proven end to end here, not just that the
        // generated code compiles: the SAME candidate against the SAME target project genuinely
        // behaves differently with the registered generator than without it.
        var root = Path.Combine(Path.GetTempPath(), $"attest-custom-generator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var csprojPath = Path.Combine(root, "MoneyFixture.csproj");
            await File.WriteAllTextAsync(csprojPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="FsCheck" Version="3.3.4" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "Money.cs"), """
                namespace MoneyFixture;

                public sealed class Money
                {
                    public decimal Amount { get; }
                    private Money(decimal amount) => Amount = amount;
                    public static Money Create(decimal amount) => new(amount);
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "MoneyGenerators.cs"), """
                namespace MoneyFixture;

                using FsCheck;
                using FsCheck.Fluent;

                public static class MoneyGenerators
                {
                    public static Arbitrary<Money> Money() =>
                        Arb.From(Gen.Choose(0, 1000).Select(x => MoneyFixture.Money.Create(x)));
                }
                """);

            var candidate = new PropertyCandidate(
                Name: "MoneyAmountIsNeverNegative",
                Description: "A Money instance's Amount is never negative.",
                SourceCode: """
                    [Property]
                    public bool MoneyAmountIsNeverNegative(MoneyFixture.Money money)
                    {
                        return money.Amount >= 0;
                    }
                    """);

            var synthesizer = new Synthesizer();
            var validator = new Validator();

            var withoutGenerator = await synthesizer.SynthesizeAsync(candidate, csprojPath, CancellationToken.None);
            var withoutGeneratorResult = await validator.ValidateAsync(withoutGenerator, CancellationToken.None);

            var withGenerator = await synthesizer.SynthesizeAsync(
                candidate, csprojPath, CancellationToken.None, customGeneratorsType: "MoneyFixture.MoneyGenerators");
            var withGeneratorResult = await validator.ValidateAsync(withGenerator, CancellationToken.None);

            // The exact failure shape of "FsCheck could not construct Money on its own" is not
            // the point being proven (it could be a thrown exception during generation, an
            // inconclusive run, or a result that never reflects real Money instances) -- the
            // point is that registering the generator is what makes the SAME property source
            // genuinely validate, which only a real run, not a compile check, can show.
            Assert.NotEqual(ValidationOutcome.Valid, withoutGeneratorResult.Outcome);
            Assert.Equal(ValidationOutcome.Valid, withGeneratorResult.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
