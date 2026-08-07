namespace Attest.Core;

/// <summary>
/// Raised by the DiffScope when it could not determine what a diff touches: the repository,
/// the base ref, or the solution itself could not be read.
/// </summary>
public sealed class AttestDiffScopeFailedException : AttestException
{
    /// <summary>Root of the repository the DiffScope was asked to scope.</summary>
    public string RepositoryRoot { get; }

    /// <param name="repositoryRoot">Root of the repository the DiffScope was asked to scope.</param>
    /// <param name="reason">Why the scope could not be computed, for diagnosis.</param>
    public AttestDiffScopeFailedException(string repositoryRoot, string reason)
        : base($"Could not compute the diff scope for '{repositoryRoot}': {reason}")
    {
        RepositoryRoot = repositoryRoot;
    }
}
