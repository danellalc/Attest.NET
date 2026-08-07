using System.Net;
using System.Text;
using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class OllamaProviderTests
{
    private const string SuccessBody = """
        {
          "message": {"role": "assistant", "content": "[{\"name\": \"Foo\", \"description\": \"d\", \"sourceCode\": \"code\"}]"},
          "prompt_eval_count": 80,
          "eval_count": 30
        }
        """;

    [Fact]
    public async Task CompleteAsync_SendsCorrectPathAndModel()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaProvider(httpClient, "llama3");

        await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Equal("http://localhost:11434/api/chat", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("\"model\":\"llama3\"", handler.LastRequestBody);
        Assert.Contains("\"stream\":false", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_ParsesMessageContentAndTokenCounts()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaProvider(httpClient, "llama3");

        var response = await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Contains("\"name\": \"Foo\"", response.Content);
        Assert.Equal(80, response.InputTokens);
        Assert.Equal(30, response.OutputTokens);
    }

    [Fact]
    public async Task CompleteAsync_AlwaysReportsZeroCost()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaProvider(httpClient, "llama3");

        var response = await provider.CompleteAsync("system", "user", CancellationToken.None);

        Assert.Equal(0m, response.EstimatedCostUsd);
    }

    [Fact]
    public async Task CompleteAsync_NonSuccessStatus_ThrowsNamedExceptionWithBody()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("model not found", Encoding.UTF8, "text/plain"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaProvider(httpClient, "not-pulled-model");

        var exception = await Assert.ThrowsAsync<AttestProposalFailedException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));

        Assert.Contains("500", exception.Message);
        Assert.Contains("model not found", exception.RawResponse);
    }

    [Fact]
    public async Task CompleteAsync_NonJsonBodyOn200_ThrowsNamedExceptionInsteadOfRaw()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not ollama</html>", Encoding.UTF8, "text/html"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaProvider(httpClient, "llama3");

        await Assert.ThrowsAsync<AttestProposalFailedException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_NetworkFailure_ThrowsNamedExceptionNotRaw()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaProvider(httpClient, "llama3");

        await Assert.ThrowsAsync<AttestProposalFailedException>(
            () => provider.CompleteAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_CallerCancels_ThrowsOperationCanceledNotAttestProposalFailed()
    {
        var httpClient = new HttpClient(new BlockingHttpMessageHandler()) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaProvider(httpClient, "llama3");
        using var cts = new CancellationTokenSource();

        var completeTask = provider.CompleteAsync("system", "user", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => completeTask);
    }
}
