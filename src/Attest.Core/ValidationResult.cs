namespace Attest.Core;

/// <summary>Outcome of running a synthesized test's two seeded copies against the current code.</summary>
/// <param name="Test">The synthesized test that was run.</param>
/// <param name="Outcome">What the two seeded runs against current code showed.</param>
/// <param name="Detail">Human-readable explanation, e.g. the assertion failure or the seed pair that disagreed.</param>
public sealed record ValidationResult(SynthesizedTest Test, ValidationOutcome Outcome, string? Detail);
