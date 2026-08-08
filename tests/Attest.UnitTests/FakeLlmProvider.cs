using Attest.NET;

namespace Attest.UnitTests;

internal sealed class FakeLlmProvider : ILlmProvider
{
    private readonly LlmResponse _response;

    public int CallCount { get; private set; }
    public string? LastSystemPrompt { get; private set; }
    public string? LastUserPrompt { get; private set; }
    public string Identity { get; }

    public FakeLlmProvider(LlmResponse response, string identity = "fake:test-model")
    {
        _response = response;
        Identity = identity;
    }

    public Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        CallCount++;
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        return Task.FromResult(_response);
    }
}
