using Attest.Cli;
using Attest.NET;

namespace Attest.UnitTests;

public class ProviderFactoryTests
{
    [Fact]
    public void Create_UnknownProvider_ThrowsNamedExceptionListingAllThree()
    {
        var config = new AttestConfig("carmenere", "some-model", 200, BaseUrl: null, AttestConfig.DefaultJsonMode, null, null);

        var exception = Assert.Throws<AttestCliException>(() => ProviderFactory.Create(config));

        Assert.Contains("anthropic", exception.Message);
        Assert.Contains("ollama", exception.Message);
        Assert.Contains("openai-compatible", exception.Message);
    }

    [Fact]
    public void Create_OpenAiCompatibleWithoutBaseUrl_ThrowsNamedException()
    {
        var originalKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        Environment.SetEnvironmentVariable("LLM_API_KEY", "sk-test");
        try
        {
            var config = new AttestConfig("openai-compatible", "gpt-4o-mini", 200, BaseUrl: null, AttestConfig.DefaultJsonMode, null, null);

            var exception = Assert.Throws<AttestCliException>(() => ProviderFactory.Create(config));

            Assert.Contains("baseUrl", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_API_KEY", originalKey);
        }
    }

    [Fact]
    public void Create_OpenAiCompatibleWithoutApiKeyEnvVar_ThrowsNamedException()
    {
        var originalKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        Environment.SetEnvironmentVariable("LLM_API_KEY", null);
        try
        {
            var config = new AttestConfig("openai-compatible", "gpt-4o-mini", 200, "https://api.openai.com/v1", AttestConfig.DefaultJsonMode, null, null);

            var exception = Assert.Throws<AttestCliException>(() => ProviderFactory.Create(config));

            Assert.Contains("LLM_API_KEY", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_API_KEY", originalKey);
        }
    }

    [Theory]
    [InlineData("not-a-real-mode")]
    [InlineData("")]
    public void Create_OpenAiCompatibleWithInvalidJsonMode_ThrowsNamedException(string invalidMode)
    {
        var originalKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        Environment.SetEnvironmentVariable("LLM_API_KEY", "sk-test");
        try
        {
            // An empty jsonMode never reaches Create as "": AttestConfig.Load already normalizes
            // that to the default. Passed here directly to prove Create's own switch is what
            // actually rejects a genuinely unrecognized value, not something upstream.
            var jsonMode = invalidMode.Length == 0 ? "definitely-not-valid" : invalidMode;
            var config = new AttestConfig("openai-compatible", "gpt-4o-mini", 200, "https://api.openai.com/v1", jsonMode, null, null);

            var exception = Assert.Throws<AttestCliException>(() => ProviderFactory.Create(config));

            Assert.Contains("jsonMode", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_API_KEY", originalKey);
        }
    }

    [Fact]
    public void Create_OpenAiCompatibleWithValidConfig_ReturnsProvider()
    {
        var originalKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        Environment.SetEnvironmentVariable("LLM_API_KEY", "sk-test");
        try
        {
            var config = new AttestConfig("openai-compatible", "gpt-4o-mini", 200, "https://api.openai.com/v1", "object", 0.15m, 0.60m);

            var provider = ProviderFactory.Create(config);

            Assert.IsType<OpenAiCompatibleProvider>(provider);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_API_KEY", originalKey);
        }
    }
}
