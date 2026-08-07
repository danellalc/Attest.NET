using System.Collections.Concurrent;

namespace Attest.NET;

/// <summary>
/// A scratch directory belongs to exactly one candidate, but every stage (Synthesizer,
/// Validator, Falsifier) can be asked to operate on it, and two operations against the same
/// directory at once corrupt each other's build/test/mutation output. Serializes by path, both
/// within this process (an in-memory semaphore, no polling) and across separate `attest`
/// processes racing on the exact same content-hashed directory, e.g. two invocations over the
/// same diff: those share no memory to synchronize with, so only a file lock reaches them.
/// </summary>
internal static class ScratchDirectoryLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InProcessLocks = new();
    private static readonly string CrossProcessLockDirectory = Path.Combine(Path.GetTempPath(), "attest-scratch", ".locks");
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public static async Task<IAsyncDisposable> AcquireAsync(string scratchDirectory, CancellationToken cancellationToken)
    {
        var semaphore = InProcessLocks.GetOrAdd(scratchDirectory, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        FileStream lockFile;
        try
        {
            lockFile = await AcquireLockFileAsync(scratchDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            semaphore.Release();
            throw;
        }

        return new Lease(semaphore, lockFile);
    }

    internal static string ComputeLockFilePath(string scratchDirectory) =>
        Path.Combine(CrossProcessLockDirectory, $"{Path.GetFileName(scratchDirectory)}.lock");

    private static async Task<FileStream> AcquireLockFileAsync(string scratchDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(CrossProcessLockDirectory);
        var lockPath = ComputeLockFilePath(scratchDirectory);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly FileStream _lockFile;

        public Lease(SemaphoreSlim semaphore, FileStream lockFile)
        {
            _semaphore = semaphore;
            _lockFile = lockFile;
        }

        public async ValueTask DisposeAsync()
        {
            var lockFilePath = _lockFile.Name;
            await _lockFile.DisposeAsync().ConfigureAwait(false);

            try
            {
                File.Delete(lockFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: another waiter may already have re-created or be holding it.
            }

            _semaphore.Release();
        }
    }
}
