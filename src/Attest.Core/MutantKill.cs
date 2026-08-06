namespace Attest.Core;

/// <summary>
/// A single mutant a property was proven to kill. This is the evidence attached to every
/// delivered property.
/// </summary>
/// <param name="MutatorName">The mutation operator applied, e.g. "Equality mutation".</param>
/// <param name="FilePath">Absolute path to the mutated source file.</param>
/// <param name="Line">Line where the mutant was introduced.</param>
/// <param name="Replacement">The mutated code that replaced the original.</param>
public sealed record MutantKill(string MutatorName, string FilePath, int Line, string Replacement);
