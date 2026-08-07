using System.Net.Http.Json;
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
            ["claude-opus-5"] = (15.00m, 75.00m),
            ["claude-haiku-4-5-20251001"] = (0.80m, 4.00m),
        };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicProvider(HttpClient httpClient, string apiKey, string model)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
    }

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

        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadFromJsonAsync<AnthropicResponseDto>(cancellationToken).ConfigureAwait(false)
            ?? throw new AttestProposalFailedException("Anthropic response body was empty.", "");

        var text = string.Concat(body.Content.Where(block => block.Type == "text").Select(block => block.Text));
        var cost = body.Usage.InputTokens / 1_000_000m * pricing.InputPerMillion
            + body.Usage.OutputTokens / 1_000_000m * pricing.OutputPerMillion;

        return new LlmResponse(text, body.Usage.InputTokens, body.Usage.OutputTokens, cost);
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
