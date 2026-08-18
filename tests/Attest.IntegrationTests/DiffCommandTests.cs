using System.Diagnostics;
using System.Text.Json;
using Attest.Cli;

namespace Attest.IntegrationTests;

[Trait("Category", "Integration")]
public class DiffCommandTests
{
    private readonly string _repositoryRoot = Path.Combine(Path.GetTempPath(), $"attest-diffcommand-{Guid.NewGuid():N}");

    public DiffCommandTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeCompletion_ReturnsCancelledExitCodeInsteadOfThrowingRaw()
    {
        // Finding #33: cancellation used to be hardcoded to CancellationToken.None all the way
        // from Program.cs, so ProcessRunner's own kill-on-cancel path was unreachable in
        // production. This exercises the real, wired-up path end to end: an already-cancelled
        // token must abort cleanly (exit code 130), not throw an unhandled exception.
        await RunGitAsync("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repositoryRoot, "Foo.cs"), "public class Foo { }");
        await RunGitAsync("add", ".");
        await CommitAsync("initial");

        await File.WriteAllTextAsync(
            Path.Combine(_repositoryRoot, "attest.json"),
            """{"provider": "ollama", "model": "qwen2.5-coder:14b"}""");

        var args = new[]
        {
            "--diff", "HEAD",
            "--project", Path.Combine(_repositoryRoot, "Foo.csproj"),
            "--repo", _repositoryRoot,
        };
        var output = new StringWriter();
        var error = new StringWriter();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exitCode = await DiffCommand.RunAsync(args, output, error, useColor: false, cancellation.Token);

        Assert.Equal(130, exitCode);
        Assert.Contains("cancelled", error.ToString());
    }

    [Fact]
    public async Task RunAsync_FormatJson_RealZeroDiffRun_ProducesValidParseableJson()
    {
        // Real end-to-end proof, not a renderer-level unit test: --format json actually reaches
        // DiffCommand's real AttestRunner.RunAsync path (git, DiffScope, config loading) and the
        // output on stdout is genuinely valid, parseable JSON -- no LLM call needed, since a
        // zero-diff run (--diff HEAD, nothing changed) short-circuits before any proposal call.
        await RunGitAsync("init", "-b", "main");
        var projectPath = Path.Combine(_repositoryRoot, "Foo.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(_repositoryRoot, "Foo.cs"), "public class Foo { }");
        await RunGitAsync("add", ".");
        await CommitAsync("initial");

        await File.WriteAllTextAsync(
            Path.Combine(_repositoryRoot, "attest.json"),
            """{"provider": "ollama", "model": "qwen2.5-coder:14b"}""");

        var args = new[]
        {
            "--diff", "HEAD",
            "--project", projectPath,
            "--format", "json",
            "--repo", _repositoryRoot,
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DiffCommand.RunAsync(args, output, error, useColor: false, CancellationToken.None);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString()); // throws if malformed
        Assert.Equal(0, document.RootElement.GetProperty("proposedCount").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("delivered").GetArrayLength());
    }

    private async Task<string> CommitAsync(string message)
    {
        await RunGitAsync("-c", "user.name=attest-test", "-c", "user.email=attest-test@example.com", "commit", "-m", message);
        return (await RunGitAsync("rev-parse", "HEAD")).Trim();
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
