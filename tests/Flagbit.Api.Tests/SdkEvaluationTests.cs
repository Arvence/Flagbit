using System.Net;
using System.Net.Http.Json;
using Flagbit.Api.Authentication;
using Flagbit.Api.Contracts;
using Flagbit.Infrastructure.Persistence;
using Flagbit.Sdk;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Flagbit.Api.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class SdkEvaluationTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    public Task InitializeAsync() => postgreSql.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ContextualSdkUsesEveryRestrictionAndObservesGeneratedKeyRevocation()
    {
        using var application = CreateApplication();
        using var management = CreateClient(application, "test-management-key");
        var now = DateTimeOffset.UtcNow;
        const string userId = "User /?#+%\u00e9";
        using var dependency = await management.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("accounts", true));
        Assert.Equal(HttpStatusCode.Created, dependency.StatusCode);
        var settings = new UpdateFeatureFlagEvaluationRequest([userId], 100, ["production"], [new FeatureFlagRuleRequest("plan", "Equals", "enterprise")], now.AddDays(-1), now.AddDays(1), ["accounts"]);
        using var created = await management.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("sdk-checkout", true,
            settings.TargetedUserIds, settings.RolloutPercentage, settings.Environments, settings.Rules, settings.StartsAt, settings.EndsAt, settings.DependencyKeys));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var key = await CreateKeyAsync(management);
        using var evaluationHttp = CreateClient(application, key.Key);
        var sdk = new FlagbitClient(evaluationHttp);
        var context = new FeatureFlagEvaluationContext(userId, "PRODUCTION", new Dictionary<string, string> { ["PLAN"] = "Enterprise" });

        Assert.True(await sdk.EvaluateAsync("sdk-checkout", context));
        Assert.Equal("modern", await sdk.GetContextualVariationAsync("sdk-checkout", "modern", "classic", context));
        Assert.False(await sdk.IsEnabledAsync("sdk-checkout", userId));
        Assert.False(await sdk.EvaluateAsync("sdk-checkout", context with { UserId = "someone-else" }));
        Assert.False(await sdk.EvaluateAsync("sdk-checkout", context with { Environment = "staging" }));
        Assert.False(await sdk.EvaluateAsync("sdk-checkout", context with { Attributes = null }));
        Assert.False(await sdk.EvaluateAsync("sdk-checkout", context with { Attributes = new Dictionary<string, string> { ["plan"] = "free" } }));

        using var disabledDependency = await management.PutAsync("/api/flags/accounts/disable", null);
        Assert.Equal(HttpStatusCode.OK, disabledDependency.StatusCode);
        Assert.Equal("classic", await sdk.GetContextualVariationAsync("sdk-checkout", "modern", "classic", context));
        using var enabledDependency = await management.PutAsync("/api/flags/accounts/enable", null);
        Assert.Equal(HttpStatusCode.OK, enabledDependency.StatusCode);
        Assert.True(await sdk.EvaluateAsync("sdk-checkout", context));

        foreach (var rejectedSettings in new[] { settings with { RolloutPercentage = 0 }, settings with { StartsAt = now.AddDays(1), EndsAt = now.AddDays(2) }, settings with { StartsAt = now.AddDays(-2), EndsAt = now.AddDays(-1) } })
        {
            using var updated = await management.PutAsJsonAsync("/api/flags/sdk-checkout/evaluation", rejectedSettings);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.False(await sdk.EvaluateAsync("sdk-checkout", context));
        }

        using var restored = await management.PutAsJsonAsync("/api/flags/sdk-checkout/evaluation", settings);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.True(await sdk.EvaluateAsync("sdk-checkout", context));
        using var disabled = await management.PutAsync("/api/flags/sdk-checkout/disable", null);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False(await sdk.EvaluateAsync("sdk-checkout", context));

        var invalidContext = context with { Attributes = new Dictionary<string, string> { ["plan"] = "enterprise", ["PLAN"] = "free" } };
        var invalid = await Assert.ThrowsAsync<HttpRequestException>(() => sdk.EvaluateAsync("sdk-checkout", invalidContext));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var forbidden = await evaluationHttp.GetAsync("/api/keys");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var revoked = await management.DeleteAsync($"/api/keys/{key.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        Func<Task>[] requests =
        [
            () => sdk.IsEnabledAsync("sdk-checkout", userId),
            () => sdk.EvaluateAsync("sdk-checkout", context),
            () => sdk.GetVariationAsync("sdk-checkout", "modern", "classic", userId),
            () => sdk.GetContextualVariationAsync("sdk-checkout", "modern", "classic", context)
        ];
        foreach (var request in requests)
        {
            var unauthorized = await Assert.ThrowsAsync<HttpRequestException>(request);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }
    }

    [Fact]
    public async Task LegacyAndContextualSdkAgreeForEquivalentContextOverRealHttp()
    {
        using var application = CreateApplication();
        using var management = CreateClient(application, "test-management-key");
        const string flagKey = "SDK checkout?plan=pro#%\u00e9";
        const string userId = "user /?#+%\u6771\u4eac";
        using var created = await management.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest(flagKey, true, [userId], 100));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var key = await CreateKeyAsync(management);
        using var evaluationHttp = CreateClient(application, key.Key);
        var sdk = new FlagbitClient(evaluationHttp);

        foreach (var (user, expected) in new (string? User, bool Expected)[] { (userId, true), ("someone-else", false), (null, false), ("", false) })
        {
            Assert.Equal(expected, await sdk.IsEnabledAsync(flagKey, user));
            Assert.Equal(expected, await sdk.EvaluateAsync(flagKey, new FeatureFlagEvaluationContext(user)));
            var variation = expected ? "modern" : "classic";
            Assert.Equal(variation, await sdk.GetVariationAsync(flagKey, "modern", "classic", user));
            Assert.Equal(variation, await sdk.GetContextualVariationAsync(flagKey, "modern", "classic", new FeatureFlagEvaluationContext(user)));
        }

        Assert.False(await sdk.IsEnabledAsync("missing", null));
        Assert.False(await sdk.EvaluateAsync("missing", new FeatureFlagEvaluationContext()));
    }

    private WebApplicationFactory<Program> CreateApplication()
    {
        var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<ApiKeyOptions>(options =>
                {
                    options.ManagementKey = "test-management-key";
                    options.EvaluationKey = "test-evaluation-key";
                });
                services.RemoveAll<IDbContextOptionsConfiguration<FlagbitDbContext>>();
                services.RemoveAll<DbContextOptions<FlagbitDbContext>>();
                services.RemoveAll<FlagbitDbContext>();
                services.AddDbContext<FlagbitDbContext>(options => options.UseNpgsql(postgreSql.ConnectionString));
            });
        });
        application.UseKestrel(0);
        return application;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> application, string key)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private static async Task<CreatedApiKeyResponse> CreateKeyAsync(HttpClient management)
    {
        using var response = await management.PostAsJsonAsync("/api/keys", new CreateApiKeyRequest("sdk-consumer"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<CreatedApiKeyResponse>())!;
    }
}
