namespace Attest.NET;

/// <summary>A raw completion from an <see cref="ILlmProvider"/>, before the Proposer parses it.</summary>
/// <param name="Content">The model's raw text response.</param>
/// <param name="InputTokens">Tokens sent to the model.</param>
/// <param name="OutputTokens">Tokens the model generated.</param>
/// <param name="EstimatedCostUsd">Estimated cost in USD, at this provider's own published pricing.</param>
public sealed record LlmResponse(string Content, int InputTokens, int OutputTokens, decimal EstimatedCostUsd);
