using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Attest.Core;

namespace Attest.NET;

/// <summary>
/// Calls the Anthropic Messages API. Refuses to estimate cost for a model it has no
/// published pricing for, rather than reporting a silently wrong number.
/// </summary>
public sealed class AnthropicProvider : ILlmProvider
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxResponseTokens = 8192;

    private static readonly IReadOnlyDictionary<string, (decimal InputPerMillion, decimal OutputPerMillion)> PricingUsd =
        new Dictionary<string, (decimal, decimal)>
        {
            ["claude-sonnet-5"] = (3.00m, 15.00m),
            ["claude-opus-5"] = (5.00m, 25.00m),
            ["claude-haiku-4-5-20251001"] = (1.00m, 5.00m),
        };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    /// <summary>
    /// Creates the provider for the given model, called through the given <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">The client used to call the Anthropic Messages API.</param>
    /// <param name="apiKey">The Anthropic API key sent with every request.</param>
    /// <param name="model">The model id to complete against; must have a known price per <see cref="PricingUsd"/>.</param>
    public AnthropicProvider(HttpClient httpClient, string apiKey, string model)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
    }

    /// <inheritdoc/>
    public string Identity => $"anthropic:{_model}";

    /// <inheritdoc/>
    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        if (!PricingUsd.TryGetValue(_model, out var pricing))
            throw new AttestUnknownModelPricingException(_model);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = JsonContent.Create(new AnthropicRequestDto(
            _model,
            MaxResponseTokens,
            systemPrompt,
            [new AnthropicMessageDto("user", userPrompt)]));

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new AttestProposalFailedException($"Could not reach Anthropic: {ex.Message}", "");
        }

        using (httpResponse)
        {
            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorBody = await ReadBodySafelyAsync(httpResponse, cancellationToken).ConfigureAwait(false);
                throw new AttestProposalFailedException(
                    $"Anthropic returned {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}.", errorBody);
            }

            AnthropicResponseDto? body;
            try
            {
                body = await httpResponse.Content.ReadFromJsonAsync<AnthropicResponseDto>(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw new AttestProposalFailedException(
                    $"Anthropic response body was not valid JSON: {ex.Message}",
                    await ReadBodySafelyAsync(httpResponse, cancellationToken).ConfigureAwait(false));
            }

            if (body is null)
                throw new AttestProposalFailedException("Anthropic response body was empty.", "");

            var text = string.Concat(body.Content.Where(block => block.Type == "text").Select(block => block.Text));
            var cost = body.Usage.InputTokens / 1_000_000m * pricing.InputPerMillion
                + body.Usage.OutputTokens / 1_000_000m * pricing.OutputPerMillion;

            return new LlmResponse(text, body.Usage.InputTokens, body.Usage.OutputTokens, cost);
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

    private sealed record AnthropicRequestDto(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] List<AnthropicMessageDto> Messages);

    private sealed record AnthropicMessageDto(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record AnthropicResponseDto(
        [property: JsonPropertyName("content")] List<AnthropicContentBlockDto> Content,
        [property: JsonPropertyName("usage")] AnthropicUsageDto Usage);

    private sealed record AnthropicContentBlockDto(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record AnthropicUsageDto(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);
}
