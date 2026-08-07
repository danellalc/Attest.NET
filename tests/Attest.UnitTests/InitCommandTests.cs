using Attest.Cli;

namespace Attest.UnitTests;

public class InitCommandTests
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"attest-init-{Guid.NewGuid():N}");

    public InitCommandTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Run_AllDefaults_WritesOllamaConfig()
    {
        var exitCode = InitCommand.Run(_directory, new StringReader("\n\n\n"), TextWriter.Null);

        Assert.Equal(0, exitCode);
        var config = AttestConfig.Load(_directory);
        Assert.Equal("ollama", config.Provider);
        Assert.Equal("qwen2.5-coder:14b", config.Model);
        Assert.Equal(200, config.MaxMutants);
    }

    [Fact]
    public void Run_ExplicitAnthropicChoice_WritesAnthropicDefaultModel()
    {
        var exitCode = InitCommand.Run(_directory, new StringReader("anthropic\n\n\n"), TextWriter.Null);

        Assert.Equal(0, exitCode);
        var config = AttestConfig.Load(_directory);
        Assert.Equal("anthropic", config.Provider);
        Assert.Equal("claude-sonnet-5", config.Model);
    }

    [Fact]
    public void Run_ExplicitValues_WritesThemVerbatim()
    {
        var exitCode = InitCommand.Run(_directory, new StringReader("ollama\nllama3.1:8b\n50\n"), TextWriter.Null);

        Assert.Equal(0, exitCode);
        var config = AttestConfig.Load(_directory);
        Assert.Equal("llama3.1:8b", config.Model);
        Assert.Equal(50, config.MaxMutants);
    }

    [Fact]
    public void Run_InvalidMaxMutants_FallsBackToDefault()
    {
        var exitCode = InitCommand.Run(_directory, new StringReader("ollama\nqwen2.5-coder:14b\nnot-a-number\n"), TextWriter.Null);

        Assert.Equal(0, exitCode);
        var config = AttestConfig.Load(_directory);
        Assert.Equal(AttestConfig.DefaultMaxMutants, config.MaxMutants);
    }

    [Fact]
    public void Run_ExistingConfigDeclinedOverwrite_LeavesFileUntouched()
    {
        InitCommand.Run(_directory, new StringReader("anthropic\ncustom-model\n99\n"), TextWriter.Null);

        var exitCode = InitCommand.Run(_directory, new StringReader("n\n"), TextWriter.Null);

        Assert.Equal(0, exitCode);
        var config = AttestConfig.Load(_directory);
        Assert.Equal("anthropic", config.Provider);
        Assert.Equal("custom-model", config.Model);
        Assert.Equal(99, config.MaxMutants);
    }

    [Fact]
    public void Run_ExistingConfigAcceptedOverwrite_ReplacesFile()
    {
        InitCommand.Run(_directory, new StringReader("anthropic\ncustom-model\n99\n"), TextWriter.Null);

        var exitCode = InitCommand.Run(_directory, new StringReader("y\nollama\nqwen2.5-coder:14b\n200\n"), TextWriter.Null);

        Assert.Equal(0, exitCode);
        var config = AttestConfig.Load(_directory);
        Assert.Equal("ollama", config.Provider);
        Assert.Equal("qwen2.5-coder:14b", config.Model);
    }

    [Fact]
    public void Run_ModelContainingQuoteAndBackslash_RoundTripsCorrectly()
    {
        // Raw string interpolation into the JSON template used to corrupt the file the moment
        // a value contained a '"' or '\': proper JSON escaping must round-trip it instead.
        const string model = "weird\"model\\name";

        var exitCode = InitCommand.Run(_directory, new StringReader($"ollama\n{model}\n\n"), TextWriter.Null);

        Assert.Equal(0, exitCode);
        var config = AttestConfig.Load(_directory);
        Assert.Equal(model, config.Model);
    }

    [Fact]
    public void Run_PromptsAreWrittenToOutput()
    {
        var output = new StringWriter();

        InitCommand.Run(_directory, new StringReader("\n\n\n"), output);

        Assert.Contains("Provider [anthropic/ollama]", output.ToString());
        Assert.Contains("Model (", output.ToString());
        Assert.Contains("Max mutants per run", output.ToString());
    }
}
