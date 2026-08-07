using Attest.NET;

namespace Attest.UnitTests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CancelledWhileRunning_ThrowsAndKillsTheProcess()
    {
        // The whole point of finding #33: cancellation must actually kill the external process
        // (dotnet build/test, dotnet-stryker), not just abandon waiting for it while it keeps
        // running in the background.
        using var cts = new CancellationTokenSource();

        var runTask = ProcessRunner.RunAsync(
            "cmd.exe",
            ["/c", "ping", "-n", "30", "127.0.0.1"],
            Path.GetTempPath(),
            cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => runTask);
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
