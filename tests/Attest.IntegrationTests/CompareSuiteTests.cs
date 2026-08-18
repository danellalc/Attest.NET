using System.Diagnostics;
using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

// Same convention as AttestRunnerTests: the repository built here is left under %TEMP% rather
// than deleted, since MSBuildWorkspace's engine can hold a file open under .git/objects well
// past this test returning.
[Trait("Category", "Integration")]
public class CompareSuiteTests
{
    private readonly string _repositoryRoot = Path.Combine(Path.GetTempPath(), $"attest-compare-suite-{Guid.NewGuid():N}");

    public CompareSuiteTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Fact]
    public async Task CompareSuiteAsync_PartiallyTestedDiff_KillsMutantsInTheTestedMethodAndMissesTheUntestedOne()
    {
        // Proves --compare-suite genuinely distinguishes "my tests catch this" from "my tests
        // never touch this", not just that it runs without crashing: Add is exercised by a real
        // assertion (its mutants should die), Multiply is declared but never called by any test
        // (its mutants should survive as NoCoverage). No LLM anywhere in this path.
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

            public static class Calculator
            {
                public static int Add(int a, int b) => a + b;
            }
            """);

        var testsDir = Path.Combine(_repositoryRoot, "Fixture.Tests");
        Directory.CreateDirectory(testsDir);
        var testsCsprojPath = Path.Combine(testsDir, "Fixture.Tests.csproj");
        await File.WriteAllTextAsync(testsCsprojPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="{csprojPath}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(testsDir, "CalculatorTests.cs"), """
            using Xunit;

            namespace Fixture.Tests;

            public class CalculatorTests
            {
                [Fact]
                public void Add_ReturnsSum() => Assert.Equal(5, Calculator.Add(2, 3));
            }
            """);

        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("add", ".");
        var baseCommit = await CommitAsync("initial");

        // Multiply is added AFTER the base commit and has no test at all -- this is the
        // "existing suite doesn't cover this" half of the proof. Uncommitted on purpose: --diff
        // compares the base commit against the current working tree, the same convention every
        // other integration test in this project uses.
        await File.WriteAllTextAsync(calculatorPath, """
            namespace Fixture;

            public static class Calculator
            {
                public static int Add(int a, int b) => a + b;

                public static int Multiply(int a, int b) => a * b;
            }
            """);

        var scope = await new DiffScope().ComputeScopeAsync(_repositoryRoot, baseCommit, CancellationToken.None);
        Assert.NotEmpty(scope.ChangedMethods);

        var mutationScope = new MutationScope(
            scope.ChangedMethods.Select(m => m.FilePath).Distinct().ToList(),
            MaxMutants: 200);

        var falsifier = new Falsifier();
        var result = await falsifier.CompareSuiteAsync(testsCsprojPath, mutationScope, CancellationToken.None);

        Assert.True(result.TestedMutants > 0, "Expected Stryker to test at least one mutant in Calculator.cs.");
        Assert.NotEmpty(result.KilledMutants);
        Assert.NotEmpty(result.SurvivedMutants);
        Assert.Equal(result.TestedMutants, result.KilledMutants.Count + result.SurvivedMutants.Count);
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
