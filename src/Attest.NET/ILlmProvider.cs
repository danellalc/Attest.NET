namespace Attest.NET;

/// <summary>
/// A single LLM backend the Proposer can call. V1 has exactly two implementations,
/// <see cref="AnthropicProvider"/> and <see cref="OllamaProvider"/>; a third provider is
/// deliberately not built ahead of a real request for one.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Sends one prompt and returns the model's completion.</summary>
    /// <param name="systemPrompt">Instructions for the model, not shown as a user turn.</param>
    /// <param name="userPrompt">The sanitized content to propose properties for.</param>
    /// <param name="cancellationToken">Token to cancel the call.</param>
    /// <returns>The model's raw response and its cost.</returns>
    Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
