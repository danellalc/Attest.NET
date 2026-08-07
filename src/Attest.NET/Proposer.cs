using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Attest.Core;

namespace Attest.NET;

/// <summary>
/// The only stage that calls an LLM. Builds one prompt per batch of scoped methods, parses
/// the model's response into <see cref="PropertyCandidate"/>, and caches by content hash so
/// the same diff never pays for or reproposes the same properties twice.
/// </summary>
public sealed class Proposer : IProposer
{
    private readonly ILlmProvider _provider;

    public Proposer(ILlmProvider provider)
    {
        _provider = provider;
    }

    public async Task<ProposalResult> ProposeAsync(IReadOnlyList<ScopedSource> scopedMethods, CancellationToken cancellationToken)
    {
        if (scopedMethods.Count == 0)
            return new ProposalResult([], new LlmUsage(0, 0, 0m), FromCache: false);

        var cacheKey = ProposalCache.ComputeCacheKey(scopedMethods);
        if (ProposalCache.TryRead(cacheKey) is { } cached)
            return new ProposalResult(ParseCandidates(cached), new LlmUsage(0, 0, 0m), FromCache: true);

        var userPrompt = BuildUserPrompt(scopedMethods);
        var response = await _provider.CompleteAsync(SystemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);

        var candidates = ParseCandidates(response.Content);
        ProposalCache.Write(cacheKey, response.Content);

        var usage = new LlmUsage(response.InputTokens, response.OutputTokens, response.EstimatedCostUsd);
        return new ProposalResult(candidates, usage, FromCache: false);
    }

    internal static string BuildUserPrompt(IReadOnlyList<ScopedSource> scopedMethods)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Propose FsCheck properties for the following changed methods.");
        builder.AppendLine();

        foreach (var method in scopedMethods)
        {
            builder.AppendLine($"### {method.ContainingType}.{method.MethodName}");
            builder.AppendLine("```csharp");
            builder.AppendLine(method.SanitizedSourceCode);
            builder.AppendLine("```");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    internal static IReadOnlyList<PropertyCandidate> ParseCandidates(string rawResponse)
    {
        var json = ExtractJsonArray(rawResponse);

        List<ProposedPropertyDto>? proposals;
        try
        {
            proposals = JsonSerializer.Deserialize<List<ProposedPropertyDto>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new AttestProposalFailedException($"response is not valid JSON: {ex.Message}", rawResponse);
        }

        if (proposals is null)
            throw new AttestProposalFailedException("response deserialized to null.", rawResponse);

        var candidates = new List<PropertyCandidate>(proposals.Count);
        foreach (var proposal in proposals)
        {
            if (string.IsNullOrWhiteSpace(proposal.Name) || string.IsNullOrWhiteSpace(proposal.SourceCode))
                throw new AttestProposalFailedException("a proposed property is missing 'name' or 'sourceCode'.", rawResponse);

            candidates.Add(new PropertyCandidate(proposal.Name, proposal.Description ?? "", proposal.SourceCode));
        }

        return candidates;
    }

    private static string ExtractJsonArray(string rawResponse)
    {
        var start = rawResponse.IndexOf('[');
        var end = rawResponse.LastIndexOf(']');
        if (start < 0 || end < start)
            throw new AttestProposalFailedException("no JSON array found in the response.", rawResponse);

        return rawResponse[start..(end + 1)];
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record ProposedPropertyDto(string Name, string? Description, string SourceCode);

    private const string SystemPrompt = """
        You propose property-based tests for C# code, to be run with FsCheck.Xunit. You do
        not write example-based tests. Favor these shapes, in order of usefulness: invariant
        (some relation holds for every input), idempotency (applying the operation twice
        equals applying it once), round-trip (encode then decode returns the original),
        ordering (the operation preserves or reverses a known order), metamorphic relation
        (a specific change to the input produces a predictable change to the output).

        Rules for every proposed property:
        - The method signature is `[Property] public bool Name(<FsCheck-generatable parameters>)`.
        - The method name matches the JSON "name" field exactly; it becomes a class member name.
        - Return true for inputs outside the property's domain (guard and return true), never throw.
        - Reference the type under test by its fully qualified name; do not assume any `using`.
        - Do not write a property that is trivially true for every input; a property that
          cannot fail is worthless here, however it is written.
        - You may declare private fields or helper methods alongside the `[Property]` method
          if the property needs them; do not declare a class, namespace, or using directive.

        Respond with a JSON array only, no prose, no markdown fence, matching this shape:
        [{"name": "...", "description": "...", "sourceCode": "..."}]

        Example, for `PriceCalculatorFixture.PriceCalculator.ApplyDiscount(decimal price, decimal percent)`:
        [{"name": "DiscountNeverExceedsOriginalPrice", "description": "Applying a discount never returns a price higher than the original.", "sourceCode": "[Property]\npublic bool DiscountNeverExceedsOriginalPrice(decimal price, decimal percent)\n{\n    if (price < 0 || percent < 0 || percent > 100)\n        return true;\n\n    var result = PriceCalculatorFixture.PriceCalculator.ApplyDiscount(price, percent);\n    return result <= price;\n}"}]
        """;
}
