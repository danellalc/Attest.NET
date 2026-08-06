namespace Attest.Core;

/// <summary>
/// Runs a synthesized test against the current, unmutated code, twice with different seeds.
/// Deterministic given the seed: no LLM call belongs here.
/// </summary>
public interface IValidator
{
    /// <summary>Runs <paramref name="test"/>'s two seeded copies against the current code.</summary>
    /// <param name="test">The synthesized test to validate.</param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    /// <returns>Valid, FailsOnCurrentCode, or Inconsistent, with a human-readable detail.</returns>
    /// <exception cref="AttestValidationFailedException">The run produced no result to read.</exception>
    Task<ValidationResult> ValidateAsync(SynthesizedTest test, CancellationToken cancellationToken);
}
