namespace Attest.Core;

/// <summary>
/// Finds what a diff touches: the changed methods, and their direct callers across the whole
/// solution, not just the same project. Deterministic: no LLM call belongs here.
/// </summary>
public interface IDiffScope
{
    /// <summary>Computes the diff scope of the working tree against <paramref name="baseRef"/>.</summary>
    /// <param name="repositoryRoot">Any directory inside the git repository to scope.</param>
    /// <param name="baseRef">Git ref (branch, tag, or commit) to diff the working tree against.</param>
    /// <param name="cancellationToken">Token to cancel the scope computation.</param>
    /// <returns>The changed methods and their direct callers.</returns>
    /// <exception cref="AttestDiffScopeFailedException">
    /// The repository, <paramref name="baseRef"/>, or the solution could not be read.
    /// </exception>
    Task<DiffScopeResult> ComputeScopeAsync(string repositoryRoot, string baseRef, CancellationToken cancellationToken);
}
