using System.Net;
using System.Text;
using System.Text.Json;

namespace Flagbit.Sdk.Tests;

public sealed class FlagbitClientFailureTests
{
    [Fact]
    public void ConstructorRejectsNullClient()
    {
        Assert.Throws<ArgumentNullException>(() => new FlagbitClient(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ftp://flagbit.example/")]
    [InlineData("file:///flagbit/")]
    [InlineData("mailto:flagbit@example.com")]
    public void ConstructorRejectsMissingOrNonHttpBaseAddress(string? address)
    {
        using var httpClient = new HttpClient { BaseAddress = address is null ? null : new Uri(address) };
        var exception = Assert.Throws<ArgumentException>(() => new FlagbitClient(httpClient));
        Assert.Equal("httpClient", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n")]
    public async Task AllMethodsRejectMissingFlagKeysBeforeSending(string? key)
    {
        using var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No request should be sent."));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FlagbitClient(httpClient);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.IsEnabledAsync(key!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.EvaluateAsync(key!, new FeatureFlagEvaluationContext()));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.GetVariationAsync(key!, "on", "off"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.GetContextualVariationAsync(key!, "on", "off", new FeatureFlagEvaluationContext()));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ContextualMethodsRejectNullContextBeforeSending()
    {
        using var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No request should be sent."));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FlagbitClient(httpClient);

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.EvaluateAsync("checkout", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetContextualVariationAsync("checkout", "on", "off", null!));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidResponsesNeverBecomeFalseEvaluations(bool contextual)
    {
        string[] invalidBodies =
        [
            "", " \t", "null", "{", "[]", "true", "42", "\"text\"", "{}", "{\"key\":\"checkout\"}",
            "{\"isEnabled\":true}", "{\"isEnabled\":false}", "{\"key\":null,\"isEnabled\":true}",
            "{\"key\":\"\",\"isEnabled\":false}", "{\"key\":\" \\t\",\"isEnabled\":true}",
            "{\"key\":42,\"isEnabled\":true}", "{\"key\":\"checkout\",\"isEnabled\":null}",
            "{\"key\":\"checkout\",\"isEnabled\":\"false\"}", "{\"key\":\"checkout\",\"isEnabled\":0}",
            "{\"key\":\"checkout\",\"isEnabled\":{}}", "{\"key\":\"checkout\",\"isEnabled\":[]}"
        ];
        foreach (var body in invalidBodies)
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            var client = new FlagbitClient(httpClient);

            await Assert.ThrowsAnyAsync<JsonException>(() => EvaluateAsync(client, contextual));
            Assert.Equal(1, handler.CallCount);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ResponseCompatibilityIncludesPropertyCasingAndUnknownFields(bool contextual, bool isEnabled)
    {
        var body = "{\"KEY\":\"Checkout\",\"ISENABLED\":" + (isEnabled ? "true" : "false") + ",\"futureField\":{\"value\":42}}";
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        Assert.Equal(isEnabled, await EvaluateAsync(new FlagbitClient(httpClient), contextual));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpFailuresKeepTheirStatusAndAreNotRetried(bool contextual)
    {
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable })
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("not an evaluation response") });
            using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => EvaluateAsync(new FlagbitClient(httpClient), contextual));

            Assert.Equal(status, exception.StatusCode);
            Assert.Equal(1, handler.CallCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkFailuresRemainDistinctFromHttpFailures(bool contextual)
    {
        var failure = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused.");
        using var handler = new StubHttpMessageHandler(_ => throw failure);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => EvaluateAsync(new FlagbitClient(httpClient), contextual));

        Assert.Same(failure, exception);
        Assert.Null(exception.StatusCode);
        Assert.Equal(HttpRequestError.ConnectionError, exception.HttpRequestError);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VariationsDoNotMaskInvalidResponsesOrHttpFailures(bool contextual)
    {
        foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable })
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });
            using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            var client = new FlagbitClient(httpClient);
            Func<Task<string>> evaluate = () => contextual
                ? client.GetContextualVariationAsync("checkout", "on", "off", new FeatureFlagEvaluationContext())
                : client.GetVariationAsync("checkout", "on", "off");

            if (status == HttpStatusCode.OK)
            {
                await Assert.ThrowsAnyAsync<JsonException>(evaluate);
            }
            else
            {
                await Assert.ThrowsAsync<HttpRequestException>(evaluate);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponsesAreDisposedOnSuccessInvalidJsonAndHttpFailure(bool contextual)
    {
        foreach (var (status, body) in new[] { (HttpStatusCode.OK, "{\"key\":\"checkout\",\"isEnabled\":false}"), (HttpStatusCode.OK, "{}"), (HttpStatusCode.ServiceUnavailable, "") })
        {
            var content = new TrackingContent(body);
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(status) { Content = content });
            using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            var exception = await Record.ExceptionAsync(() => EvaluateAsync(new FlagbitClient(httpClient), contextual));

            if (status == HttpStatusCode.ServiceUnavailable)
            {
                Assert.IsType<HttpRequestException>(exception);
            }
            else if (body == "{}")
            {
                Assert.IsType<JsonException>(exception);
            }
            else
            {
                Assert.Null(exception);
            }

            Assert.True(content.IsDisposed);
            Assert.False(handler.IsDisposed);
        }
    }

    private static Task<bool> EvaluateAsync(FlagbitClient client, bool contextual)
    {
        return contextual ? client.EvaluateAsync("checkout", new FeatureFlagEvaluationContext()) : client.IsEnabledAsync("checkout");
    }

    private sealed class TrackingContent(string body) : StringContent(body)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
