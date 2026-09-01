using System.Net;
using System.Net.Http.Json;
using Flagbit.Api.Contracts;
using Flagbit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Flagbit.Api.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class FeatureFlagApiTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSql;

    public FeatureFlagApiTests(PostgreSqlFixture postgreSql)
    {
        _postgreSql = postgreSql;
    }

    public Task InitializeAsync()
    {
        return _postgreSql.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FeatureFlagLifecycleWorks()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("new-checkout", true));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal("/api/flags/new-checkout", createResponse.Headers.Location?.OriginalString);

        var createdFlag = await createResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        AssertFlag(createdFlag, "new-checkout", true);

        var getResponse = await client.GetAsync("/api/flags/NEW-CHECKOUT");
        var storedFlag = await getResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        AssertFlag(storedFlag, "new-checkout", true);

        var flags = await client.GetFromJsonAsync<FeatureFlagResponse[]>("/api/flags");
        var listedFlag = Assert.Single(flags!);
        AssertFlag(listedFlag, "new-checkout", true);

        var disableResponse = await client.PutAsync("/api/flags/new-checkout/disable", null);
        var disabledFlag = await disableResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        AssertFlag(disabledFlag, "new-checkout", false);

        var disabledEvaluation = await client.GetFromJsonAsync<FeatureFlagEvaluationResponse>("/api/flags/new-checkout/enabled");
        Assert.Equal(new FeatureFlagEvaluationResponse("new-checkout", false), disabledEvaluation);

        var enableResponse = await client.PutAsync("/api/flags/new-checkout/enable", null);
        var enabledFlag = await enableResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);
        AssertFlag(enabledFlag, "new-checkout", true);

        var enabledEvaluation = await client.GetFromJsonAsync<FeatureFlagEvaluationResponse>("/api/flags/new-checkout/enabled");
        Assert.Equal(new FeatureFlagEvaluationResponse("new-checkout", true), enabledEvaluation);

        var deleteResponse = await client.DeleteAsync("/api/flags/NEW-CHECKOUT");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deletedFlagResponse = await client.GetAsync("/api/flags/new-checkout");
        Assert.Equal(HttpStatusCode.NotFound, deletedFlagResponse.StatusCode);
    }

    [Fact]
    public async Task InvalidDuplicateAndUnknownFlagsReturnExpectedResponses()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();

        var invalidResponse = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("", false));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var invalidRuleResponse = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("invalid-rule", true, Rules: [new FeatureFlagRuleRequest("plan", "Unknown", "enterprise")]));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRuleResponse.StatusCode);
        var invalidRuleProblem = await invalidRuleResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid request", invalidRuleProblem?.Title);

        await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("recommendations", false));
        var duplicateResponse = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("RECOMMENDATIONS", true));
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        var duplicateProblem = await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Feature flag already exists", duplicateProblem?.Title);
        Assert.Equal("A feature flag with the key 'RECOMMENDATIONS' already exists.", duplicateProblem?.Detail);
        Assert.True(duplicateProblem?.Extensions.ContainsKey("traceId"));

        var missingResponse = await client.GetAsync("/api/flags/unknown");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        var missingProblem = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Feature flag not found", missingProblem?.Title);
        Assert.Equal("A feature flag with the key 'unknown' does not exist.", missingProblem?.Detail);

        var missingEnableResponse = await client.PutAsync("/api/flags/unknown/enable", null);
        Assert.Equal(HttpStatusCode.NotFound, missingEnableResponse.StatusCode);

        var missingDeleteResponse = await client.DeleteAsync("/api/flags/unknown");
        Assert.Equal(HttpStatusCode.NotFound, missingDeleteResponse.StatusCode);

        var unknownEvaluation = await client.GetFromJsonAsync<FeatureFlagEvaluationResponse>("/api/flags/unknown/enabled");
        Assert.Equal(new FeatureFlagEvaluationResponse("unknown", false), unknownEvaluation);
    }

    [Fact]
    public async Task EvaluationSettingsCanBeCreatedUsedAndUpdated()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();

        var createRequest = new CreateFeatureFlagRequest("targeted-checkout", true, ["user-123"], 100);
        var createResponse = await client.PostAsJsonAsync("/api/flags", createRequest);
        var createdFlag = await createResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        AssertFlag(createdFlag, "targeted-checkout", true, ["user-123"], 100);

        var targetedEvaluation = await client.GetFromJsonAsync<FeatureFlagEvaluationResponse>("/api/flags/targeted-checkout/enabled?userId=user-123");
        var excludedEvaluation = await client.GetFromJsonAsync<FeatureFlagEvaluationResponse>("/api/flags/targeted-checkout/enabled?userId=user-456");

        Assert.True(targetedEvaluation?.IsEnabled);
        Assert.False(excludedEvaluation?.IsEnabled);

        var updateRequest = new UpdateFeatureFlagEvaluationRequest(["user-456"], 100);
        var updateResponse = await client.PutAsJsonAsync("/api/flags/targeted-checkout/evaluation", updateRequest);
        var updatedFlag = await updateResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        AssertFlag(updatedFlag, "targeted-checkout", true, ["user-456"], 100);

        var previousUserEvaluation = await client.GetFromJsonAsync<FeatureFlagEvaluationResponse>("/api/flags/targeted-checkout/enabled?userId=user-123");
        var newUserEvaluation = await client.GetFromJsonAsync<FeatureFlagEvaluationResponse>("/api/flags/targeted-checkout/enabled?userId=user-456");

        Assert.False(previousUserEvaluation?.IsEnabled);
        Assert.True(newUserEvaluation?.IsEnabled);
    }

    [Fact]
    public async Task AdvancedEvaluationUsesEnvironmentAttributesScheduleAndDependencies()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();
        var scheduleAnchor = DateTimeOffset.UtcNow;
        scheduleAnchor = scheduleAnchor.AddTicks(-(scheduleAnchor.Ticks % TimeSpan.TicksPerSecond));
        var startsAt = scheduleAnchor.AddMinutes(-5);
        var endsAt = scheduleAnchor.AddMinutes(5);
        var rule = new FeatureFlagRuleRequest("plan", "Equals", "enterprise");

        var dependencyResponse = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("accounts", true));
        Assert.Equal(HttpStatusCode.Created, dependencyResponse.StatusCode);

        var createRequest = new CreateFeatureFlagRequest("advanced-checkout", true, ["user-123"], 100, ["production"], [rule], startsAt, endsAt, ["accounts"]);
        var createResponse = await client.PostAsJsonAsync("/api/flags", createRequest);
        var createdFlag = await createResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        AssertFlag(createdFlag, "advanced-checkout", true, ["user-123"], 100, ["production"], [new FeatureFlagRuleResponse("plan", "Equals", "enterprise")], startsAt, endsAt, ["accounts"]);

        var matchingRequest = new EvaluateFeatureFlagRequest("user-123", "production", new Dictionary<string, string> { ["plan"] = "enterprise" });
        var matchingResponse = await client.PostAsJsonAsync("/api/flags/advanced-checkout/evaluate", matchingRequest);
        var matchingEvaluation = await matchingResponse.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>();
        Assert.Equal(HttpStatusCode.OK, matchingResponse.StatusCode);
        Assert.True(matchingEvaluation?.IsEnabled);

        var wrongEnvironment = await client.PostAsJsonAsync("/api/flags/advanced-checkout/evaluate", matchingRequest with { Environment = "staging" });
        var wrongEnvironmentEvaluation = await wrongEnvironment.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>();
        Assert.False(wrongEnvironmentEvaluation?.IsEnabled);

        var wrongAttributes = await client.PostAsJsonAsync("/api/flags/advanced-checkout/evaluate", matchingRequest with { Attributes = new Dictionary<string, string> { ["plan"] = "free" } });
        var wrongAttributesEvaluation = await wrongAttributes.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>();
        Assert.False(wrongAttributesEvaluation?.IsEnabled);

        var disableDependencyResponse = await client.PutAsync("/api/flags/accounts/disable", null);
        Assert.Equal(HttpStatusCode.OK, disableDependencyResponse.StatusCode);

        var disabledDependencyEvaluationResponse = await client.PostAsJsonAsync("/api/flags/advanced-checkout/evaluate", matchingRequest);
        var disabledDependencyEvaluation = await disabledDependencyEvaluationResponse.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>();
        Assert.False(disabledDependencyEvaluation?.IsEnabled);

        var updateRequest = new UpdateFeatureFlagEvaluationRequest(["user-456"], 100, ["staging"], [new FeatureFlagRuleRequest("country", "Equals", "TR")], startsAt, endsAt, []);
        var updateResponse = await client.PutAsJsonAsync("/api/flags/advanced-checkout/evaluation", updateRequest);
        var updatedFlag = await updateResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        AssertFlag(updatedFlag, "advanced-checkout", true, ["user-456"], 100, ["staging"], [new FeatureFlagRuleResponse("country", "Equals", "TR")], startsAt, endsAt, []);

        var updatedEvaluationRequest = new EvaluateFeatureFlagRequest("user-456", "staging", new Dictionary<string, string> { ["country"] = "TR" });
        var updatedEvaluationResponse = await client.PostAsJsonAsync("/api/flags/advanced-checkout/evaluate", updatedEvaluationRequest);
        var updatedEvaluation = await updatedEvaluationResponse.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>();
        Assert.True(updatedEvaluation?.IsEnabled);
    }

    [Fact]
    public async Task FeatureFlagPersistsAcrossApplicationRestarts()
    {
        var scheduleAnchor = DateTimeOffset.UtcNow;
        scheduleAnchor = scheduleAnchor.AddTicks(-(scheduleAnchor.Ticks % TimeSpan.TicksPerSecond));
        var startsAt = scheduleAnchor.AddMinutes(-5);
        var endsAt = scheduleAnchor.AddMinutes(5);

        using (var firstApplication = CreateApplication())
        using (var firstClient = firstApplication.CreateClient())
        {
            var dependencyResponse = await firstClient.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("accounts", true));
            Assert.Equal(HttpStatusCode.Created, dependencyResponse.StatusCode);

            var createRequest = new CreateFeatureFlagRequest("persistent-checkout", true, ["user-123"], 100, ["production"], [new FeatureFlagRuleRequest("plan", "Equals", "enterprise")], startsAt, endsAt, ["accounts"]);
            var createResponse = await firstClient.PostAsJsonAsync("/api/flags", createRequest);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        }

        using var restartedApplication = CreateApplication();
        using var restartedClient = restartedApplication.CreateClient();

        var persistedFlag = await restartedClient.GetFromJsonAsync<FeatureFlagResponse>("/api/flags/PERSISTENT-CHECKOUT");

        AssertFlag(persistedFlag, "persistent-checkout", true, ["user-123"], 100, ["production"], [new FeatureFlagRuleResponse("plan", "Equals", "enterprise")], startsAt, endsAt, ["accounts"]);

        var evaluationRequest = new EvaluateFeatureFlagRequest("user-123", "production", new Dictionary<string, string> { ["plan"] = "enterprise" });
        var evaluationResponse = await restartedClient.PostAsJsonAsync("/api/flags/persistent-checkout/evaluate", evaluationRequest);
        var evaluation = await evaluationResponse.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>();

        Assert.Equal(HttpStatusCode.OK, evaluationResponse.StatusCode);
        Assert.True(evaluation?.IsEnabled);
    }

    [Fact]
    public async Task HealthReturnsOkWhenDatabaseIsReachable()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReturnsServiceUnavailableWhenDatabaseIsUnreachable()
    {
        using var application = CreateApplication("Host=127.0.0.1;Port=1;Database=flagbit;Username=flagbit;Password=flagbit;Timeout=1");
        using var client = application.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static void AssertFlag(FeatureFlagResponse? flag, string key, bool isEnabled, IReadOnlyCollection<string>? targetedUserIds = null, int? rolloutPercentage = null, IReadOnlyCollection<string>? environments = null, IReadOnlyCollection<FeatureFlagRuleResponse>? rules = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, IReadOnlyCollection<string>? dependencyKeys = null)
    {
        Assert.NotNull(flag);
        Assert.Equal(key, flag.Key);
        Assert.Equal(isEnabled, flag.IsEnabled);
        Assert.Equal(targetedUserIds ?? [], flag.TargetedUserIds);
        Assert.Equal(rolloutPercentage, flag.RolloutPercentage);
        Assert.Equal(environments ?? [], flag.Environments);
        Assert.Equal(rules ?? [], flag.Rules);
        Assert.Equal(startsAt, flag.StartsAt);
        Assert.Equal(endsAt, flag.EndsAt);
        Assert.Equal(dependencyKeys ?? [], flag.DependencyKeys);
    }

    private WebApplicationFactory<Program> CreateApplication(string? connectionString = null)
    {
        var testConnectionString = connectionString ?? _postgreSql.ConnectionString;

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                ReplaceDbContext(services, options => options.UseNpgsql(testConnectionString));
            });
        });
    }

    private static void ReplaceDbContext(IServiceCollection services, Action<DbContextOptionsBuilder> configureOptions)
    {
        services.RemoveAll<IDbContextOptionsConfiguration<FlagbitDbContext>>();
        services.RemoveAll<DbContextOptions<FlagbitDbContext>>();
        services.RemoveAll<FlagbitDbContext>();
        services.AddDbContext<FlagbitDbContext>(configureOptions);
    }
}
