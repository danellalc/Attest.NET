using System.Diagnostics;
using Attest.NET;

namespace Attest.UnitTests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CancelledWhileRunning_ThrowsAndKillsTheProcess()
    {
        // The whole point of finding #33: cancellation must actually kill the external process
        // (dotnet build/test, dotnet-stryker), not just abandon waiting for it while it keeps
        // running in the background. Asserting only that RunAsync throws is not enough:
        // Process.WaitForExitAsync throws TaskCanceledException purely from the token being
        // cancelled, whether or not the child process was actually killed, so this tracks the
        // real long-running child process by PID and proves it stops running, not just that the
        // await returned.
        //
        // cmd.exe/ping -n (Windows-only syntax) used to be hardcoded here: it never even started
        // on the Linux CI runner (no cmd.exe there at all), so the very first assertion --
        // "the child process actually started" -- failed immediately, a real cross-platform bug
        // this test itself had, caught only once CI ran on Linux for the first time. `sleep` is
        // a real coreutils binary on Linux/macOS and ships with Git Bash's own toolchain on
        // Windows too, so the exact same process name and command work on every OS this ships
        // to test on -- no OS-conditional branching needed.
        var pidsBefore = Process.GetProcessesByName("sleep").Select(p => p.Id).ToHashSet();

        using var cts = new CancellationTokenSource();
        var runTask = ProcessRunner.RunAsync(
            "sleep",
            ["30"],
            Path.GetTempPath(),
            cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        var newPids = Process.GetProcessesByName("sleep").Select(p => p.Id).Where(id => !pidsBefore.Contains(id)).ToList();
        Assert.NotEmpty(newPids); // Sanity check: the child process must have actually started.

        cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => runTask);

        // Give the OS a moment to finish tearing down the killed process tree.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var stillRunning = Process.GetProcessesByName("sleep").Select(p => p.Id).Where(newPids.Contains).ToList();
        Assert.Empty(stillRunning);
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_ReturnsFailedResultInsteadOfThrowingRaw()
    {
        var result = await ProcessRunner.RunAsync(
            "attest-definitely-not-a-real-executable",
            [],
            Path.GetTempPath(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Could not start", result.CombinedOutput);
    }
}
