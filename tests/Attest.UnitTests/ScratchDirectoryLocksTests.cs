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
    public void ComputeLockFilePath_TwoDifferentPathsSharingAFileName_ProduceDifferentLockFiles()
    {
        // The lock now also protects a target project's real .csproj path (not just a
        // candidate's own content-hashed scratch directory), and a target project's file name
        // is not unique the way a scratch directory's is -- two different repos can both have a
        // "Lib.csproj". Taking Path.GetFileName verbatim (the pre-fix behavior) would have
        // collided these two unrelated projects onto the exact same lock file, serializing
        // builds that have nothing to do with each other.
        var pathA = Path.Combine(Path.GetTempPath(), $"attest-lock-test-{Guid.NewGuid():N}", "Lib.csproj");
        var pathB = Path.Combine(Path.GetTempPath(), $"attest-lock-test-{Guid.NewGuid():N}", "Lib.csproj");

        Assert.NotEqual(ScratchDirectoryLocks.ComputeLockFilePath(pathA), ScratchDirectoryLocks.ComputeLockFilePath(pathB));
    }

    [Fact]
    public void ComputeLockFilePath_SamePathCalledTwice_ProducesTheSameLockFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"attest-lock-test-{Guid.NewGuid():N}", "Lib.csproj");

        Assert.Equal(ScratchDirectoryLocks.ComputeLockFilePath(path), ScratchDirectoryLocks.ComputeLockFilePath(path));
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
