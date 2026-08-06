namespace Attest.Core;

/// <param name="Test">The synthesized test that was run.</param>
/// <param name="Outcome">What the two seeded runs against current code showed.</param>
/// <param name="Detail">Human-readable explanation, e.g. the assertion failure or the seed pair that disagreed.</param>
public sealed record ValidationResult(SynthesizedTest Test, ValidationOutcome Outcome, string? Detail);
