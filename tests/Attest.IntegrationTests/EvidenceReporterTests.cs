using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

[Trait("Category", "Integration")]
public class EvidenceReporterTests
{
    private static readonly string TargetProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "PriceCalculatorFixture", "PriceCalculatorFixture.csproj"));

    private static readonly string TargetSourcePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "PriceCalculatorFixture", "PriceCalculator.cs"));

    private static readonly MutationScope Scope = new(FilePaths: [TargetSourcePath], MaxMutants: 200);

    private static readonly PropertyCandidate StrongCandidate = new(
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

    private static readonly PropertyCandidate WrongCandidate = new(
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

    private static readonly PropertyCandidate TrivialCandidate = new(
        Name: "ApplyDiscountReturnsADecimal",
        Description: "Trivial on purpose: swallows the exception a bad mutant would throw.",
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

    private static async Task<(ValidationResult Validation, FalsificationResult? Falsification)> RunThroughFalsifierAsync(
        PropertyCandidate candidate)
    {
        var synthesizer = new Synthesizer();
        var synthesized = await synthesizer.SynthesizeAsync(candidate, TargetProjectPath, CancellationToken.None);

        var validator = new Validator();
        var validation = await validator.ValidateAsync(synthesized, CancellationToken.None);

        if (validation.Outcome != ValidationOutcome.Valid)
            return (validation, null);

        var falsifier = new Falsifier();
        var falsification = await falsifier.FalsifyAsync(synthesized, Scope, CancellationToken.None);
        return (validation, falsification);
    }

    [Fact]
    public async Task BuildReportAsync_StrongProperty_IsDelivered()
    {
        var (validation, falsification) = await RunThroughFalsifierAsync(StrongCandidate);
        var reporter = new EvidenceReporter(new Falsifier());

        var report = await reporter.BuildReportAsync(
            [StrongCandidate],
            [validation],
            falsification is null ? [] : [falsification],
            Scope,
            CancellationToken.None);

        Assert.Equal(1, report.ProposedCount);
        var delivered = Assert.Single(report.Delivered);
        Assert.Equal(StrongCandidate, delivered.Candidate);
        Assert.Equal(TargetSourcePath, delivered.Mutant.FilePath);
        Assert.Empty(report.Rejected);
        Assert.Empty(report.Quarantined);
    }

    [Fact]
    public async Task BuildReportAsync_WrongProperty_IsRejectedAsWrong()
    {
        var (validation, falsification) = await RunThroughFalsifierAsync(WrongCandidate);
        var reporter = new EvidenceReporter(new Falsifier());

        var report = await reporter.BuildReportAsync(
            [WrongCandidate],
            [validation],
            falsification is null ? [] : [falsification],
            Scope,
            CancellationToken.None);

        Assert.Empty(report.Delivered);
        var rejection = Assert.Single(report.Rejected);
        Assert.Equal(WrongCandidate, rejection.Candidate);
        Assert.Equal(RejectionReason.Wrong, rejection.Reason);
    }

    [Fact]
    public async Task BuildReportAsync_TrivialProperty_IsRejectedAsTrivial()
    {
        var (validation, falsification) = await RunThroughFalsifierAsync(TrivialCandidate);
        var reporter = new EvidenceReporter(new Falsifier());

        var report = await reporter.BuildReportAsync(
            [TrivialCandidate],
            [validation],
            falsification is null ? [] : [falsification],
            Scope,
            CancellationToken.None);

        Assert.Empty(report.Delivered);
        var rejection = Assert.Single(report.Rejected);
        Assert.Equal(TrivialCandidate, rejection.Candidate);
        Assert.Equal(RejectionReason.Trivial, rejection.Reason);
    }

    [Fact]
    public async Task BuildReportAsync_KillThatDoesNotReproduce_IsDroppedAsTrivial()
    {
        var synthesizer = new Synthesizer();
        var synthesized = await synthesizer.SynthesizeAsync(StrongCandidate, TargetProjectPath, CancellationToken.None);
        var validator = new Validator();
        var validation = await validator.ValidateAsync(synthesized, CancellationToken.None);

        var fabricatedKill = new MutantKill("Fabricated mutation", TargetSourcePath, 999, 1, "not a real mutant");
        var fabricatedFalsification = new FalsificationResult(synthesized, [fabricatedKill]);

        var reporter = new EvidenceReporter(new Falsifier());

        var report = await reporter.BuildReportAsync(
            [StrongCandidate],
            [validation],
            [fabricatedFalsification],
            Scope,
            CancellationToken.None);

        Assert.Empty(report.Delivered);
        var rejection = Assert.Single(report.Rejected);
        Assert.Equal(RejectionReason.Trivial, rejection.Reason);
        Assert.Contains("did not reproduce", rejection.Detail);
    }

    [Fact]
    public async Task BuildReportAsync_ZeroDeliveries_StillReportsProposedCount()
    {
        var (validation, falsification) = await RunThroughFalsifierAsync(TrivialCandidate);
        var reporter = new EvidenceReporter(new Falsifier());

        var report = await reporter.BuildReportAsync(
            [TrivialCandidate],
            [validation],
            falsification is null ? [] : [falsification],
            Scope,
            CancellationToken.None);

        Assert.Equal(1, report.ProposedCount);
        Assert.Empty(report.Delivered);
        Assert.NotEmpty(report.Rejected);
    }
}
