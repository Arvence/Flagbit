using System.Net;

namespace Flagbit.Sdk.Tests;

public sealed class FlagbitClientCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCancelledCallsDoNotSendRequests(bool contextual)
    {
        using var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No request should be sent."));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FlagbitClient(httpClient);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EvaluateAsync(client, contextual, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => contextual
            ? client.GetContextualVariationAsync("checkout", "on", "off", new FeatureFlagEvaluationContext(), cancellation.Token)
            : client.GetVariationAsync("checkout", "on", "off", null, cancellation.Token));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationInterruptsSendingAndReading(bool contextual, bool reading)
    {
        var blocked = new BlockedOperation();
        var content = new BlockingContent(blocked);
        using var handler = CreateHandler(blocked, content, reading);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/"), Timeout = Timeout.InfiniteTimeSpan };
        var client = new FlagbitClient(httpClient);
        using var cancellation = new CancellationTokenSource();
        var pending = EvaluateAsync(client, contextual, cancellation.Token);
        try
        {
            await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(blocked.Token.IsCancellationRequested);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(1, handler.CallCount);
            if (reading)
            {
                Assert.True(content.IsDisposed);
            }
        }
        finally
        {
            cancellation.Cancel();
            blocked.Release.TrySetResult();
            content.Dispose();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HttpClientTimeoutCoversSendingAndReadingWithoutCancellingCallerToken(bool contextual, bool reading)
    {
        var blocked = new BlockedOperation();
        var content = new BlockingContent(blocked);
        using var handler = CreateHandler(blocked, content, reading);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/"), Timeout = TimeSpan.FromMilliseconds(200) };
        var client = new FlagbitClient(httpClient);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var pending = EvaluateAsync(client, contextual, cancellation.Token);
            await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.IsType<TimeoutException>(exception.InnerException);
            Assert.False(cancellation.IsCancellationRequested);
            Assert.True(blocked.Token.IsCancellationRequested);
            Assert.Equal(1, handler.CallCount);
            if (reading)
            {
                Assert.True(content.IsDisposed);
            }
        }
        finally
        {
            blocked.Release.TrySetResult();
            content.Dispose();
        }
    }

    private static StubHttpMessageHandler CreateHandler(BlockedOperation blocked, HttpContent content, bool reading)
    {
        return new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            if (!reading)
            {
                await blocked.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
    }

    private static Task<bool> EvaluateAsync(FlagbitClient client, bool contextual, CancellationToken cancellationToken)
    {
        return contextual ? client.EvaluateAsync("checkout", new FeatureFlagEvaluationContext(), cancellationToken) : client.IsEnabledAsync("checkout", cancellationToken: cancellationToken);
    }

    private sealed class BlockedOperation
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingContent(BlockedOperation blocked) : HttpContent
    {
        public bool IsDisposed { get; private set; }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => blocked.WaitAsync(CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => blocked.WaitAsync(cancellationToken);

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
