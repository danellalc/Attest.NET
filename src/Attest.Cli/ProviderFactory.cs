using Attest.Core;
using Attest.NET;

namespace Attest.Cli;

internal static class ProviderFactory
{
    internal static ILlmProvider Create(AttestConfig config) => config.Provider.ToLowerInvariant() switch
    {
        "anthropic" => CreateAnthropicProvider(config.Model),
        "ollama" => CreateOllamaProvider(config.Model),
        "openai-compatible" => CreateOpenAiCompatibleProvider(config),
        _ => throw new AttestCliException($"Unknown provider '{config.Provider}'. Use \"anthropic\", \"ollama\", or \"openai-compatible\"."),
    };

    private static AnthropicProvider CreateAnthropicProvider(string model)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AttestCliException("ANTHROPIC_API_KEY is not set.");

        return new AnthropicProvider(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, apiKey, model);
    }

    private static OllamaProvider CreateOllamaProvider(string model)
    {
        var baseUrl = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";

        // HttpClient's own 100-second default is shorter than a single cold-loaded CPU-only
        // inference pass on a local model can take (observed directly: ~50s just to load a 7B
        // model before generation starts). One deliberate call with no retry loop is the
        // point, so let it take as long as local hardware needs instead of timing out
        // mid-generation.
        var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(15) };
        return new OllamaProvider(httpClient, model);
    }

    private static OpenAiCompatibleProvider CreateOpenAiCompatibleProvider(AttestConfig config)
    {
        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AttestCliException("LLM_API_KEY is not set.");

        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            throw new AttestCliException("attest.json must specify \"baseUrl\" for the \"openai-compatible\" provider.");

        if (!IsAbsoluteHttpUri(config.BaseUrl))
            throw new AttestCliException(
                $"attest.json's \"baseUrl\" must be an absolute http:// or https:// URL, got '{config.BaseUrl}'. " +
                "A scheme-less value (e.g. \"api.openai.com/v1\") reaches HttpClient as a relative URI and fails with a raw .NET exception instead of a usable error.");

        var jsonMode = config.JsonMode.ToLowerInvariant() switch
        {
            "schema" => JsonResponseMode.Schema,
            "object" => JsonResponseMode.Object,
            "none" => JsonResponseMode.None,
            _ => throw new AttestCliException($"Unknown \"jsonMode\" '{config.JsonMode}'. Use \"schema\", \"object\", or \"none\"."),
        };

        // Same 15-minute allowance as Ollama: a self-hosted OpenAI-compatible server behind this
        // config is just as likely to be running on modest local hardware as Ollama itself is.
        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        return new OpenAiCompatibleProvider(
            httpClient, apiKey, config.Model, config.BaseUrl, jsonMode,
            ResolvePricing(config.InputPricePerMillion, config.OutputPricePerMillion));
    }

    // Documented as all-or-nothing (llms.txt): a typo'd field name in attest.json that leaves
    // only one of the two populated must collapse to "no pricing configured" (cost reported as
    // not tracked), not throw and not silently default the missing side to zero. Extracted so
    // this specific collapsing rule is unit-testable without a live HTTP call.
    internal static (decimal, decimal)? ResolvePricing(decimal? inputPerMillion, decimal? outputPerMillion) =>
        inputPerMillion is { } input && outputPerMillion is { } output ? (input, output) : null;

    // Shared with DoctorCommand's openai-compatible check, so a malformed baseUrl is caught by
    // `attest doctor` before it ever reaches a real run.
    internal static bool IsAbsoluteHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
