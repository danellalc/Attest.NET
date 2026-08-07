namespace Attest.Core;

/// <summary>
/// What a diff touches, expanded one hop to direct callers across the whole solution.
/// <see cref="CallerMethods"/> never includes an entry already present in
/// <see cref="ChangedMethods"/>.
/// </summary>
/// <param name="ChangedMethods">Methods whose body the diff modified directly.</param>
/// <param name="CallerMethods">Methods elsewhere in the solution that call a changed method directly.</param>
public sealed record DiffScopeResult(
    IReadOnlyList<ChangedMethod> ChangedMethods,
    IReadOnlyList<ChangedMethod> CallerMethods);
