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

    [Fact]
    public void SelectMatchingProjectReference_SingleReference_ReturnsItUnconditionally()
    {
        // A scratch project always has exactly one ProjectReference by construction; no need to
        // check it against scope at all in that case.
        var references = new[] { System.IO.Path.GetFullPath("Lib/Lib.csproj") };
        var scope = new[] { System.IO.Path.GetFullPath("SomewhereElse/Unrelated.cs") };

        var result = Falsifier.SelectMatchingProjectReference(references, scope);

        Assert.Equal(references[0], result);
    }

    [Fact]
    public void SelectMatchingProjectReference_MultipleReferences_PicksTheOneContainingAScopedFile()
    {
        // Caught testing --compare-suite against Attest's own multi-reference test project:
        // Stryker refuses to guess which referenced project to mutate, so this has to resolve
        // the ambiguity the same way a person reading its error message would -- by checking
        // which referenced project's own directory actually contains a file being mutated.
        var libA = System.IO.Path.GetFullPath("LibA/LibA.csproj");
        var libB = System.IO.Path.GetFullPath("LibB/LibB.csproj");
        var scope = new[] { System.IO.Path.GetFullPath("LibB/Calculator.cs") };

        var result = Falsifier.SelectMatchingProjectReference([libA, libB], scope);

        Assert.Equal(libB, result);
    }

    [Fact]
    public void SelectMatchingProjectReference_MultipleReferencesNoneMatchScope_ReturnsNull()
    {
        var libA = System.IO.Path.GetFullPath("LibA/LibA.csproj");
        var libB = System.IO.Path.GetFullPath("LibB/LibB.csproj");
        var scope = new[] { System.IO.Path.GetFullPath("Unrelated/Calculator.cs") };

        var result = Falsifier.SelectMatchingProjectReference([libA, libB], scope);

        Assert.Null(result);
    }
}
