namespace Attest.Core;

/// <summary>
/// Mutates only the files in <see cref="MutationScope"/> and re-runs a validated test against
/// every mutant. Deterministic: no LLM call belongs here.
/// </summary>
public interface IFalsifier
{
    /// <summary>Mutates <paramref name="scope"/> and re-runs <paramref name="test"/> against every mutant.</summary>
    /// <param name="test">The validated test to falsify.</param>
    /// <param name="scope">Which files may be mutated, and the mutant ceiling.</param>
    /// <param name="cancellationToken">Token to cancel the mutation run.</param>
    /// <returns>Every mutant the test killed. Empty means the property is trivial.</returns>
    /// <exception cref="AttestMutantCountMismatchException">
    /// The mutator tested a mutant outside the requested scope.
    /// </exception>
    /// <exception cref="AttestMutantCeilingExceededException">
    /// The mutator tested more mutants than <see cref="MutationScope.MaxMutants"/> allows.
    /// </exception>
    /// <exception cref="AttestFalsificationFailedException">The run produced no mutation report to read.</exception>
    Task<FalsificationResult> FalsifyAsync(SynthesizedTest test, MutationScope scope, CancellationToken cancellationToken);
}
