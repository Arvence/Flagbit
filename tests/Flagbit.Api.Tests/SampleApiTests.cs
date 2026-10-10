extern alias FlagbitSample;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flagbit.Sdk;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SampleProgram = FlagbitSample::Program;

namespace Flagbit.Api.Tests;

public sealed class SampleApiTests
{
    [Theory]
    [InlineData(true, "modern")]
    [InlineData(false, "classic")]
    public async Task CheckoutForwardsContextAndUsesTheSdkResult(bool isEnabled, string checkout)
    {
        using var application = CreateApplication(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/prefix/api/flags/sample-new-checkout/evaluate", request.RequestUri!.PathAndQuery);
            Assert.Equal("sample-evaluation-key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            var context = await request.Content!.ReadFromJsonAsync<FeatureFlagEvaluationContext>(cancellationToken);
            Assert.Equal("demo ?&=", context!.UserId);
            Assert.Equal(" Production ", context.Environment);
            Assert.Equal("enterprise=a", Assert.Single(context.Attributes!).Value);
            Assert.True(context.Attributes!.ContainsKey("plan"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { key = "sample-new-checkout", isEnabled }) };
        });
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/checkout?userId=demo%20%3F%26%3D&environment=%20Production%20&plan=enterprise%3Da");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(isEnabled, body.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(checkout, body.GetProperty("checkout").GetString());
    }

    [Fact]
    public async Task CheckoutWithoutQueryParametersSendsAnEmptyContext()
    {
        using var application = CreateApplication(async (request, cancellationToken) =>
        {
            var context = await request.Content!.ReadFromJsonAsync<FeatureFlagEvaluationContext>(cancellationToken);
            Assert.NotNull(context);
            Assert.Null(context.UserId);
            Assert.Null(context.Environment);
            Assert.Null(context.Attributes);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { key = "sample-new-checkout", isEnabled = false }) };
        });
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/checkout");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("classic", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checkout").GetString());
    }

    [Theory]
    [InlineData("connection", 502)]
    [InlineData("revoked", 502)]
    [InlineData("forbidden", 502)]
    [InlineData("unavailable", 502)]
    [InlineData("malformed", 502)]
    [InlineData("timeout", 504)]
    public async Task DependencyFailuresAreRequestErrorsWithoutCheckoutFallback(string failure, int expectedStatus)
    {
        using var application = CreateApplication((_, _) => failure switch
        {
            "connection" => throw new HttpRequestException("private-secret"),
            "timeout" => throw new TaskCanceledException("private-secret"),
            "malformed" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { key = "sample-new-checkout" }) }),
            _ => Task.FromResult(new HttpResponseMessage(failure == "revoked" ? HttpStatusCode.Unauthorized : failure == "forbidden" ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable))
        });
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/checkout");
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("classic", body);
        Assert.DoesNotContain("isEnabled", body);
        Assert.DoesNotContain("private-secret", body);
    }

    [Fact]
    public async Task CallerCancellationReachesTheSdkRequest()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var application = CreateApplication(async (_, token) =>
        {
            started.SetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("The request should be cancelled.");
        });
        using var client = application.CreateClient();
        using var cancellation = new CancellationTokenSource();

        var pending = client.GetAsync("/checkout", cancellation.Token);
        var receivedToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(receivedToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData("ftp://example.com", "sample-evaluation-key", "Flagbit__ApiUrl")]
    [InlineData("http://user:secret@example.com", "sample-evaluation-key", "Flagbit__ApiUrl")]
    [InlineData("http://localhost/?secret=value", "sample-evaluation-key", "Flagbit__ApiUrl")]
    [InlineData("http://localhost", "", "Flagbit__ApiKey")]
    [InlineData("http://localhost", "secret\r\nheader", "Flagbit__ApiKey")]
    public void InvalidConfigurationFailsBeforeServingRequests(string url, string key, string setting)
    {
        using var application = CreateApplication((_, _) => throw new InvalidOperationException("No request expected."), url, key);

        var exception = Assert.Throws<InvalidOperationException>(() => application.CreateClient());

        Assert.Contains(setting, exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
    }

    private static WebApplicationFactory<SampleProgram> CreateApplication(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, string url = "https://flagbit.example/prefix", string key = "sample-evaluation-key")
    {
        return new WebApplicationFactory<SampleProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("Flagbit:ApiUrl", url);
            builder.UseSetting("Flagbit:ApiKey", key);
            builder.ConfigureTestServices(services => services.AddHttpClient<FlagbitClient>().ConfigurePrimaryHttpMessageHandler(() => new SampleHttpHandler(send)));
        });
    }

    private sealed class SampleHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }
}
