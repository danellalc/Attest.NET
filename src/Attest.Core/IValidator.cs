namespace Attest.Core;

/// <summary>
/// Runs a synthesized test against the current, unmutated code, twice with different seeds.
/// Deterministic given the seed: no LLM call belongs here.
/// </summary>
public interface IValidator
{
    Task<ValidationResult> ValidateAsync(SynthesizedTest test, CancellationToken cancellationToken);
}
