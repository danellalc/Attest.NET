using Attest.Cli;
using Attest.Core;

namespace Attest.UnitTests;

public class AttestConfigTests
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"attest-config-{Guid.NewGuid():N}");

    public AttestConfigTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Load_NoConfigFile_ThrowsNamedException()
    {
        var exception = Assert.Throws<AttestCliException>(() => AttestConfig.Load(_directory));

        Assert.Contains("attest.json", exception.Message);
    }

    [Fact]
    public void Load_MalformedJson_ThrowsNamedException()
    {
        File.WriteAllText(Path.Combine(_directory, "attest.json"), "{not valid json");

        Assert.Throws<AttestCliException>(() => AttestConfig.Load(_directory));
    }

    [Theory]
    [InlineData("""{"model": "claude-sonnet-5"}""")]
    [InlineData("""{"provider": "anthropic"}""")]
    [InlineData("""{"provider": "", "model": "claude-sonnet-5"}""")]
    public void Load_MissingRequiredField_ThrowsNamedException(string json)
    {
        File.WriteAllText(Path.Combine(_directory, "attest.json"), json);

        Assert.Throws<AttestCliException>(() => AttestConfig.Load(_directory));
    }

    [Fact]
    public void Load_ValidConfig_ReturnsParsedValues()
    {
        File.WriteAllText(Path.Combine(_directory, "attest.json"), """
            {"provider": "anthropic", "model": "claude-sonnet-5", "maxMutants": 50}
            """);

        var config = AttestConfig.Load(_directory);

        Assert.Equal("anthropic", config.Provider);
        Assert.Equal("claude-sonnet-5", config.Model);
        Assert.Equal(50, config.MaxMutants);
    }

    [Fact]
    public void Load_MissingMaxMutants_DefaultsTo200()
    {
        File.WriteAllText(Path.Combine(_directory, "attest.json"), """
            {"provider": "ollama", "model": "llama3"}
            """);

        var config = AttestConfig.Load(_directory);

        Assert.Equal(AttestConfig.DefaultMaxMutants, config.MaxMutants);
        Assert.Equal(200, config.MaxMutants);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Load_NonPositiveMaxMutants_FallsBackToDefaultInsteadOfAcceptingIt(int handEditedValue)
    {
        // InitCommand's own interactive prompt already falls back to the default for the same
        // input (parsed > 0 ? parsed : DefaultMaxMutants); Load trusted a hand-edited
        // attest.json's value verbatim, so "maxMutants": 0 silently became an unusable ceiling
        // that would trip on the very first tested mutant with no hint the config was the cause.
        File.WriteAllText(Path.Combine(_directory, "attest.json"), $$"""
            {"provider": "ollama", "model": "llama3", "maxMutants": {{handEditedValue}}}
            """);

        var config = AttestConfig.Load(_directory);

        Assert.Equal(AttestConfig.DefaultMaxMutants, config.MaxMutants);
    }

    [Fact]
    public void Load_OpenAiCompatibleWithoutJsonMode_DefaultsToObject()
    {
        File.WriteAllText(Path.Combine(_directory, "attest.json"), """
            {"provider": "openai-compatible", "model": "gpt-4o-mini", "baseUrl": "https://api.openai.com/v1"}
            """);

        var config = AttestConfig.Load(_directory);

        Assert.Equal("https://api.openai.com/v1", config.BaseUrl);
        Assert.Equal(AttestConfig.DefaultJsonMode, config.JsonMode);
        Assert.Null(config.InputPricePerMillion);
        Assert.Null(config.OutputPricePerMillion);
    }

    [Fact]
    public void Load_OpenAiCompatibleWithPricingAndJsonMode_ReturnsThemVerbatim()
    {
        File.WriteAllText(Path.Combine(_directory, "attest.json"), """
            {
              "provider": "openai-compatible",
              "model": "gpt-4o-mini",
              "baseUrl": "https://api.openai.com/v1",
              "jsonMode": "schema",
              "inputPricePerMillion": 0.15,
              "outputPricePerMillion": 0.60
            }
            """);

        var config = AttestConfig.Load(_directory);

        Assert.Equal("schema", config.JsonMode);
        Assert.Equal(0.15m, config.InputPricePerMillion);
        Assert.Equal(0.60m, config.OutputPricePerMillion);
    }

    [Fact]
    public void Load_AnthropicOrOllamaConfig_LeavesNewFieldsAtDefaults()
    {
        // The three new fields only matter for "openai-compatible"; an existing attest.json for
        // the other two providers must keep working unchanged, with no new required field.
        File.WriteAllText(Path.Combine(_directory, "attest.json"), """
            {"provider": "anthropic", "model": "claude-sonnet-5"}
            """);

        var config = AttestConfig.Load(_directory);

        Assert.Null(config.BaseUrl);
        Assert.Equal(AttestConfig.DefaultJsonMode, config.JsonMode);
    }
}
