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
        // real "ping" child process by PID and proves it stops running, not just that the await
        // returned.
        var pidsBefore = Process.GetProcessesByName("ping").Select(p => p.Id).ToHashSet();

        using var cts = new CancellationTokenSource();
        var runTask = ProcessRunner.RunAsync(
            "cmd.exe",
            ["/c", "ping", "-n", "30", "127.0.0.1"],
            Path.GetTempPath(),
            cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        var newPids = Process.GetProcessesByName("ping").Select(p => p.Id).Where(id => !pidsBefore.Contains(id)).ToList();
        Assert.NotEmpty(newPids); // Sanity check: the child process must have actually started.

        cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => runTask);

        // Give the OS a moment to finish tearing down the killed process tree.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var stillRunning = Process.GetProcessesByName("ping").Select(p => p.Id).Where(newPids.Contains).ToList();
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
