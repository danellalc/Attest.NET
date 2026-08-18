using Attest.Cli;
using Attest.Core;

namespace Attest.UnitTests;

public class CompareSuiteReportRendererTests
{
    [Fact]
    public void Render_ZeroTestedMutants_ReportsZeroCleanly()
    {
        var result = new CompareSuiteResult(0, [], []);

        var rendered = CompareSuiteReportRenderer.Render(result);

        Assert.Contains("0 mutants tested", rendered);
    }

    [Fact]
    public void Render_AllMutantsKilled_ReportsFullKillRateAndNoSurvivedSection()
    {
        var kill = new MutantKill("Arithmetic mutation", "Calculator.cs", 10, 5, "a - b");
        var result = new CompareSuiteResult(1, [kill], []);

        var rendered = CompareSuiteReportRenderer.Render(result);

        Assert.Contains("killed 1/1 mutants", rendered);
        Assert.Contains("100%", rendered);
        Assert.DoesNotContain("Survived:", rendered);
    }

    [Fact]
    public void Render_SomeMutantsSurvived_ListsEachWithFileAndLine()
    {
        var killed = new MutantKill("Arithmetic mutation", "Calculator.cs", 10, 5, "a - b");
        var survived = new MutantKill("Arithmetic mutation", "Calculator.cs", 15, 5, "a / b");
        var result = new CompareSuiteResult(2, [killed], [survived]);

        var rendered = CompareSuiteReportRenderer.Render(result);

        Assert.Contains("killed 1/2 mutants", rendered);
        Assert.Contains("50%", rendered);
        Assert.Contains("Survived:", rendered);
        Assert.Contains("Calculator.cs:15", rendered);
        Assert.Contains("Arithmetic mutation", rendered);
    }
}
