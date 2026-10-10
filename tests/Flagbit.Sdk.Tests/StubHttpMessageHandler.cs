namespace Flagbit.Sdk.Tests;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
{
    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : this((request, _) => Task.FromResult(responseFactory(request)))
    {
    }

    public Uri? RequestUri { get; private set; }
    public HttpMethod? RequestMethod { get; private set; }
    public int CallCount { get; private set; }
    public bool IsDisposed { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri;
        RequestMethod = request.Method;
        CallCount++;
        return responseFactory(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
