using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Flagbit.Api.Authentication;
using Flagbit.Api.Contracts;
using Flagbit.Core.Abstractions;
using Flagbit.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Flagbit.Api.Tests;

public sealed class FeatureFlagContractTests
{
    [Theory]
    [InlineData("new checkout")]
    [InlineData(" padded ")]
    [InlineData("checkout?plan=pro")]
    [InlineData("checkout#v2")]
    [InlineData("checkout+beta")]
    [InlineData("checkout%v2")]
    [InlineData("literal%2Fseparator")]
    [InlineData("%2e")]
    [InlineData("%2e%2e")]
    [InlineData("checkout\\beta")]
    [InlineData("checkout:beta")]
    [InlineData("checkout.beta")]
    [InlineData("...")]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    [InlineData(" . ")]
    [InlineData(" .. ")]
    [InlineData("\tcheckout\t")]
    [InlineData("checkout\nbeta")]
    [InlineData("caf\u00e9")]
    [InlineData("cafe\u0301")]
    [InlineData("\u0130stanbul")]
    [InlineData("stra\u00dfe")]
    [InlineData("\u6771\u4eac")]
    [InlineData("checkout-\U0001f680")]
    public async Task KeysRoundTripThroughKestrel(string key)
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-management-key");

        using var created = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest(key, true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var path = $"/api/flags/{Uri.EscapeDataString(key)}";
        Assert.Equal(path, created.Headers.Location?.OriginalString);

        using var retrieved = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, retrieved.StatusCode);
        Assert.Equal(key, (await retrieved.Content.ReadFromJsonAsync<FeatureFlagResponse>())?.Key);

        var simple = await client.GetFromJsonAsync<FeatureFlagEvaluationResponse>($"{path}/enabled");
        Assert.Equal(new FeatureFlagEvaluationResponse(key, true), simple);
        using var contextual = await client.PostAsJsonAsync($"{path}/evaluate", new EvaluateFeatureFlagRequest());
        Assert.Equal(HttpStatusCode.OK, contextual.StatusCode);
        Assert.Equal(simple, await contextual.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>());

        using var deleted = await client.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("/")]
    [InlineData("checkout/beta")]
    [InlineData("checkout/../beta")]
    [InlineData("checkout\0beta")]
    public async Task UnaddressableKeysAreRejectedBeforeStorage(string key)
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-management-key");

        using var response = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest(key, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("key", problem.Errors.Keys);
        Assert.Empty((await client.GetFromJsonAsync<FeatureFlagResponse[]>("/api/flags"))!);
    }

    [Fact]
    public async Task ExistingUnaddressableKeysRemainReadableWithoutRenaming()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-management-key");
        var store = application.Services.GetRequiredService<IFeatureFlagStore>();
        await store.AddAsync(new FeatureFlag("legacy/key", true));

        var flags = await client.GetFromJsonAsync<FeatureFlagResponse[]>("/api/flags");

        Assert.NotNull(flags);
        Assert.Equal("legacy/key", Assert.Single(flags).Key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousAttributeNamesReturnBadRequest(bool reverseOrder)
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-management-key");
        using var created = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("context", true, Rules: [new FeatureFlagRuleRequest("plan", "Equals", "enterprise")]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var attributes = reverseOrder
            ? new Dictionary<string, string> { ["PLAN"] = "free", ["plan"] = "enterprise" }
            : new Dictionary<string, string> { ["plan"] = "enterprise", ["PLAN"] = "free" };

        using var response = await client.PostAsJsonAsync("/api/flags/context/evaluate", new EvaluateFeatureFlagRequest(Attributes: attributes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var valid = await client.PostAsJsonAsync("/api/flags/context/evaluate",
            new EvaluateFeatureFlagRequest(Attributes: new Dictionary<string, string> { ["PLAN"] = "Enterprise" }));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.True((await valid.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>())?.IsEnabled);
    }

    private static WebApplicationFactory<Program> CreateApplication()
    {
        var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:PostgreSQL", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<ApiKeyOptions>(options =>
                {
                    options.ManagementKey = "test-management-key";
                    options.EvaluationKey = "test-evaluation-key";
                });
                services.RemoveAll<IFeatureFlagStore>();
                services.AddSingleton<IFeatureFlagStore, InMemoryFeatureFlagStore>();
            });
        });
        application.UseKestrel(0);
        return application;
    }

    private sealed class InMemoryFeatureFlagStore : IFeatureFlagStore
    {
        private readonly ConcurrentDictionary<string, FeatureFlag> _flags = new(StringComparer.OrdinalIgnoreCase);

        public ValueTask<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            _flags.TryGetValue(key, out var flag);
            return ValueTask.FromResult(flag);
        }

        public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyCollection<FeatureFlag>>(_flags.Values.ToArray());
        }

        public ValueTask AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
        {
            _flags[flag.Key] = flag;
            return ValueTask.CompletedTask;
        }

        public ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled, CancellationToken cancellationToken = default)
        {
            var flag = _flags[key];
            if (isEnabled)
            {
                flag.Enable();
            }
            else
            {
                flag.Disable();
            }

            return ValueTask.FromResult(flag);
        }

        public ValueTask<FeatureFlag> UpdateEvaluationAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
        {
            var stored = _flags[flag.Key];
            stored.ConfigureEvaluation(flag.TargetedUserIds, flag.RolloutPercentage, flag.Environments, flag.Rules, flag.StartsAt, flag.EndsAt, flag.DependencyKeys);
            return ValueTask.FromResult(stored);
        }

        public ValueTask<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_flags.TryRemove(key, out _));
        }
    }
}
