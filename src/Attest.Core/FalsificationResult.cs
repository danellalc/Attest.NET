namespace Attest.Core;

/// <summary>Outcome of mutating the scope and re-running a validated test against every mutant.</summary>
/// <param name="Test">The synthesized test that was falsified against mutants.</param>
/// <param name="KilledMutants">Every mutant this test killed. Empty means the property is trivial; rejected.</param>
public sealed record FalsificationResult(SynthesizedTest Test, IReadOnlyList<MutantKill> KilledMutants);
