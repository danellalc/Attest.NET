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

    [Theory]
    [InlineData("api.openai.com/v1")]
    [InlineData("localhost:8000/v1")]
    [InlineData("ftp://api.openai.com/v1")]
    public void Create_OpenAiCompatibleWithNonAbsoluteHttpBaseUrl_ThrowsNamedException(string malformedBaseUrl)
    {
        // Caught by the audit: a scheme-less or non-http(s) baseUrl reaches HttpClient as a
        // relative URI or an unsupported scheme and throws InvalidOperationException/
        // NotSupportedException at call time, well past where a clean config-time error belongs
        // -- and attest doctor's own openai-compatible check made no live call, so it reported
        // this misconfiguration as [OK]. Caught here, at config validation, instead.
        var originalKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        Environment.SetEnvironmentVariable("LLM_API_KEY", "sk-test");
        try
        {
            var config = new AttestConfig("openai-compatible", "gpt-4o-mini", 200, malformedBaseUrl, AttestConfig.DefaultJsonMode, null, null);

            var exception = Assert.Throws<AttestCliException>(() => ProviderFactory.Create(config));

            Assert.Contains("baseUrl", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_API_KEY", originalKey);
        }
    }

    [Theory]
    [InlineData("SCHEMA")]
    [InlineData("Object")]
    [InlineData("NoNe")]
    public void Create_OpenAiCompatibleJsonModeIsCaseInsensitive(string mixedCaseJsonMode)
    {
        // A mutation that dropped ToLowerInvariant() (or swapped it for ToUpperInvariant())
        // passed the entire existing suite before this test existed: every prior test used an
        // already-lowercase jsonMode value, so nothing proved the case-folding itself did
        // anything. "Schema" is also the natural capitalization a user would type, matching the
        // JsonResponseMode.Schema enum name visible in code and docs.
        var originalKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        Environment.SetEnvironmentVariable("LLM_API_KEY", "sk-test");
        try
        {
            var config = new AttestConfig("openai-compatible", "gpt-4o-mini", 200, "https://api.openai.com/v1", mixedCaseJsonMode, null, null);

            var provider = ProviderFactory.Create(config);

            Assert.IsType<OpenAiCompatibleProvider>(provider);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_API_KEY", originalKey);
        }
    }

    [Theory]
    [InlineData(0.15, null)]
    [InlineData(null, 0.60)]
    [InlineData(null, null)]
    public void ResolvePricing_LessThanBothFieldsSet_CollapsesToNull(double? input, double? output)
    {
        // Caught by the audit: a mutation from "&&" to "||" here, or one that defaulted the
        // missing field to 0, would pass every test that only exercised the both-set and
        // both-null cases. A typo'd field name in attest.json leaving only one field populated
        // must collapse to "no pricing configured" (cost reported as not tracked, per llms.txt),
        // never a partial or zero-defaulted price.
        Assert.Null(ProviderFactory.ResolvePricing((decimal?)input, (decimal?)output));
    }

    [Fact]
    public void ResolvePricing_BothFieldsSet_ReturnsBothValues()
    {
        var pricing = ProviderFactory.ResolvePricing(0.15m, 0.60m);

        Assert.Equal((0.15m, 0.60m), pricing);
    }
}
