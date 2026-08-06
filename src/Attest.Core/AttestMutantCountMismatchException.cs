namespace Attest.Core;

/// <summary>
/// Raised by the Falsifier when the mutant count a mutator actually tested does not match
/// the count computed for the requested scope. A silent scope-fallback (mutating more than
/// asked) must never be allowed to pass as a normal result.
/// </summary>
public sealed class AttestMutantCountMismatchException : AttestException
{
    public int ExpectedCount { get; }
    public int ActualCount { get; }

    public AttestMutantCountMismatchException(int expectedCount, int actualCount)
        : base($"Mutation scope mismatch: expected at most {expectedCount} tested mutant(s) within scope, but {actualCount} were reported. Refusing to trust a possibly widened mutation run.")
    {
        ExpectedCount = expectedCount;
        ActualCount = actualCount;
    }
}
