using System.Diagnostics;
using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

// Repositories built here are deliberately left under %TEMP% rather than cleaned up:
// MSBuildWorkspace's underlying MSBuild engine keeps process-lifetime state that can hold a
// file open under .git/objects well past ComputeScopeAsync returning, the same reason
// Synthesizer's own scratch output in %TEMP%/attest-scratch is never deleted by its tests
// either.
[Trait("Category", "Integration")]
public class DiffScopeTests
{
    private readonly string _repositoryRoot = Path.Combine(Path.GetTempPath(), $"attest-diffscope-{Guid.NewGuid():N}");

    public DiffScopeTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Fact]
    public async Task ComputeScopeAsync_ChangedPublicMethod_FindsItAndItsCrossProjectCaller()
    {
        WriteFixture();
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("add", ".");
        var baseCommit = await CommitAsync("initial");

        var calculatorPath = Path.Combine(_repositoryRoot, "Lib", "Calculator.cs");
        await File.WriteAllTextAsync(calculatorPath, """
            namespace Lib;

            public class Calculator
            {
                public int Add(int a, int b) => a + b + 0;

                internal int AddInternal(int a, int b) => a + b;
            }
            """);

        var scope = await new DiffScope().ComputeScopeAsync(_repositoryRoot, baseCommit, CancellationToken.None);

        Assert.Contains(scope.ChangedMethods, m => m.MethodName == "Add" && m.ContainingType == "Lib.Calculator");
        Assert.DoesNotContain(scope.ChangedMethods, m => m.MethodName == "AddInternal");

        Assert.Contains(scope.CallerMethods, m => m.MethodName == "UsePublic" && m.ContainingType == "Consumer.Usage");
        Assert.DoesNotContain(scope.CallerMethods, m => m.MethodName == "UseInternal");

        Assert.DoesNotContain(scope.ChangedMethods, m => m.FilePath.Contains("Unrelated"));
        Assert.DoesNotContain(scope.CallerMethods, m => m.FilePath.Contains("Unrelated"));
    }

    [Fact]
    public async Task ComputeScopeAsync_ChangedInternalMethod_FindsCallerAcrossProjectsViaInternalsVisibleTo()
    {
        WriteFixture();
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("add", ".");
        var baseCommit = await CommitAsync("initial");

        var calculatorPath = Path.Combine(_repositoryRoot, "Lib", "Calculator.cs");
        await File.WriteAllTextAsync(calculatorPath, """
            namespace Lib;

            public class Calculator
            {
                public int Add(int a, int b) => a + b;

                internal int AddInternal(int a, int b) => a + b + 0;
            }
            """);

        var scope = await new DiffScope().ComputeScopeAsync(_repositoryRoot, baseCommit, CancellationToken.None);

        Assert.Contains(scope.ChangedMethods, m => m.MethodName == "AddInternal" && m.ContainingType == "Lib.Calculator");
        Assert.Contains(scope.CallerMethods, m => m.MethodName == "UseInternal" && m.ContainingType == "Consumer.Usage");
    }

    [Fact]
    public async Task ComputeScopeAsync_NoCsharpChanges_ReturnsEmptyScope()
    {
        WriteFixture();
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("add", ".");
        var baseCommit = await CommitAsync("initial");

        await File.WriteAllTextAsync(Path.Combine(_repositoryRoot, "README.md"), "unrelated change");

        var scope = await new DiffScope().ComputeScopeAsync(_repositoryRoot, baseCommit, CancellationToken.None);

        Assert.Empty(scope.ChangedMethods);
        Assert.Empty(scope.CallerMethods);
    }

    [Fact]
    public async Task ComputeScopeAsync_RepositoryRootIsASubdirectory_StillFindsCallersOutsideIt()
    {
        // IDiffScope.ComputeScopeAsync's own doc explicitly allows repositoryRoot to be any
        // directory inside the repository, not just its root; project discovery must resolve
        // the true git root the same way the diff itself does, or a subdirectory call silently
        // stops seeing callers that live outside that subdirectory.
        WriteFixture();
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("add", ".");
        var baseCommit = await CommitAsync("initial");

        var calculatorPath = Path.Combine(_repositoryRoot, "Lib", "Calculator.cs");
        await File.WriteAllTextAsync(calculatorPath, """
            namespace Lib;

            public class Calculator
            {
                public int Add(int a, int b) => a + b + 0;

                internal int AddInternal(int a, int b) => a + b;
            }
            """);

        var libSubdirectory = Path.Combine(_repositoryRoot, "Lib");

        var scope = await new DiffScope().ComputeScopeAsync(libSubdirectory, baseCommit, CancellationToken.None);

        Assert.Contains(scope.ChangedMethods, m => m.MethodName == "Add" && m.ContainingType == "Lib.Calculator");
        Assert.Contains(scope.CallerMethods, m => m.MethodName == "UsePublic" && m.ContainingType == "Consumer.Usage");
    }

    [Fact]
    public async Task ComputeScopeAsync_RepositoryRootIsASubdirectory_ProjectLoadFailureReportsTheRealRootNotTheSubdirectory()
    {
        // The fix for the subdirectory case above resolves realRoot once and uses it for the
        // diff and for project discovery, but the two exception sites for a project that fails
        // to load still passed the raw, possibly-subdirectory repositoryRoot parameter through
        // to AttestDiffScopeFailedException. Its own doc calls RepositoryRoot "Root of the
        // repository the DiffScope was asked to scope", so it must reflect the actual resolved
        // root, not whichever subdirectory this particular call happened to be scoped from.
        WriteFixture();
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("add", ".");
        var baseCommit = await CommitAsync("initial");

        var calculatorPath = Path.Combine(_repositoryRoot, "Lib", "Calculator.cs");
        await File.WriteAllTextAsync(calculatorPath, """
            namespace Lib;

            public class Calculator
            {
                public int Add(int a, int b) => a + b + 0;
            }
            """);

        // A project OUTSIDE the "Lib" subdirectory, found only because project discovery walks
        // from the resolved real root, made malformed on purpose so it fails to load.
        await File.WriteAllTextAsync(Path.Combine(_repositoryRoot, "Consumer", "Consumer.csproj"), "not valid project xml <<<");

        var libSubdirectory = Path.Combine(_repositoryRoot, "Lib");

        var exception = await Assert.ThrowsAsync<AttestDiffScopeFailedException>(
            () => new DiffScope().ComputeScopeAsync(libSubdirectory, baseCommit, CancellationToken.None));

        Assert.NotEqual(libSubdirectory, exception.RepositoryRoot);
    }

    [Fact]
    public async Task ComputeScopeAsync_BadBaseRef_ThrowsNamedException()
    {
        WriteFixture();
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("add", ".");
        await CommitAsync("initial");

        await Assert.ThrowsAsync<AttestDiffScopeFailedException>(
            () => new DiffScope().ComputeScopeAsync(_repositoryRoot, "not-a-real-ref", CancellationToken.None));
    }

    [Fact]
    public async Task ScopeThenSanitize_SecretPlantedInScopedFile_NeverSurvivesRedaction()
    {
        WriteFixture();
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("add", ".");
        var baseCommit = await CommitAsync("initial");

        const string plantedSecret = "AKIAIOSFODNN7EXAMPLE";
        var calculatorPath = Path.Combine(_repositoryRoot, "Lib", "Calculator.cs");
        await File.WriteAllTextAsync(calculatorPath, $$"""
            namespace Lib;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    // Planted on purpose: proves a secret in a DiffScope-scoped file never
                    // survives the Sanitizer, however it got there.
                    const string debugKey = "{{plantedSecret}}";
                    return a + b;
                }

                internal int AddInternal(int a, int b) => a + b;
            }
            """);

        var scope = await new DiffScope().ComputeScopeAsync(_repositoryRoot, baseCommit, CancellationToken.None);
        var scopedFilePaths = scope.ChangedMethods.Select(m => m.FilePath)
            .Concat(scope.CallerMethods.Select(m => m.FilePath))
            .Distinct();

        var sanitizer = new Sanitizer();
        foreach (var filePath in scopedFilePaths)
        {
            var sourceCode = await File.ReadAllTextAsync(filePath);
            var firstPass = sanitizer.Sanitize(sourceCode);
            Assert.DoesNotContain(plantedSecret, firstPass.RedactedContent);

            var renderedReport = $"## Evidence\n\nSource considered:\n\n```csharp\n{firstPass.RedactedContent}\n```\n";
            var secondPass = sanitizer.Sanitize(renderedReport);
            Assert.DoesNotContain(plantedSecret, secondPass.RedactedContent);
        }

        Assert.Contains(scope.ChangedMethods, m => m.FilePath == calculatorPath);
    }

    private void WriteFixture()
    {
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Lib"));
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Consumer"));
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Unrelated"));

        File.WriteAllText(Path.Combine(_repositoryRoot, "Lib", "Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <InternalsVisibleTo Include="Consumer" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(_repositoryRoot, "Lib", "Calculator.cs"), """
            namespace Lib;

            public class Calculator
            {
                public int Add(int a, int b) => a + b;

                internal int AddInternal(int a, int b) => a + b;
            }
            """);

        File.WriteAllText(Path.Combine(_repositoryRoot, "Consumer", "Consumer.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(_repositoryRoot, "Consumer", "Usage.cs"), """
            namespace Consumer;

            public class Usage
            {
                public int UsePublic(Lib.Calculator calculator) => calculator.Add(1, 2);

                public int UseInternal(Lib.Calculator calculator) => calculator.AddInternal(3, 4);
            }
            """);

        File.WriteAllText(Path.Combine(_repositoryRoot, "Unrelated", "Unrelated.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(_repositoryRoot, "Unrelated", "Standalone.cs"), """
            namespace Unrelated;

            public class Standalone
            {
                public int NotRelatedAtAll() => 42;
            }
            """);
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
