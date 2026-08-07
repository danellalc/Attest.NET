using System.Net;
using System.Text;
using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class AnthropicProviderTests
{
    private const string SuccessBody = """
        {
          "content": [{"type": "text", "text": "[{\"name\": \"Foo\", \"description\": \"d\", \"sourceCode\": \"code\"}]"}],
          "usage": {"input_tokens": 100, "output_tokens": 50}
        }
        """;

    [Fact]
    public async Task CompleteAsync_SendsCorrectUrlAndHeaders()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json"),
        });
        var provider = new AnthropicProvider(new HttpClient(handler), "sk-test-key", "claude-sonnet-5");

        await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Equal("https://api.anthropic.com/v1/messages", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("sk-test-key", handler.LastRequest.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.LastRequest.Headers.GetValues("anthropic-version").Single());
        Assert.Contains("\"model\":\"claude-sonnet-5\"", handler.LastRequestBody);
        Assert.Contains("\"system\":\"system\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_ParsesContentAndUsage()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json"),
        });
        var provider = new AnthropicProvider(new HttpClient(handler), "sk-test-key", "claude-sonnet-5");

        var response = await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Contains("\"name\": \"Foo\"", response.Content);
        Assert.Equal(100, response.InputTokens);
        Assert.Equal(50, response.OutputTokens);
    }

    [Fact]
    public async Task CompleteAsync_ComputesCostFromKnownPricing()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json"),
        });
        var provider = new AnthropicProvider(new HttpClient(handler), "sk-test-key", "claude-sonnet-5");

        var response = await provider.CompleteAsync("system", "user", CancellationToken.None);

        // 100 input tokens @ $3.00/M + 50 output tokens @ $15.00/M
        Assert.Equal(0.00105m, response.EstimatedCostUsd);
    }

    [Fact]
    public async Task CompleteAsync_UnknownModel_ThrowsWithoutCallingTheApi()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var provider = new AnthropicProvider(new HttpClient(handler), "sk-test-key", "claude-unknown-model");

        await Assert.ThrowsAsync<AttestUnknownModelPricingException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));

        Assert.Null(handler.LastRequest);
    }
}
