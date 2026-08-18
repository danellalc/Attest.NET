using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Attest.NET;

/// <summary>
/// Serializes access to a shared resource identified by a path: a candidate's own scratch
/// directory, but also (deliberately reusing the same primitive) the target project a scratch
/// build's ProjectReference points at, whose obj/bin two unrelated scratch builds corrupt if
/// they build it at once -- a real, reproduced failure (see PLANO.md) once two scratch projects
/// referencing the same target raced inside one process; the same race reaches across separate
/// `attest` processes too, since a candidate's own scratch directory is unique per candidate but
/// the target project it references is not. Serializes by path, both within this process (an
/// in-memory semaphore, no polling) and across separate processes: those share no memory to
/// synchronize with, so only a file lock reaches them.
/// </summary>
internal static class ScratchDirectoryLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InProcessLocks = new();
    private static readonly string CrossProcessLockDirectory = Path.Combine(Path.GetTempPath(), "attest-scratch", ".locks");
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public static async Task<IAsyncDisposable> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        var normalizedPath = Path.GetFullPath(path);
        var semaphore = InProcessLocks.GetOrAdd(normalizedPath, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        FileStream lockFile;
        try
        {
            lockFile = await AcquireLockFileAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            semaphore.Release();
            throw;
        }

        return new Lease(semaphore, lockFile);
    }

    // Hashed rather than taken from the file name verbatim: a candidate's own scratch directory
    // name is already unique (content-hashed by Synthesizer), but a target project's .csproj
    // path is not -- two different repos can both have a "Lib.csproj" -- so only hashing the
    // full path guarantees two distinct resources never collide on the same lock file.
    internal static string ComputeLockFilePath(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path))))[..16].ToLowerInvariant();
        return Path.Combine(CrossProcessLockDirectory, $"{hash}.lock");
    }

    private static async Task<FileStream> AcquireLockFileAsync(string normalizedPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(CrossProcessLockDirectory);
        var lockPath = ComputeLockFilePath(normalizedPath);

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
