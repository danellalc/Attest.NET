using Attest.Core;
using Attest.NET;

namespace Attest.Cli;

internal static class ProviderFactory
{
    internal static ILlmProvider Create(AttestConfig config) => config.Provider.ToLowerInvariant() switch
    {
        "anthropic" => CreateAnthropicProvider(config.Model),
        "ollama" => CreateOllamaProvider(config.Model),
        _ => throw new AttestCliException($"Unknown provider '{config.Provider}'. Use \"anthropic\" or \"ollama\"."),
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
}
