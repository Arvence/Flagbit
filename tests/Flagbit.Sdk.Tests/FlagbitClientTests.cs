using System.Net;
using System.Net.Http.Json;

namespace Flagbit.Sdk.Tests;

public sealed class FlagbitClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsEnabledAsyncReturnsApiEvaluation(bool isEnabled)
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { key = "new-checkout", isEnabled })
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FlagbitClient(httpClient);

        var result = await client.IsEnabledAsync("new checkout", "user 123");

        Assert.Equal(isEnabled, result);
        Assert.Equal("/api/flags/new%20checkout/enabled?userId=user%20123", handler.RequestUri?.PathAndQuery);
    }

    [Theory]
    [InlineData(true, "modern")]
    [InlineData(false, "classic")]
    public async Task GetVariationAsyncReturnsVariationForEvaluatedState(bool isEnabled, string expected)
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { key = "new-checkout", isEnabled })
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FlagbitClient(httpClient);

        var variation = await client.GetVariationAsync("new-checkout", "modern", "classic");

        Assert.Equal(expected, variation);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(_responseFactory(request));
        }
    }
}
