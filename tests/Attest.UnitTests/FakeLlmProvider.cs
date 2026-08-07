using Attest.NET;

namespace Attest.UnitTests;

internal sealed class FakeLlmProvider : ILlmProvider
{
    private readonly LlmResponse _response;

    public int CallCount { get; private set; }
    public string? LastSystemPrompt { get; private set; }
    public string? LastUserPrompt { get; private set; }

    public FakeLlmProvider(LlmResponse response)
    {
        _response = response;
    }

    public Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        CallCount++;
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        return Task.FromResult(_response);
    }
}
