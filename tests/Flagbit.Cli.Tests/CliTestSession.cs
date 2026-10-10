extern alias FlagbitCli;

using System.Net;
using System.Text;
using CliApplication = FlagbitCli::Flagbit.Cli.CliApplication;
using FlagbitApiClient = FlagbitCli::Flagbit.Cli.Api.FlagbitApiClient;

namespace Flagbit.Cli.Tests;

internal sealed class CliTestSession : IDisposable
{
    public CliTestSession(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory, string? apiKey = null)
    {
        Handler = new StubHttpMessageHandler(responseFactory);
        HttpClient = new HttpClient(Handler) { BaseAddress = new Uri("https://flagbit.example/prefix/") };
        Application = new CliApplication(new FlagbitApiClient(HttpClient, apiKey), Output, Error);
    }

    public StubHttpMessageHandler Handler { get; }
    public HttpClient HttpClient { get; }
    public CliApplication Application { get; }
    public StringWriter Output { get; } = new();
    public StringWriter Error { get; } = new();

    public static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json")
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };
    }

    public void Dispose()
    {
        HttpClient.Dispose();
        Output.Dispose();
        Error.Dispose();
    }
}

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return responseFactory(request, cancellationToken);
    }
}
