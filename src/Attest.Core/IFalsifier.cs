namespace Attest.Core;

/// <summary>
/// Mutates only the files in <see cref="MutationScope"/> and re-runs a validated test against
/// every mutant. Deterministic: no LLM call belongs here.
/// </summary>
public interface IFalsifier
{
    /// <exception cref="AttestMutantCountMismatchException">
    /// The mutator tested more mutants than the requested scope allows.
    /// </exception>
    Task<FalsificationResult> FalsifyAsync(SynthesizedTest test, MutationScope scope, CancellationToken cancellationToken);
}
