namespace Attest.Core;

/// <param name="Test">The synthesized test that was falsified against mutants.</param>
/// <param name="KilledMutants">Every mutant this test killed. Empty means the property is trivial; rejected.</param>
public sealed record FalsificationResult(SynthesizedTest Test, IReadOnlyList<MutantKill> KilledMutants);
