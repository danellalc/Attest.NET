using System.Diagnostics;
using Attest.Cli;

namespace Attest.IntegrationTests;

// Same convention as DiffScopeTests/AttestRunnerTests: left under %TEMP%, not deleted.
[Trait("Category", "Integration")]
public class DoctorCommandTests
{
    private readonly string _repositoryRoot = Path.Combine(Path.GetTempPath(), $"attest-doctor-{Guid.NewGuid():N}");

    public DoctorCommandTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Fact]
    public async Task RunAsync_NotAGitRepository_ReportsGitRepositoryFailureAndNonZeroExit()
    {
        var output = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(_repositoryRoot, output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("[FAIL] git repository", output.ToString());
    }

    [Fact]
    public async Task RunAsync_GitRepoWithoutAttestJson_ReportsThatCheckFailedButStillRunsTheRest()
    {
        await RunGitAsync("init", "-b", "main");

        var output = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(_repositoryRoot, output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        var rendered = output.ToString();
        Assert.Contains("[FAIL] attest.json", rendered);
        Assert.Contains("git fetch depth", rendered);
        Assert.Contains("dotnet SDK", rendered);
    }

    [Fact]
    public async Task RunAsync_GitRepoWithValidConfig_GitAndDotnetChecksPass()
    {
        await RunGitAsync("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repositoryRoot, "attest.json"), """
            {"provider": "ollama", "model": "some-model-not-necessarily-pulled"}
            """);

        var output = new StringWriter();

        await DoctorCommand.RunAsync(_repositoryRoot, output, CancellationToken.None);

        var rendered = output.ToString();
        Assert.Contains("[OK] git:", rendered);
        Assert.Contains("[OK] git fetch depth", rendered);
        Assert.Contains("[OK] dotnet SDK", rendered);
        Assert.Contains("[OK] attest.json: provider=ollama, model=some-model-not-necessarily-pulled", rendered);
    }

    [Fact]
    public async Task RunAsync_OpenAiCompatibleWithBaseUrlAndKey_PassesWithoutALiveNetworkCall()
    {
        // Same reasoning as the Anthropic check: an arbitrary configured backend may not be
        // free, so doctor must not spend money confirming it actually answers.
        await RunGitAsync("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repositoryRoot, "attest.json"), """
            {"provider": "openai-compatible", "model": "gpt-4o-mini", "baseUrl": "https://api.openai.com/v1"}
            """);
        var originalKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        Environment.SetEnvironmentVariable("LLM_API_KEY", "sk-test");

        try
        {
            var output = new StringWriter();

            var exitCode = await DoctorCommand.RunAsync(_repositoryRoot, output, CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Contains("[OK] openai-compatible", output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_API_KEY", originalKey);
        }
    }

    [Fact]
    public async Task RunAsync_OpenAiCompatibleWithoutBaseUrl_FailsWithActionableMessage()
    {
        await RunGitAsync("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repositoryRoot, "attest.json"), """
            {"provider": "openai-compatible", "model": "gpt-4o-mini"}
            """);
        var originalKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        Environment.SetEnvironmentVariable("LLM_API_KEY", "sk-test");

        try
        {
            var output = new StringWriter();

            var exitCode = await DoctorCommand.RunAsync(_repositoryRoot, output, CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.Contains("[FAIL] openai-compatible", output.ToString());
            Assert.Contains("baseUrl", output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_API_KEY", originalKey);
        }
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeCompletion_ReturnsCancelledExitCodeInsteadOfThrowingRaw()
    {
        // Program.cs now wires a real, user-driven CancellationToken into DoctorCommand (via
        // Console.CancelKeyPress); before this fix, cancelling mid-check crashed with an
        // unhandled OperationCanceledException instead of exiting cleanly like DiffCommand does.
        var output = new StringWriter();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exitCode = await DoctorCommand.RunAsync(_repositoryRoot, output, cancellation.Token);

        Assert.Equal(130, exitCode);
        Assert.Contains("cancelled", output.ToString());
    }

    private async Task<string> RunGitAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stdout}{stderr}");

        return stdout;
    }
}
