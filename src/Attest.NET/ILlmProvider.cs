namespace Attest.NET;

/// <summary>
/// A single LLM backend the Proposer can call. V1 has exactly two implementations,
/// <see cref="AnthropicProvider"/> and <see cref="OllamaProvider"/>; a third provider is
/// deliberately not built ahead of a real request for one.
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// A stable string identifying this backend and model (e.g. "anthropic:claude-sonnet-5",
    /// "ollama:qwen2.5-coder:14b"). Folded into the proposal cache key so switching provider or
    /// model on the same diff is a cache miss, never a stale hit from a different model.
    /// </summary>
    string Identity { get; }

    /// <summary>Sends one prompt and returns the model's completion.</summary>
    /// <param name="systemPrompt">Instructions for the model, not shown as a user turn.</param>
    /// <param name="userPrompt">The sanitized content to propose properties for.</param>
    /// <param name="cancellationToken">Token to cancel the call.</param>
    /// <returns>The model's raw response and its cost.</returns>
    Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
