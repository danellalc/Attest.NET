namespace Attest.Core;

/// <summary>
/// Files the Falsifier is allowed to mutate, and the ceiling on how many mutants it may test.
/// File-level for now; line-level scoping needs Roslyn span translation and lands with DiffScope.
/// </summary>
/// <param name="FilePaths">Absolute paths of files eligible for mutation. Never the whole repository.</param>
/// <param name="MaxMutants">Ceiling on tested mutants. Exceeding it must be reported by name, never silently truncated.</param>
public sealed record MutationScope(IReadOnlyList<string> FilePaths, int MaxMutants);
