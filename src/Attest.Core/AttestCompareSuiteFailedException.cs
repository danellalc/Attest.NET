namespace Attest.Core;

/// <summary>
/// Raised when Stryker itself could not produce a mutation report while comparing against the
/// repo's own existing test suite (process crash, missing runtime) -- as opposed to the suite
/// legitimately killing zero mutants, which is a real <see cref="CompareSuiteResult"/>, not this.
/// </summary>
public sealed class AttestCompareSuiteFailedException : AttestException
{
    /// <summary>The mutation run's own output, for diagnosis.</summary>
    public string RunOutput { get; }

    /// <param name="runOutput">The mutation run's own output, for diagnosis.</param>
    public AttestCompareSuiteFailedException(string runOutput)
        : base("compare-suite produced no mutation report when run.")
    {
        RunOutput = runOutput;
    }
}
