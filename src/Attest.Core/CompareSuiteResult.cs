namespace Attest.Core;

/// <summary>
/// Outcome of running the repo's own existing test suite (no LLM, no synthesis involved) against
/// every mutant in a diff's scope: does the suite the team already has catch real changes?
/// </summary>
/// <param name="TestedMutants">Every mutant Stryker actually tested in the diff's scope.</param>
/// <param name="KilledMutants">Mutants the existing suite caught.</param>
/// <param name="SurvivedMutants">Mutants the existing suite did not catch -- the actionable finding.</param>
public sealed record CompareSuiteResult(
    int TestedMutants,
    IReadOnlyList<MutantKill> KilledMutants,
    IReadOnlyList<MutantKill> SurvivedMutants);
