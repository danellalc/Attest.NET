using System.Net;
using System.Text;
using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class OpenAiCompatibleProviderTests
{
    // Wrapped in {"properties": [...]}, not a bare array: "json_object"/"json_schema" response
    // formats constrain several real OpenAI-compatible backends (confirmed directly against
    // Ollama's own OpenAI-compatible endpoint) to a top-level JSON *object*, not an array -- a
    // bare array is what Object/Schema mode actually receives back from a real server today.
    private const string SuccessBody = """
        {
          "choices": [{"message": {"role": "assistant", "content": "{\"properties\": [{\"name\": \"Foo\", \"description\": \"d\", \"sourceCode\": \"code\"}]}"}}],
          "usage": {"prompt_tokens": 100, "completion_tokens": 50}
        }
        """;

    private const string BareArrayBody = """
        {
          "choices": [{"message": {"role": "assistant", "content": "[{\"name\": \"Foo\", \"description\": \"d\", \"sourceCode\": \"code\"}]"}}],
          "usage": {"prompt_tokens": 100, "completion_tokens": 50}
        }
        """;

    private static HttpResponseMessage SuccessResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage BareArrayResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(BareArrayBody, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task CompleteAsync_SendsCorrectUrlAndAuthHeader()
    {
        var handler = new FakeHttpMessageHandler(_ => SuccessResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object, pricing: null);

        await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test-key", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Contains("\"model\":\"gpt-4o-mini\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_TrailingSlashOnBaseUrl_DoesNotDoubleUp()
    {
        var handler = new FakeHttpMessageHandler(_ => SuccessResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1/", JsonResponseMode.Object, pricing: null);

        await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task CompleteAsync_ParsesContentAndUsage()
    {
        var handler = new FakeHttpMessageHandler(_ => SuccessResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object, pricing: null);

        var response = await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Contains("\"name\": \"Foo\"", response.Content);
        Assert.Equal(100, response.InputTokens);
        Assert.Equal(50, response.OutputTokens);
    }

    [Theory]
    [InlineData(JsonResponseMode.Schema, "\"response_format\":{\"type\":\"json_schema\"")]
    [InlineData(JsonResponseMode.Object, "\"response_format\":{\"type\":\"json_object\"}")]
    public async Task CompleteAsync_JsonMode_SendsExpectedResponseFormat(JsonResponseMode mode, string expectedFragment)
    {
        var handler = new FakeHttpMessageHandler(_ => SuccessResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", mode, pricing: null);

        await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Contains(expectedFragment, handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_JsonModeNone_OmitsResponseFormatEntirely()
    {
        // Some OpenAI-compatible backends reject an unrecognized response_format parameter
        // outright; None must not send the key at all, not send it as an explicit null.
        var handler = new FakeHttpMessageHandler(_ => BareArrayResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.None, pricing: null);

        await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.DoesNotContain("response_format", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_JsonModeNone_PassesResponseThroughWithoutUnwrapping()
    {
        // None mode relies on the shared prompt's own instruction (a bare array), with no
        // response_format hint at all -- there is nothing to unwrap here, unlike Schema/Object.
        var handler = new FakeHttpMessageHandler(_ => BareArrayResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.None, pricing: null);

        var response = await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Contains("\"name\": \"Foo\"", response.Content);
    }

    [Theory]
    [InlineData(JsonResponseMode.Schema)]
    [InlineData(JsonResponseMode.Object)]
    public async Task CompleteAsync_SchemaOrObjectMode_AppendsWrapperInstructionToSystemPrompt(JsonResponseMode mode)
    {
        var handler = new FakeHttpMessageHandler(_ => SuccessResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", mode, pricing: null);

        await provider.CompleteAsync("system prompt text", "user", CancellationToken.None);

        Assert.Contains("Respond with a single JSON object", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_JsonModeNone_DoesNotAppendWrapperInstruction()
    {
        var handler = new FakeHttpMessageHandler(_ => BareArrayResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.None, pricing: null);

        await provider.CompleteAsync("system prompt text", "user", CancellationToken.None);

        Assert.DoesNotContain("properties", handler.LastRequestBody);
    }

    [Theory]
    [InlineData(JsonResponseMode.Schema)]
    [InlineData(JsonResponseMode.Object)]
    public async Task CompleteAsync_BareArrayUnderConstrainedMode_ThrowsInsteadOfSilentlyMisparsing(JsonResponseMode mode)
    {
        // The exact failure caught live against a real server: asked for a bare array under
        // "json_object" mode, Ollama's own OpenAI-compatible endpoint returned a single bare
        // object with no "properties" wrapper and no array at all -- because that mode
        // constrains the top level to an object on that backend, contradicting the prompt's own
        // "respond with a bare array" instruction. UnwrapPropertiesArray must fail loudly on a
        // response that isn't wrapped as instructed, not misinterpret it as an empty proposal.
        var handler = new FakeHttpMessageHandler(_ => BareArrayResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", mode, pricing: null);

        var exception = await Assert.ThrowsAsync<AttestProposalFailedException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));

        Assert.Contains("properties", exception.Message);
    }

    [Fact]
    public async Task CompleteAsync_ObjectModeMissingPropertiesKey_ThrowsNamedException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices": [{"message": {"role": "assistant", "content": "{\"somethingElse\": []}"}}], "usage": {"prompt_tokens": 1, "completion_tokens": 1}}""",
                Encoding.UTF8, "application/json"),
        });
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object, pricing: null);

        var exception = await Assert.ThrowsAsync<AttestProposalFailedException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));

        Assert.Contains("properties", exception.Message);
    }

    [Fact]
    public async Task CompleteAsync_NoPricingConfigured_CostIsNullNotZero()
    {
        // An arbitrary endpoint's cost cannot be assumed to be zero just because Attest was not
        // told its price -- only Ollama gets to report a real $0, since it genuinely has no API
        // to bill against. A generic backend with unconfigured pricing might well be a paid one.
        var handler = new FakeHttpMessageHandler(_ => SuccessResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object, pricing: null);

        var response = await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Null(response.EstimatedCostUsd);
    }

    [Fact]
    public async Task CompleteAsync_PricingConfigured_ComputesRealCost()
    {
        var handler = new FakeHttpMessageHandler(_ => SuccessResponse());
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object,
            pricing: (InputPerMillion: 0.15m, OutputPerMillion: 0.60m));

        var response = await provider.CompleteAsync("system", "user", CancellationToken.None);

        // 100 input tokens @ $0.15/M + 50 output tokens @ $0.60/M
        Assert.Equal(0.000045m, response.EstimatedCostUsd);
    }

    [Fact]
    public void Identity_IncludesBaseUrlAndModel_NotJustModel()
    {
        // Caught fixing the proposal cache: two different backends can legitimately share a
        // model name (a paid API and a self-hosted server both calling it "llama-3.1-70b"), and
        // the cache must not treat their answers as interchangeable.
        var providerA = new OpenAiCompatibleProvider(
            new HttpClient(), "key", "llama-3.1-70b", "https://api.example.com/v1", JsonResponseMode.Object, pricing: null);
        var providerB = new OpenAiCompatibleProvider(
            new HttpClient(), "key", "llama-3.1-70b", "http://localhost:8000/v1", JsonResponseMode.Object, pricing: null);

        Assert.NotEqual(providerA.Identity, providerB.Identity);
    }

    [Fact]
    public async Task CompleteAsync_NonSuccessStatus_ThrowsNamedExceptionWithBody()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error": {"message": "invalid api key"}}""", Encoding.UTF8, "application/json"),
        });
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-bad-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object, pricing: null);

        var exception = await Assert.ThrowsAsync<AttestProposalFailedException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));

        Assert.Contains("401", exception.Message);
        Assert.Contains("invalid api key", exception.RawResponse);
    }

    [Fact]
    public async Task CompleteAsync_MalformedJsonBody_ThrowsNamedException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        });
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object, pricing: null);

        await Assert.ThrowsAsync<AttestProposalFailedException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_NoChoicesInResponse_ThrowsNamedException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices": [], "usage": {"prompt_tokens": 1, "completion_tokens": 1}}""", Encoding.UTF8, "application/json"),
        });
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object, pricing: null);

        await Assert.ThrowsAsync<AttestProposalFailedException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_NetworkFailure_ThrowsNamedExceptionNotRaw()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(handler), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object, pricing: null);

        await Assert.ThrowsAsync<AttestProposalFailedException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_CallerCancels_ThrowsOperationCanceledNotAttestProposalFailed()
    {
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(new BlockingHttpMessageHandler()), "sk-test-key", "gpt-4o-mini", "https://api.openai.com/v1", JsonResponseMode.Object, pricing: null);
        using var cts = new CancellationTokenSource();

        var completeTask = provider.CompleteAsync("system", "user", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => completeTask);
    }
}
