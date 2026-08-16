using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Attest.Core;

namespace Attest.NET;

/// <summary>How strongly <see cref="OpenAiCompatibleProvider"/> constrains the response to JSON.</summary>
public enum JsonResponseMode
{
    /// <summary>
    /// `response_format: {"type": "json_schema", ...}` -- full structured outputs, enforced at
    /// the token level. Not every OpenAI-compatible backend supports this; a backend that
    /// rejects the parameter fails the whole call rather than silently falling back, since a
    /// second, different request would be a retry the design forbids.
    /// </summary>
    Schema,

    /// <summary>
    /// `response_format: {"type": "json_object"}` -- guarantees valid JSON syntax, not a
    /// specific shape. The default: the widest-supported constraint across OpenAI, Groq,
    /// DeepSeek, Together, and most self-hosted OpenAI-compatible servers.
    /// </summary>
    Object,

    /// <summary>
    /// No `response_format` sent at all. Last resort for a backend that rejects the parameter
    /// entirely; relies purely on the prompt's own instruction and <c>Proposer</c>'s balanced-
    /// bracket JSON extraction.
    /// </summary>
    None,
}

/// <summary>
/// Calls any backend that speaks the OpenAI Chat Completions API shape: OpenAI itself, Groq,
/// DeepSeek, Together, a self-hosted vLLM/llama.cpp server, or Ollama's own OpenAI-compatible
/// endpoint. Unlike <see cref="AnthropicProvider"/>, there is no fixed pricing table for an
/// arbitrary endpoint -- pricing is config-supplied and optional; <see cref="LlmResponse.EstimatedCostUsd"/>
/// is null, not zero, when it is not configured.
/// </summary>
public sealed class OpenAiCompatibleProvider : ILlmProvider
{
    private const int MaxResponseTokens = 8192;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly JsonResponseMode _jsonMode;
    private readonly (decimal InputPerMillion, decimal OutputPerMillion)? _pricing;

    /// <summary>
    /// Creates the provider for the given model and endpoint.
    /// </summary>
    /// <param name="httpClient">The client used to call the configured endpoint.</param>
    /// <param name="apiKey">Bearer token sent with every request.</param>
    /// <param name="model">The model id, exactly as the target service expects it.</param>
    /// <param name="baseUrl">The API base, e.g. "https://api.openai.com/v1"; "/chat/completions" is appended.</param>
    /// <param name="jsonMode">How hard to constrain the response to JSON.</param>
    /// <param name="pricing">Per-million-token USD pricing, if known; null means cost is reported as unknown, never guessed.</param>
    public OpenAiCompatibleProvider(
        HttpClient httpClient,
        string apiKey,
        string model,
        string baseUrl,
        JsonResponseMode jsonMode,
        (decimal InputPerMillion, decimal OutputPerMillion)? pricing)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
        _baseUrl = baseUrl.TrimEnd('/');
        _jsonMode = jsonMode;
        _pricing = pricing;
    }

    /// <inheritdoc/>
    // The base URL and jsonMode are both part of the identity, not just the model name: two
    // different backends can legitimately use the identical model string, and switching
    // jsonMode on the same diff/backend/model is a materially different request and parse
    // contract (BuildResponseFormat, WrapperInstruction) -- the proposal cache must not treat
    // any of these as interchangeable.
    public string Identity => $"openai-compatible:{_baseUrl}:{_model}:{_jsonMode}";

    // "json_object" and "json_schema" response formats constrain the TOP-LEVEL shape of the
    // response to an object, not an array, in several real OpenAI-compatible implementations
    // (confirmed directly against Ollama's own OpenAI-compatible endpoint: asked for a bare
    // JSON array under "json_object" mode, got a single bare object back instead, no array at
    // all). Proposer's shared prompt asks for a bare array; rather than change that prompt per
    // provider, both constrained modes ask for the array wrapped in a single "properties" key
    // instead, and UnwrapPropertiesArray below extracts it back out before Proposer ever sees
    // the text, so Proposer itself stays provider-agnostic.
    private const string WrapperInstruction =
        "\n\nRespond with a single JSON object of the shape {\"properties\": [...]}, where the " +
        "array is exactly what was described above. Do not respond with a bare array at the top " +
        "level; wrap it in a \"properties\" key.";

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            properties = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        description = new { type = "string" },
                        sourceCode = new { type = "string" },
                    },
                    required = new[] { "name", "sourceCode" },
                },
            },
        },
        required = new[] { "properties" },
    };

    /// <inheritdoc/>
    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var effectiveSystemPrompt = _jsonMode == JsonResponseMode.None ? systemPrompt : systemPrompt + WrapperInstruction;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = JsonContent.Create(new OpenAiRequestDto(
            _model,
            [new OpenAiMessageDto("system", effectiveSystemPrompt), new OpenAiMessageDto("user", userPrompt)],
            MaxResponseTokens,
            BuildResponseFormat()));

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException
            && !cancellationToken.IsCancellationRequested)
        {
            // InvalidOperationException/NotSupportedException are HttpClient's own reaction to a
            // baseUrl that isn't a valid absolute http(s) URI (a relative or scheme-less string,
            // or an unsupported scheme) -- ProviderFactory validates this before ever
            // constructing this class, but that validation living one layer away is exactly the
            // kind of thing a future refactor could silently drop, so this is defense in depth,
            // not the primary guard.
            throw new AttestProposalFailedException($"Could not reach '{_baseUrl}': {ex.Message}", "");
        }

        using (httpResponse)
        {
            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorBody = await ReadBodySafelyAsync(httpResponse, cancellationToken).ConfigureAwait(false);
                throw new AttestProposalFailedException(
                    $"'{_baseUrl}' returned {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}.", errorBody);
            }

            OpenAiResponseDto? body;
            try
            {
                body = await httpResponse.Content.ReadFromJsonAsync<OpenAiResponseDto>(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw new AttestProposalFailedException(
                    $"Response body from '{_baseUrl}' was not valid JSON: {ex.Message}",
                    await ReadBodySafelyAsync(httpResponse, cancellationToken).ConfigureAwait(false));
            }

            if (body is null || body.Choices.Count == 0)
                throw new AttestProposalFailedException($"Response body from '{_baseUrl}' had no choices.", "");

            // Every field below is nullable in the DTO because System.Text.Json's default
            // reflection deserializer silently accepts a missing or JSON-null member into a
            // non-nullable-looking record property instead of throwing (no `required` marker,
            // no RespectNullableAnnotations configured) -- a self-hosted or non-conforming
            // OpenAI-compatible server omitting "usage", or a message with a null "content" (a
            // real, spec-legal shape for a tool-call finish_reason or a refusal), must fail with
            // a named exception here, not a raw NullReferenceException/ArgumentNullException
            // three layers up in Proposer or UnwrapPropertiesArray.
            var rawText = body.Choices[0].Message?.Content
                ?? throw new AttestProposalFailedException($"Response from '{_baseUrl}' had no message content.", "");
            var usage = body.Usage
                ?? throw new AttestProposalFailedException($"Response from '{_baseUrl}' had no \"usage\" field.", "");

            var text = _jsonMode == JsonResponseMode.None ? rawText : UnwrapPropertiesArray(rawText);
            var cost = _pricing is { } pricing
                ? usage.PromptTokens / 1_000_000m * pricing.InputPerMillion
                    + usage.CompletionTokens / 1_000_000m * pricing.OutputPerMillion
                : (decimal?)null;

            return new LlmResponse(text, usage.PromptTokens, usage.CompletionTokens, cost);
        }
    }

    private object? BuildResponseFormat() => _jsonMode switch
    {
        JsonResponseMode.Schema => new { type = "json_schema", json_schema = new { name = "attest_property_proposals", strict = true, schema = ResponseSchema } },
        JsonResponseMode.Object => new { type = "json_object" },
        JsonResponseMode.None => null,
        _ => throw new ArgumentOutOfRangeException(nameof(_jsonMode), _jsonMode, "Unknown JsonResponseMode."),
    };

    // Both constrained modes wrap the array in {"properties": [...]} (see WrapperInstruction);
    // this undoes that so Proposer, which expects a bare array, never has to know this provider
    // exists. GetRawText() re-serializes the array exactly as received, not re-encoded from a
    // deserialized model, so nothing about the candidates' own content can be altered here.
    private static string UnwrapPropertiesArray(string rawText)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawText);
        }
        catch (JsonException ex)
        {
            throw new AttestProposalFailedException($"Response was not a JSON object: {ex.Message}", rawText);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("properties", out var propertiesElement))
                throw new AttestProposalFailedException("Response JSON did not contain a \"properties\" array as instructed.", rawText);

            return propertiesElement.GetRawText();
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

    private sealed record OpenAiRequestDto(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<OpenAiMessageDto> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("response_format"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? ResponseFormat);

    private sealed record OpenAiMessageDto(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record OpenAiResponseDto(
        [property: JsonPropertyName("choices")] List<OpenAiChoiceDto> Choices,
        [property: JsonPropertyName("usage")] OpenAiUsageDto? Usage);

    private sealed record OpenAiChoiceDto([property: JsonPropertyName("message")] OpenAiMessageDto? Message);

    private sealed record OpenAiUsageDto(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
