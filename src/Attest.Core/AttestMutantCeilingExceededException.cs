namespace Attest.Core;

/// <summary>
/// Raised by the Falsifier when the number of tested mutants exceeds
/// <see cref="MutationScope.MaxMutants"/>. Exceeding the ceiling is reported by name, never
/// silently truncated.
/// </summary>
public sealed class AttestMutantCeilingExceededException : AttestException
{
    /// <summary>The configured ceiling that was exceeded.</summary>
    public int MaxMutants { get; }

    /// <summary>How many tested mutants were actually reported.</summary>
    public int ActualCount { get; }

    /// <param name="maxMutants">The configured ceiling that was exceeded.</param>
    /// <param name="actualCount">How many tested mutants were actually reported.</param>
    public AttestMutantCeilingExceededException(int maxMutants, int actualCount)
        : base($"Falsification produced {actualCount} tested mutant(s), exceeding the ceiling of {maxMutants}.")
    {
        MaxMutants = maxMutants;
        ActualCount = actualCount;
    }
}
