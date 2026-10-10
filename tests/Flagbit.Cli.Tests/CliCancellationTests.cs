using System.Net;

namespace Flagbit.Cli.Tests;

public sealed class CliCancellationTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("create")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("delete")]
    [InlineData("evaluate")]
    public async Task EveryCommandForwardsCancellationAndStopsBeforeTransportWhenAlreadyCancelled(string command)
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cli = new CliTestSession(async (_, token) =>
        {
            started.SetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("The request should be cancelled.");
        });
        string[] args = command == "list" ? [command] : [command, "flag"];

        var pending = cli.Application.RunAsync(args, cancellation.Token);
        var receivedToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        Assert.Equal(130, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(receivedToken.IsCancellationRequested);
        Assert.Equal("Command cancelled.", cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
        Assert.Equal(130, await cli.Application.RunAsync(args, cancellation.Token));
        Assert.Equal(1, cli.Handler.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpTimeoutIsControlledDuringSendingAndBodyReading(bool reading)
    {
        using var cli = new CliTestSession(async (_, token) =>
        {
            if (reading)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new BlockingContent() };
            }

            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("The request should time out.");
        });
        cli.HttpClient.Timeout = TimeSpan.FromMilliseconds(100);

        Assert.Equal(1, await cli.Application.RunAsync(["get", "flag"]).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("The Flagbit API request timed out.", cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    public async Task CancellationInterruptsSuccessfulAndErrorResponseBodyReading(int status)
    {
        using var cancellation = new CancellationTokenSource();
        var content = new BlockingContent();
        using var cli = new CliTestSession((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content }));

        var pending = cli.Application.RunAsync(["get", "flag"], cancellation.Token);
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        Assert.Equal(130, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Command cancelled.", cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
    }

    private sealed class BlockingContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
