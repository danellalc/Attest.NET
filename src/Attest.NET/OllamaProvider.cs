using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Attest.Core;

namespace Attest.NET;

/// <summary>
/// Calls a local Ollama server. Always free: <see cref="LlmResponse.EstimatedCostUsd"/> is
/// always zero, since there is no API to bill against.
/// </summary>
public sealed class OllamaProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    /// <param name="httpClient">Client whose <see cref="HttpClient.BaseAddress"/> is the Ollama server, e.g. http://localhost:11434.</param>
    /// <param name="model">Model tag as known to the local Ollama server, e.g. "llama3".</param>
    public OllamaProvider(HttpClient httpClient, string model)
    {
        _httpClient = httpClient;
        _model = model;
    }

    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var request = new OllamaRequestDto(
            _model,
            [new OllamaMessageDto("system", systemPrompt), new OllamaMessageDto("user", userPrompt)],
            Stream: false);

        using var httpResponse = await _httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadFromJsonAsync<OllamaResponseDto>(cancellationToken).ConfigureAwait(false)
            ?? throw new AttestProposalFailedException("Ollama response body was empty.", "");

        return new LlmResponse(body.Message.Content, body.PromptEvalCount, body.EvalCount, EstimatedCostUsd: 0m);
    }

    private sealed record OllamaRequestDto(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<OllamaMessageDto> Messages,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OllamaMessageDto(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaResponseDto(
        [property: JsonPropertyName("message")] OllamaMessageDto Message,
        [property: JsonPropertyName("prompt_eval_count")] int PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int EvalCount);
}
