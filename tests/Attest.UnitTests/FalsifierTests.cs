using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class FalsifierTests
{
    private static StrykerMutant Mutant(string status, string mutatorName = "Equality mutation") =>
        new(Id: "0", MutatorName: mutatorName, Replacement: "x", Location: new StrykerLocation(new StrykerPosition(1, 1), new StrykerPosition(1, 2)), Status: status, StatusReason: null);

    [Fact]
    public void ExtractTestedMutants_IgnoresIgnoredStatus()
    {
        var report = new StrykerReport(new Dictionary<string, StrykerFile>
        {
            ["a.cs"] = new StrykerFile([Mutant("Killed"), Mutant("Ignored"), Mutant("Survived"), Mutant("NoCoverage"), Mutant("Timeout")]),
        });

        var tested = Falsifier.ExtractTestedMutants(report);

        Assert.Equal(4, tested.Count);
        Assert.DoesNotContain(tested, entry => entry.Mutant.Status == "Ignored");
    }

    [Fact]
    public void VerifyScope_AllMutantsInScope_ReturnsAllOfThem()
    {
        var tested = new List<(string FilePath, StrykerMutant Mutant)> { ("a.cs", Mutant("Killed")) };
        var scope = new HashSet<string> { System.IO.Path.GetFullPath("a.cs") };

        var result = Falsifier.VerifyScope(tested, scope);

        Assert.Single(result);
    }

    [Fact]
    public void VerifyScope_MutantOutsideScope_ThrowsNamedException()
    {
        var tested = new List<(string FilePath, StrykerMutant Mutant)>
        {
            ("a.cs", Mutant("Killed")),
            ("b.cs", Mutant("Survived")),
        };
        var scope = new HashSet<string> { System.IO.Path.GetFullPath("a.cs") };

        var exception = Assert.Throws<AttestMutantCountMismatchException>(() => Falsifier.VerifyScope(tested, scope));

        Assert.Equal(1, exception.ExpectedCount);
        Assert.Equal(2, exception.ActualCount);
    }

    [Fact]
    public void VerifyScope_SameFileNameDifferentFolder_IsNotAliasedIntoScope()
    {
        var inFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "attest-test-a", "Order.cs");
        var outOfFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "attest-test-b", "Order.cs");
        var tested = new List<(string FilePath, StrykerMutant Mutant)> { (outOfFolder, Mutant("Killed")) };
        var scope = new HashSet<string> { inFolder };

        Assert.Throws<AttestMutantCountMismatchException>(() => Falsifier.VerifyScope(tested, scope));
    }

    [Fact]
    public void VerifyCeiling_UnderLimit_DoesNotThrow()
    {
        Falsifier.VerifyCeiling(testedMutantCount: 5, maxMutants: 10);
    }

    [Fact]
    public void VerifyCeiling_OverLimit_ThrowsNamedException()
    {
        var exception = Assert.Throws<AttestMutantCeilingExceededException>(() => Falsifier.VerifyCeiling(testedMutantCount: 11, maxMutants: 10));

        Assert.Equal(10, exception.MaxMutants);
        Assert.Equal(11, exception.ActualCount);
    }
}
