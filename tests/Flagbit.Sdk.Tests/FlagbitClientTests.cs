using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
        Assert.Equal(HttpMethod.Get, handler.RequestMethod);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EvaluateAsyncSendsEscapedKeyAndCompleteContext(bool isEnabled)
    {
        const string key = "checkout /?#+%\u6771\u4eac";
        var context = new FeatureFlagEvaluationContext("user /?#+%\u00e9", " Production ", new Dictionary<string, string> { ["PLAN"] = "Enterprise", ["region"] = "eu-west" });
        using var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/prefix/status")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/prefix/api/flags/{Uri.EscapeDataString(key)}/evaluate", request.RequestUri!.PathAndQuery);
            Assert.Equal("evaluation-secret", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal(new[] { "attributes", "environment", "userId" }, body.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.Equal(context.UserId, body.RootElement.GetProperty("userId").GetString());
            Assert.Equal(context.Environment, body.RootElement.GetProperty("environment").GetString());
            Assert.Equal("Enterprise", body.RootElement.GetProperty("attributes").GetProperty("PLAN").GetString());
            Assert.Equal("eu-west", body.RootElement.GetProperty("attributes").GetProperty("region").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { key, isEnabled }) };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://flagbit.example/prefix/") };
        httpClient.DefaultRequestHeaders.Add("X-Api-Key", "evaluation-secret");
        var client = new FlagbitClient(httpClient);

        Assert.Equal(isEnabled, await client.EvaluateAsync(key, context));
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(" Production ", context.Environment);
        Assert.Equal("Enterprise", context.Attributes!["PLAN"]);
        Assert.Equal("evaluation-secret", Assert.Single(httpClient.DefaultRequestHeaders.GetValues("X-Api-Key")));
        Assert.Equal(new Uri("https://flagbit.example/prefix/"), httpClient.BaseAddress);
        Assert.False(handler.IsDisposed);
        using var subsequent = await httpClient.GetAsync("status");
        Assert.Equal(HttpStatusCode.OK, subsequent.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("user /?#+%\u00e9")]
    public async Task LegacyGetPreservesNullEmptyAndEscapedUserValues(string? userId)
    {
        const string key = "checkout /?#+%\u6771\u4eac";
        using var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Equal("evaluation-secret", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { key, isEnabled = true }) };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        httpClient.DefaultRequestHeaders.Add("X-Api-Key", "evaluation-secret");
        var client = new FlagbitClient(httpClient);

        Assert.True(await client.IsEnabledAsync(key, userId));
        var query = userId is null ? "" : $"?userId={Uri.EscapeDataString(userId)}";
        Assert.Equal($"/api/flags/{Uri.EscapeDataString(key)}/enabled{query}", handler.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task ExistingCallsWithNullUserIdRemainUnambiguous()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { key = "checkout", isEnabled = true }) });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FlagbitClient(httpClient);

        Assert.True(await client.IsEnabledAsync("checkout"));
        Assert.True(await client.IsEnabledAsync("checkout", null));
        Assert.True(await client.IsEnabledAsync("checkout", null, CancellationToken.None));
        Assert.Equal("modern", await client.GetVariationAsync("checkout", "modern", "classic"));
        Assert.Equal("modern", await client.GetVariationAsync("checkout", "modern", "classic", null));
        Assert.Null(await client.GetVariationAsync<string?>("checkout", null, "classic", null, CancellationToken.None));
        Assert.Equal(6, handler.CallCount);
        Assert.Equal("/api/flags/checkout/enabled", handler.RequestUri!.PathAndQuery);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContextualVariationsSelectCallerOwnedValues(bool isEnabled)
    {
        var enabled = new { Name = "modern", Price = 20 };
        var disabled = new { Name = "classic", Price = 10 };
        using var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.DoesNotContain("modern", body);
            Assert.DoesNotContain("classic", body);
            Assert.DoesNotContain("price", body, StringComparison.OrdinalIgnoreCase);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { key = "checkout", isEnabled }) };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FlagbitClient(httpClient);

        var selected = await client.GetContextualVariationAsync("checkout", enabled, disabled, new FeatureFlagEvaluationContext("user-123", "production"));

        Assert.Same(isEnabled ? enabled : disabled, selected);
        Assert.Equal(1, handler.CallCount);
    }
}
