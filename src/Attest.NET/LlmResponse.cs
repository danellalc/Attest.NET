namespace Attest.NET;

/// <summary>A raw completion from an <see cref="ILlmProvider"/>, before the Proposer parses it.</summary>
/// <param name="Content">The model's raw text response.</param>
/// <param name="InputTokens">Tokens sent to the model.</param>
/// <param name="OutputTokens">Tokens the model generated.</param>
/// <param name="EstimatedCostUsd">
/// Estimated cost in USD, at this provider's own published pricing. Null, not zero, when the
/// provider has no way to know its own pricing (an arbitrary <see cref="OpenAiCompatibleProvider"/>
/// endpoint with no pricing configured) -- a report must never imply "this was free" for a
/// backend that might not be.
/// </param>
public sealed record LlmResponse(string Content, int InputTokens, int OutputTokens, decimal? EstimatedCostUsd);
