using System.Collections.Concurrent;

namespace Attest.NET;

/// <summary>
/// A scratch directory belongs to exactly one candidate, but every stage (Synthesizer,
/// Validator, Falsifier) can be asked to operate on it, and two operations against the same
/// directory at once corrupt each other's build/test/mutation output. Serializes by path.
/// </summary>
internal static class ScratchDirectoryLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public static SemaphoreSlim For(string scratchDirectory) =>
        Locks.GetOrAdd(scratchDirectory, static _ => new SemaphoreSlim(1, 1));
}
