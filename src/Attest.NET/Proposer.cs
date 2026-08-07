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

    /// <summary>
    /// Creates the proposer over the given LLM backend.
    /// </summary>
    /// <param name="provider">The LLM backend to send prompts to.</param>
    public Proposer(ILlmProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc/>
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

    // A naive first-'['/last-']' scan breaks the moment stray brackets show up in prose around
    // the array (an example like "int[]", or trailing text mentioning "T[]"), or inside a
    // proposed property's own C# source code (array indexers, attribute lists). This instead
    // tries every '[' in turn and, for each, tracks bracket depth while skipping over JSON
    // string literals (respecting '\' escapes) to find where it balances. A balanced span with
    // no object inside it (a bare "[]" from stray prose, not the real proposal) is kept only as
    // a last-resort fallback, so a stray empty pair before the real array never wins over it.
    private static string ExtractJsonArray(string rawResponse)
    {
        string? fallback = null;

        var start = rawResponse.IndexOf('[');
        while (start >= 0)
        {
            var match = TryMatchBalancedArray(rawResponse, start);
            if (match is { ContainsObject: true } found)
                return found.Text;

            fallback ??= match?.Text;
            start = rawResponse.IndexOf('[', start + 1);
        }

        return fallback ?? throw new AttestProposalFailedException("no balanced JSON array found in the response.", rawResponse);
    }

    private static (string Text, bool ContainsObject)? TryMatchBalancedArray(string rawResponse, int start)
    {
        var depth = 0;
        var insideString = false;
        var escapeNext = false;
        var containsObject = false;

        for (var i = start; i < rawResponse.Length; i++)
        {
            var current = rawResponse[i];

            if (insideString)
            {
                if (escapeNext)
                    escapeNext = false;
                else if (current == '\\')
                    escapeNext = true;
                else if (current == '"')
                    insideString = false;

                continue;
            }

            switch (current)
            {
                case '"':
                    insideString = true;
                    break;
                case '{':
                    containsObject = true;
                    depth++;
                    break;
                case '[':
                    depth++;
                    break;
                case ']' or '}':
                    depth--;
                    if (depth == 0)
                        return (rawResponse[start..(i + 1)], containsObject);
                    if (depth < 0)
                        return null;
                    break;
            }
        }

        return null;
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

        Propose at most 2 properties per method. One excellent property beats several mediocre
        ones, and every extra property is another chance to make a mistake in the JSON below.

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
