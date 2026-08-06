using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class ValidatorTests
{
    [Theory]
    [InlineData(true, true, ValidationOutcome.Valid)]
    [InlineData(false, false, ValidationOutcome.FailsOnCurrentCode)]
    [InlineData(true, false, ValidationOutcome.Inconsistent)]
    [InlineData(false, true, ValidationOutcome.Inconsistent)]
    public void DetermineOutcome_CoversAllFourCombinations(bool firstSeedPassed, bool secondSeedPassed, ValidationOutcome expected)
    {
        var outcome = Validator.DetermineOutcome(firstSeedPassed, secondSeedPassed);

        Assert.Equal(expected, outcome);
    }
}
