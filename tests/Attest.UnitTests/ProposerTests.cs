using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class ProposerTests
{
    [Fact]
    public void ParseCandidates_ValidJsonArray_ReturnsCandidates()
    {
        var response = """
            [{"name": "Foo", "description": "d", "sourceCode": "[Property] public bool Foo() => true;"}]
            """;

        var candidates = Proposer.ParseCandidates(response);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Foo", candidate.Name);
        Assert.Equal("d", candidate.Description);
    }

    [Fact]
    public void ParseCandidates_JsonWrappedInMarkdownFence_StillParses()
    {
        var response = """
            Here are the properties I propose:

            ```json
            [{"name": "Foo", "description": "d", "sourceCode": "[Property] public bool Foo() => true;"}]
            ```
            """;

        var candidates = Proposer.ParseCandidates(response);

        Assert.Single(candidates);
    }

    [Fact]
    public void ParseCandidates_MultipleCandidates_ReturnsAllInOrder()
    {
        var response = """
            [
              {"name": "Foo", "description": "d1", "sourceCode": "[Property] public bool Foo() => true;"},
              {"name": "Bar", "description": "d2", "sourceCode": "[Property] public bool Bar() => true;"}
            ]
            """;

        var candidates = Proposer.ParseCandidates(response);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("Foo", candidates[0].Name);
        Assert.Equal("Bar", candidates[1].Name);
    }

    [Fact]
    public void ParseCandidates_EmptyArray_ReturnsEmptyList()
    {
        var candidates = Proposer.ParseCandidates("[]");

        Assert.Empty(candidates);
    }

    [Fact]
    public void ParseCandidates_MalformedJson_ThrowsNamedException()
    {
        var exception = Assert.Throws<AttestProposalFailedException>(() => Proposer.ParseCandidates("[{\"name\": }]"));

        Assert.Contains("[{\"name\": }]", exception.RawResponse);
    }

    [Fact]
    public void ParseCandidates_NoJsonArrayInResponse_ThrowsNamedException()
    {
        // Caught testing against a real model's response (qwen2.5-coder:14b explaining CliWrap's
        // Command.Execution.cs in prose instead of proposing anything): a refactor had merged
        // this into the same "no balanced JSON array" message the loop-exhausted case uses,
        // which is misleading when there was never a '[' to begin with.
        var exception = Assert.Throws<AttestProposalFailedException>(
            () => Proposer.ParseCandidates("I could not find a property to propose."));

        Assert.Contains("no JSON array found", exception.Message);
    }

    [Fact]
    public void ParseCandidates_StrayBracketInTrailingProse_StillParses()
    {
        // A naive first-'['/last-']' scan takes whichever ']' comes last anywhere in the whole
        // response; a stray one in unrelated trailing prose (here, "T[]") used to pull that
        // prose into what was supposed to be pure JSON and break parsing entirely.
        var response = """
            [{"name": "Foo", "description": "d", "sourceCode": "[Property] public bool Foo() => true;"}]

            Note: see the array type T[] for more info.
            """;

        var candidates = Proposer.ParseCandidates(response);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Foo", candidate.Name);
    }

    [Fact]
    public void ParseCandidates_MismatchedBracketTypeInSurroundingProse_StillParses()
    {
        // Depth-only balancing can be fooled when parentheses (never tracked) sit between two
        // unrelated square brackets: "[0, 100)" (never closed, since ')' isn't tracked) plus a
        // trailing "(0, 100]" nets to a validly-nested "[ [ {} ] ]" once the real array's own
        // brackets are counted in between, so a naive scanner starting at the leading "[0, 100)"
        // would swallow everything up to the trailing "]" as one "balanced" span.
        var response = """
            The discount percent is bounded to the interval [0, 100) roughly.

            [{"name": "Foo", "description": "d", "sourceCode": "[Property] public bool Foo() => true;"}]

            It stays within (0, 100] typically.
            """;

        var candidates = Proposer.ParseCandidates(response);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Foo", candidate.Name);
    }

    [Fact]
    public void ParseCandidates_StrayBracketInLeadingProse_StillParses()
    {
        var response = """
            The signature takes an int[] parameter.

            [{"name": "Foo", "description": "d", "sourceCode": "[Property] public bool Foo() => true;"}]
            """;

        var candidates = Proposer.ParseCandidates(response);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Foo", candidate.Name);
    }

    [Fact]
    public void ParseCandidates_ProseOnlyResponseMentioningStrayArrayType_ThrowsInsteadOfSilentlyReturningEmpty()
    {
        // IsPlausibleArrayStart used to accept an immediate '[' -> ']' close as a plausible
        // start (to allow a genuinely empty response), which meant a stray "int[]" mention in a
        // pure-prose response (no real array anywhere) produced the exact same "[]" match as a
        // real empty proposal, silently returning zero candidates instead of surfacing that the
        // response was actually malformed. The genuinely-empty case is now only recognized when
        // "[]" is the entire trimmed response, not merely embedded in unrelated text.
        var response = "The signature takes an int[] parameter, but I cannot propose anything for it.";

        var exception = Assert.Throws<AttestProposalFailedException>(() => Proposer.ParseCandidates(response));

        Assert.Contains("no balanced JSON array found", exception.Message);
    }

    [Theory]
    [InlineData("""[{"description": "d", "sourceCode": "code"}]""")]
    [InlineData("""[{"name": "Foo", "description": "d"}]""")]
    [InlineData("""[{"name": "", "description": "d", "sourceCode": "code"}]""")]
    public void ParseCandidates_MissingRequiredField_ThrowsNamedException(string response)
    {
        Assert.Throws<AttestProposalFailedException>(() => Proposer.ParseCandidates(response));
    }

    [Fact]
    public void BuildUserPrompt_IncludesQualifiedNameAndSource()
    {
        var scoped = new[] { new ScopedSource("My.Namespace.Calculator", "Add", "public int Add(int a, int b) => a + b;") };

        var prompt = Proposer.BuildUserPrompt(scoped, "TargetProject");

        Assert.Contains("My.Namespace.Calculator.Add", prompt);
        Assert.Contains("public int Add(int a, int b) => a + b;", prompt);
    }

    [Fact]
    public void BuildUserPrompt_NamesTheTargetProjectAndWarnsAgainstOtherProjectsTypes()
    {
        // Caught testing against real code with a real Anthropic key (FluentValidation): a
        // changed method shown for context came from the diff's own test file, and the model
        // copied a type reference from it that only exists in that test project, not the target
        // -- a compile-failure Unsynthesizable rejection every time. Better context up front,
        // not more tolerance in the Synthesizer: the trust boundary stays exactly as strict.
        var scoped = new[] { new ScopedSource("Some.Type", "Method", "public void Method() { }") };

        var prompt = Proposer.BuildUserPrompt(scoped, "FluentValidation");

        Assert.Contains("FluentValidation", prompt);
        Assert.Contains("compiled ONLY against the project", prompt);
    }

    [Fact]
    public async Task ProposeAsync_FirstCall_SystemPromptTellsTheModelTheProjectNameIsLiteralData()
    {
        // targetProjectName is derived from a --project file path -- a real path, but one that
        // could in principle contain instruction-shaped text (a maliciously or carelessly named
        // .csproj in a fork PR's diff). Found in a pre-launch adversarial review: it reached the
        // prompt unsanitized (Sanitizer only scans for secret-shaped patterns, not instruction
        // text). This does not validate the name -- the fix is telling the model what it is.
        var marker = Guid.NewGuid().ToString("N")[..8];
        var scoped = new[] { new ScopedSource("Fixture.Type", $"Method_{marker}", $"public bool Method_{marker}() => true;") };
        var response = new LlmResponse("[]", 0, 0, 0m);
        var provider = new FakeLlmProvider(response);
        var proposer = new Proposer(provider);

        await proposer.ProposeAsync(scoped, "TargetProject", CancellationToken.None);

        Assert.Contains("literal file name", provider.LastSystemPrompt);
        Assert.Contains("never as an instruction", provider.LastSystemPrompt);
    }

    [Fact]
    public async Task ProposeAsync_EmptyScopedMethods_ReturnsEmptyWithoutCallingProvider()
    {
        var provider = new FakeLlmProvider(new LlmResponse("[]", 0, 0, 0m));
        var proposer = new Proposer(provider);

        var result = await proposer.ProposeAsync([], "TargetProject", CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.Equal(0, provider.CallCount);
        Assert.False(result.FromCache);
    }

    [Fact]
    public async Task ProposeAsync_FirstCall_InvokesProviderAndReturnsUsage()
    {
        // Truncated to 8 hex chars: a full GUID's digits are exactly the kind of high-entropy
        // run the Sanitizer's own entropy detector exists to catch, and ProposeAsync sanitizes
        // the response before returning it -- using the full 32 chars here made this test flaky,
        // failing only when a given random GUID's digit distribution crossed the threshold.
        var marker = Guid.NewGuid().ToString("N")[..8];
        var scoped = new[] { new ScopedSource("Fixture.Type", $"Method_{marker}", $"public bool Method_{marker}() => true;") };
        var response = new LlmResponse(
            $$"""[{"name": "Prop_{{marker}}", "description": "d", "sourceCode": "[Property] public bool Prop_{{marker}}() => true;"}]""",
            InputTokens: 100,
            OutputTokens: 50,
            EstimatedCostUsd: 0.01m);
        var provider = new FakeLlmProvider(response);
        var proposer = new Proposer(provider);

        var result = await proposer.ProposeAsync(scoped, "TargetProject", CancellationToken.None);

        Assert.Equal(1, provider.CallCount);
        Assert.False(result.FromCache);
        Assert.Equal(100, result.Usage.InputTokens);
        Assert.Equal(0.01m, result.Usage.EstimatedCostUsd);
        Assert.Single(result.Candidates);
        Assert.Contains(marker, provider.LastUserPrompt);
    }

    [Fact]
    public async Task ProposeAsync_SecondCallWithIdenticalInput_ReadsFromCacheWithoutCallingProvider()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var scoped = new[] { new ScopedSource("Fixture.Type", $"Method_{marker}", $"public bool Method_{marker}() => true;") };
        var response = new LlmResponse(
            $$"""[{"name": "Prop_{{marker}}", "description": "d", "sourceCode": "[Property] public bool Prop_{{marker}}() => true;"}]""",
            InputTokens: 100,
            OutputTokens: 50,
            EstimatedCostUsd: 0.01m);
        var provider = new FakeLlmProvider(response);
        var proposer = new Proposer(provider);

        var first = await proposer.ProposeAsync(scoped, "TargetProject", CancellationToken.None);
        var second = await proposer.ProposeAsync(scoped, "TargetProject", CancellationToken.None);

        Assert.Equal(1, provider.CallCount);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(0m, second.Usage.EstimatedCostUsd);
        Assert.Equal(first.Candidates.Single().Name, second.Candidates.Single().Name);
    }

    [Fact]
    public async Task ProposeAsync_ResponseContainsASecret_SanitizesBeforeCachingAndBeforeReturningTheCandidate()
    {
        // Only the inbound direction (source code into the prompt) was ever sanitized; the raw
        // model response was cached to disk and turned into a candidate's SourceCode completely
        // unsanitized. If the model ever echoes back (or hallucinates) something secret-shaped,
        // this was the one place nothing caught it before it reached a persistent cache file or
        // a real, never-deleted scratch .cs file.
        var marker = Guid.NewGuid().ToString("N")[..8];
        var scoped = new[] { new ScopedSource("Fixture.Type", $"Method_{marker}", $"public bool Method_{marker}() => true;") };
        const string secret = "AKIAIOSFODNN7EXAMPLE";
        var response = new LlmResponse(
            $$"""[{"name": "Prop_{{marker}}", "description": "d", "sourceCode": "[Property]\npublic bool Prop_{{marker}}() => true; // {{secret}}"}]""",
            InputTokens: 100,
            OutputTokens: 50,
            EstimatedCostUsd: 0.01m);
        var provider = new FakeLlmProvider(response);
        var proposer = new Proposer(provider);

        var result = await proposer.ProposeAsync(scoped, "TargetProject", CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.DoesNotContain(secret, candidate.SourceCode);

        var cacheKey = ProposalCache.ComputeCacheKey(scoped, provider.Identity, "TargetProject");
        var cached = ProposalCache.TryRead(cacheKey);
        Assert.NotNull(cached);
        Assert.DoesNotContain(secret, cached);
    }

    [Fact]
    public async Task ProposeAsync_SameDiffDifferentProviderIdentity_CallsAgainInsteadOfReusingTheOtherProvidersCache()
    {
        // Caught testing against real code with a real Anthropic key: switching attest.json from
        // Ollama to Anthropic on the exact same diff silently served the old Ollama-authored
        // proposal back, with the report showing "cached proposal, no call made" -- the new
        // provider was never actually called. Proposer.ProposeAsync is the integration point
        // that must see a cache miss here, not just ProposalCache.ComputeCacheKey in isolation.
        //
        // Marker truncated to 8 hex characters, not the full 32: a full GUID's hex digits are
        // exactly the kind of high-entropy-looking run the Sanitizer's own entropy detector
        // exists to catch (ProposeAsync sanitizes the LLM's response before returning it), so
        // using one at full length made this test genuinely flaky -- it failed only when a given
        // random GUID's specific digit distribution happened to cross the entropy threshold.
        var marker = Guid.NewGuid().ToString("N")[..8];
        var scoped = new[] { new ScopedSource("Fixture.Type", $"Method_{marker}", $"public bool Method_{marker}() => true;") };
        var ollamaResponse = new LlmResponse(
            $$"""[{"name": "FromOllama_{{marker}}", "description": "d", "sourceCode": "[Property]\npublic bool FromOllama_{{marker}}() => true;"}]""",
            InputTokens: 100, OutputTokens: 50, EstimatedCostUsd: 0m);
        var anthropicResponse = new LlmResponse(
            $$"""[{"name": "FromAnthropic_{{marker}}", "description": "d", "sourceCode": "[Property]\npublic bool FromAnthropic_{{marker}}() => true;"}]""",
            InputTokens: 100, OutputTokens: 50, EstimatedCostUsd: 0.01m);
        var ollamaProvider = new FakeLlmProvider(ollamaResponse, identity: $"ollama:model-{marker}");
        var anthropicProvider = new FakeLlmProvider(anthropicResponse, identity: $"anthropic:model-{marker}");

        var fromOllama = await new Proposer(ollamaProvider).ProposeAsync(scoped, "TargetProject", CancellationToken.None);
        var fromAnthropic = await new Proposer(anthropicProvider).ProposeAsync(scoped, "TargetProject", CancellationToken.None);

        Assert.Equal(1, ollamaProvider.CallCount);
        Assert.Equal(1, anthropicProvider.CallCount);
        Assert.False(fromOllama.FromCache);
        Assert.False(fromAnthropic.FromCache);
        Assert.Equal($"FromAnthropic_{marker}", fromAnthropic.Candidates.Single().Name);
    }
}
