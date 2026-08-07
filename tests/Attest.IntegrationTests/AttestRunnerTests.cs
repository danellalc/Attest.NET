using System.Diagnostics;
using Attest.Cli;
using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

// Same convention as DiffScopeTests: the repository built here is left under %TEMP% rather
// than deleted, since MSBuildWorkspace's engine can hold a file open under .git/objects well
// past this test returning.
[Trait("Category", "Integration")]
public class AttestRunnerTests
{
    private readonly string _repositoryRoot = Path.Combine(Path.GetTempPath(), $"attest-runner-{Guid.NewGuid():N}");

    public AttestRunnerTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Fact]
    public async Task RunAsync_RealPipeline_DeliversGoodCandidate_RejectsUnsynthesizableOne_CachesOnRerun()
    {
        var projectDir = Path.Combine(_repositoryRoot, "Fixture");
        Directory.CreateDirectory(projectDir);

        var csprojPath = Path.Combine(projectDir, "Fixture.csproj");
        await File.WriteAllTextAsync(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        var calculatorPath = Path.Combine(projectDir, "Calculator.cs");
        await File.WriteAllTextAsync(calculatorPath, """
            namespace Fixture;

            public class Calculator
            {
                public int Add(int a, int b) => a + b;
            }
            """);

        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("add", ".");
        var baseCommit = await CommitAsync("initial");

        // The marker makes this run's changed source unique, so the Proposer's own
        // content-hash cache (which persists under %TEMP% across test runs, same convention as
        // the Synthesizer's scratch cache) cannot serve a stale hit from an earlier run of this
        // exact test and skip the very call the first assertions below depend on.
        var marker = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(calculatorPath, $$"""
            namespace Fixture;

            public class Calculator
            {
                // test-marker: {{marker}}
                public int Add(int a, int b) => a + b + 0;
            }
            """);

        const string goodCandidateJson = """
            {"name": "AddIsCommutative", "description": "Addition does not depend on argument order.", "sourceCode": "[Property]\npublic bool AddIsCommutative(int a, int b)\n{\n    var calculator = new Fixture.Calculator();\n    return calculator.Add(a, b) == calculator.Add(b, a);\n}"}
            """;
        const string badCandidateJson = """
            {"name": "ThisDoesNotCompile", "description": "Deliberately broken.", "sourceCode": "this is not valid C#;"}
            """;

        var provider = new FakeLlmProvider(new LlmResponse($"[{goodCandidateJson}, {badCandidateJson}]", 100, 50, 0.001m));

        var runner = new AttestRunner(
            new DiffScope(),
            new Sanitizer(),
            new Proposer(provider),
            new Synthesizer(),
            new Validator(),
            new Falsifier(),
            new EvidenceReporter(new Falsifier()));

        var first = await runner.RunAsync(_repositoryRoot, csprojPath, baseCommit, 200, CancellationToken.None);

        Assert.Equal(2, first.Report.ProposedCount);
        Assert.Contains(first.Report.Delivered, d => d.Candidate.Name == "AddIsCommutative");
        Assert.Contains(
            first.Report.Rejected,
            r => r.Candidate.Name == "ThisDoesNotCompile" && r.Reason == RejectionReason.Unsynthesizable);
        Assert.False(first.FromCache);
        Assert.Equal(1, provider.CallCount);

        var rendered = ReportRenderer.Render(first);
        Assert.Contains("AddIsCommutative", rendered);
        Assert.Contains("[Unsynthesizable] ThisDoesNotCompile", rendered);

        var second = await runner.RunAsync(_repositoryRoot, csprojPath, baseCommit, 200, CancellationToken.None);

        Assert.True(second.FromCache);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(0m, second.Usage.EstimatedCostUsd);

        // Same decisions on rerun, not necessarily the same bytes: ThisDoesNotCompile has no
        // successful build to cache (Synthesizer only caches a build that produced an
        // assembly), so it genuinely recompiles every run, and dotnet's own raw build output
        // carries non-deterministic noise (elapsed-time text, restore-cache-state messages)
        // even though the outcome is stable. The reproducibility the Proposer's cache actually
        // buys is: the LLM is not called again, and every candidate lands in the same bucket
        // for the same reason.
        Assert.Equal(first.Report.ProposedCount, second.Report.ProposedCount);
        Assert.Equal(
            first.Report.Delivered.Select(d => d.Candidate.Name).Order(),
            second.Report.Delivered.Select(d => d.Candidate.Name).Order());
        Assert.Equal(
            first.Report.Rejected.Select(r => (r.Candidate.Name, r.Reason)).Order(),
            second.Report.Rejected.Select(r => (r.Candidate.Name, r.Reason)).Order());
        Assert.Contains(second.Report.Delivered, d => d.Candidate.Name == "AddIsCommutative");
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
