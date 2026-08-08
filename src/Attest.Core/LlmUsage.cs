namespace Attest.Core;

/// <summary>
/// Cost of one Proposer call, always present in the report: every LLM call is counted,
/// never silent.
/// </summary>
/// <param name="InputTokens">Tokens sent to the model.</param>
/// <param name="OutputTokens">Tokens the model generated.</param>
/// <param name="EstimatedCostUsd">
/// Estimated cost in USD, at the provider's own published pricing. Null, not zero, when the
/// provider has no way to know its own pricing -- never implies "this was free" for a backend
/// that might not be.
/// </param>
public sealed record LlmUsage(int InputTokens, int OutputTokens, decimal? EstimatedCostUsd);
