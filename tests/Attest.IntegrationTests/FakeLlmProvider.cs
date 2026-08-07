using Attest.NET;

namespace Attest.IntegrationTests;

internal sealed class FakeLlmProvider : ILlmProvider
{
    private readonly LlmResponse _response;

    public int CallCount { get; private set; }

    public FakeLlmProvider(LlmResponse response)
    {
        _response = response;
    }

    public Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_response);
    }
}
