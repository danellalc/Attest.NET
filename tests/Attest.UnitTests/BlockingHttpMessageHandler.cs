namespace Attest.UnitTests;

/// <summary>
/// Never completes on its own; the only way SendAsync returns is via the caller's own
/// CancellationToken firing, exactly like a real request that stays in flight when the user
/// hits Ctrl+C. Proves cancellation propagates as OperationCanceledException, not through
/// whatever the provider's own HttpRequestException/TaskCanceledException guard does.
/// </summary>
internal sealed class BlockingHttpMessageHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable: Task.Delay with an infinite timeout only ever completes via cancellation.");
    }
}
