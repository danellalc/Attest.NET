namespace Attest.Core;

/// <summary>
/// Raised by the Falsifier when the number of tested mutants exceeds
/// <see cref="MutationScope.MaxMutants"/>. Exceeding the ceiling is reported by name, never
/// silently truncated.
/// </summary>
public sealed class AttestMutantCeilingExceededException : AttestException
{
    public int MaxMutants { get; }
    public int ActualCount { get; }

    public AttestMutantCeilingExceededException(int maxMutants, int actualCount)
        : base($"Falsification produced {actualCount} tested mutant(s), exceeding the ceiling of {maxMutants}.")
    {
        MaxMutants = maxMutants;
        ActualCount = actualCount;
    }
}
