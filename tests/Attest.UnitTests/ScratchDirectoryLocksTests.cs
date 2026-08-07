using Attest.NET;

namespace Attest.UnitTests;

public class ScratchDirectoryLocksTests
{
    [Fact]
    public async Task AcquireAsync_LockFileHeldByAnotherHandle_WaitsUntilItIsReleased()
    {
        // Two separate `attest` processes share no memory, so the only way to prove this is a
        // real cross-process lock (not just the in-memory semaphore) is to hold the exact same
        // lock file open exclusively ourselves, the same way an unrelated process would.
        var scratchDirectory = Path.Combine(Path.GetTempPath(), $"attest-scratch-lock-test-{Guid.NewGuid():N}");
        var lockPath = ScratchDirectoryLocks.ComputeLockFilePath(scratchDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        var externalHolder = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var acquireTask = ScratchDirectoryLocks.AcquireAsync(scratchDirectory, CancellationToken.None);

            var completedEarly = await Task.WhenAny(acquireTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.NotSame(acquireTask, completedEarly);
            Assert.False(acquireTask.IsCompleted, "AcquireAsync must not complete while another handle holds the lock file.");

            await externalHolder.DisposeAsync();

            var lease = await acquireTask.WaitAsync(TimeSpan.FromSeconds(5));
            await lease.DisposeAsync();
        }
        finally
        {
            await externalHolder.DisposeAsync();
        }
    }

    [Fact]
    public async Task AcquireAsync_NoContention_CompletesImmediatelyAndCleansUpTheLockFile()
    {
        var scratchDirectory = Path.Combine(Path.GetTempPath(), $"attest-scratch-lock-test-{Guid.NewGuid():N}");
        var lockPath = ScratchDirectoryLocks.ComputeLockFilePath(scratchDirectory);

        var lease = await ScratchDirectoryLocks.AcquireAsync(scratchDirectory, CancellationToken.None);
        Assert.True(File.Exists(lockPath));

        await lease.DisposeAsync();

        Assert.False(File.Exists(lockPath));
    }
}
