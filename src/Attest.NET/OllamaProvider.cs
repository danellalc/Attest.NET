using System.Net.Http.Json;
using System.Text.Json;
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

    /// <inheritdoc/>
    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var request = new OllamaRequestDto(
            _model,
            [new OllamaMessageDto("system", systemPrompt), new OllamaMessageDto("user", userPrompt)],
            Stream: false,
            // Structured JSON output needs the model to stop rambling, not explore: low
            // temperature, and a repeat penalty high enough to stop the word-stuttering
            // ("never returns returns") that a default-sampled small local model produces
            // often enough to break its own string escaping, observed directly running this
            // against qwen2.5-coder:7b.
            Options: new OllamaOptionsDto(Temperature: 0.2, RepeatPenalty: 1.3));

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new AttestProposalFailedException($"Could not reach Ollama: {ex.Message}", "");
        }

        using (httpResponse)
        {
            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorBody = await ReadBodySafelyAsync(httpResponse, cancellationToken).ConfigureAwait(false);
                throw new AttestProposalFailedException(
                    $"Ollama returned {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}.", errorBody);
            }

            OllamaResponseDto? body;
            try
            {
                body = await httpResponse.Content.ReadFromJsonAsync<OllamaResponseDto>(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw new AttestProposalFailedException(
                    $"Ollama response body was not valid JSON: {ex.Message}",
                    await ReadBodySafelyAsync(httpResponse, cancellationToken).ConfigureAwait(false));
            }

            if (body is null)
                throw new AttestProposalFailedException("Ollama response body was empty.", "");

            return new LlmResponse(body.Message.Content, body.PromptEvalCount, body.EvalCount, EstimatedCostUsd: 0m);
        }
    }

    private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage httpResponse, CancellationToken cancellationToken)
    {
        try
        {
            return await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return "";
        }
    }

    private sealed record OllamaRequestDto(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<OllamaMessageDto> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaOptionsDto Options);

    private sealed record OllamaOptionsDto(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("repeat_penalty")] double RepeatPenalty);

    private sealed record OllamaMessageDto(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaResponseDto(
        [property: JsonPropertyName("message")] OllamaMessageDto Message,
        [property: JsonPropertyName("prompt_eval_count")] int PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int EvalCount);
}
