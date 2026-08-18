using Attest.Cli;

namespace Attest.UnitTests;

public class CompareSuiteCommandTests
{
    [Fact]
    public async Task RunAsync_MissingDiff_PrintsUsageAndFails() =>
        await AssertUsageErrorAsync(["--compare-suite", "--test-project", "Foo.csproj"]);

    [Fact]
    public async Task RunAsync_MissingTestProject_PrintsUsageAndFails() =>
        await AssertUsageErrorAsync(["--compare-suite", "--diff", "main"]);

    [Fact]
    public async Task RunAsync_NoArgsAtAll_PrintsUsageAndFails() =>
        await AssertUsageErrorAsync(["--compare-suite"]);

    private static async Task AssertUsageErrorAsync(string[] args)
    {
        var error = new StringWriter();

        var exitCode = await CompareSuiteCommand.RunAsync(args, TextWriter.Null, error, cancellationToken: CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage:", error.ToString());
        Assert.Contains("--test-project", error.ToString());
    }

    [Fact]
    public async Task RunAsync_TestProjectPathDoesNotExist_FailsWithNamedError()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"attest-compare-suite-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "attest.json"), """
                {"provider": "ollama", "model": "some-model"}
                """);
            var error = new StringWriter();

            var exitCode = await CompareSuiteCommand.RunAsync(
                ["--compare-suite", "--diff", "main", "--test-project", "DoesNotExist.csproj", "--repo", directory],
                TextWriter.Null, error, cancellationToken: CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.Contains("--test-project path", error.ToString());
            Assert.Contains("does not exist", error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NoAttestJson_FailsWithTheSameNamedErrorAsDiffCommand()
    {
        // compare-suite still loads attest.json, for maxMutants alone -- it never creates an
        // LLM provider from it, but the file itself is not optional just because this path
        // makes no proposal call.
        var directory = Path.Combine(Path.GetTempPath(), $"attest-compare-suite-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var error = new StringWriter();

            var exitCode = await CompareSuiteCommand.RunAsync(
                ["--compare-suite", "--diff", "main", "--test-project", "Foo.csproj", "--repo", directory],
                TextWriter.Null, error, cancellationToken: CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.Contains("No attest.json found", error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
