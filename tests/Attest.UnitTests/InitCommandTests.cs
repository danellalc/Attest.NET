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
    public void Run_AnthropicOrOllamaChoice_OmitsOpenAiCompatibleOnlyFieldsFromTheWrittenFile()
    {
        // A user who never touches the openai-compatible provider shouldn't see
        // baseUrl/jsonMode/pricing show up as four unexplained "null" lines in a config file
        // they wrote for anthropic or ollama.
        InitCommand.Run(_directory, new StringReader("anthropic\n\n\n"), TextWriter.Null);

        var written = File.ReadAllText(Path.Combine(_directory, "attest.json"));

        Assert.DoesNotContain("baseUrl", written);
        Assert.DoesNotContain("jsonMode", written);
        Assert.DoesNotContain("inputPricePerMillion", written);
        Assert.DoesNotContain("outputPricePerMillion", written);
    }

    [Fact]
    public void Run_OpenAiCompatibleChoice_IncludesBaseUrlButOmitsUnsetPricingFields()
    {
        InitCommand.Run(_directory, new StringReader("openai-compatible\ngpt-4o-mini\nhttps://api.openai.com/v1\n\n"), TextWriter.Null);

        var written = File.ReadAllText(Path.Combine(_directory, "attest.json"));

        Assert.Contains("\"baseUrl\": \"https://api.openai.com/v1\"", written);
        Assert.DoesNotContain("inputPricePerMillion", written);
        Assert.DoesNotContain("outputPricePerMillion", written);
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

        Assert.Contains("Provider [anthropic/ollama/openai-compatible]", output.ToString());
        Assert.Contains("Model (", output.ToString());
        Assert.Contains("Max mutants per run", output.ToString());
    }

    [Fact]
    public void Run_OpenAiCompatibleChoice_PromptsForBaseUrlAndWritesIt()
    {
        var exitCode = InitCommand.Run(_directory, new StringReader("openai-compatible\ngpt-4o-mini\nhttps://api.openai.com/v1\n\n"), TextWriter.Null);

        Assert.Equal(0, exitCode);
        var config = AttestConfig.Load(_directory);
        Assert.Equal("openai-compatible", config.Provider);
        Assert.Equal("gpt-4o-mini", config.Model);
        Assert.Equal("https://api.openai.com/v1", config.BaseUrl);
    }

    [Fact]
    public void Run_AnthropicOrOllamaChoice_NeverPromptsForBaseUrl()
    {
        // The base URL prompt is conditional on the provider choice; anthropic/ollama must
        // consume exactly the same 3 lines of input they always did, or every existing script
        // piping answers to `attest init` breaks the moment this feature ships.
        var exitCode = InitCommand.Run(_directory, new StringReader("ollama\n\n\n"), TextWriter.Null);

        Assert.Equal(0, exitCode);
        var config = AttestConfig.Load(_directory);
        Assert.Null(config.BaseUrl);
    }

    [Fact]
    public void Run_CannotWriteTheFile_ReturnsExitCodeOneWithAnActionableMessageInsteadOfThrowingRaw()
    {
        // Unlike DiffCommand.RunAsync and DoctorCommand.RunAsync (both wrap their whole body
        // into an "attest: ..." message plus a defined exit code), InitCommand.Run had no
        // exception handling anywhere; a filesystem failure while writing attest.json propagated
        // as a raw, unhandled .NET exception with a non-standard exit code instead.
        var missingParent = Path.Combine(_directory, "does-not-exist", "still-does-not-exist");
        var output = new StringWriter();

        var exitCode = InitCommand.Run(missingParent, new StringReader("\n\n\n"), output);

        Assert.Equal(1, exitCode);
        Assert.Contains("attest:", output.ToString());
    }
}
