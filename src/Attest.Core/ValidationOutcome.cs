namespace Attest.Core;

/// <summary>
/// Result of running a synthesized test against the current, unmutated code, twice with
/// different seeds.
/// </summary>
public enum ValidationOutcome
{
    /// <summary>Passed on both seeded runs. The property may proceed to falsification.</summary>
    Valid,

    /// <summary>Failed on the current, working code. The property is wrong; rejected.</summary>
    FailsOnCurrentCode,

    /// <summary>Passed on one seeded run and failed on the other. Quarantined, not rejected.</summary>
    Inconsistent,
}
