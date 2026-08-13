using System.Net;
using System.Net.Http.Json;
using Flagbit.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;

namespace Flagbit.Api.Tests;

public sealed class FeatureFlagApiTests
{
    [Fact]
    public async Task FeatureFlagLifecycleWorks()
    {
        using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("new-checkout", true));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal("/api/flags/new-checkout", createResponse.Headers.Location?.OriginalString);

        var createdFlag = await createResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        AssertFlag(createdFlag, "new-checkout", true);

        var getResponse = await client.GetAsync("/api/flags/new-checkout");
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

        var deleteResponse = await client.DeleteAsync("/api/flags/new-checkout");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deletedFlagResponse = await client.GetAsync("/api/flags/new-checkout");
        Assert.Equal(HttpStatusCode.NotFound, deletedFlagResponse.StatusCode);
    }

    [Fact]
    public async Task InvalidDuplicateAndUnknownFlagsReturnExpectedResponses()
    {
        using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();

        var invalidResponse = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("", false));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("recommendations", false));
        var duplicateResponse = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("recommendations", true));
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        var duplicateProblem = await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Feature flag already exists", duplicateProblem?.Title);
        Assert.Equal("A feature flag with the key 'recommendations' already exists.", duplicateProblem?.Detail);
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
        using var application = new WebApplicationFactory<Program>();
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

    private static void AssertFlag(FeatureFlagResponse? flag, string key, bool isEnabled, IReadOnlyCollection<string>? targetedUserIds = null, int? rolloutPercentage = null)
    {
        Assert.NotNull(flag);
        Assert.Equal(key, flag.Key);
        Assert.Equal(isEnabled, flag.IsEnabled);
        Assert.Equal(targetedUserIds ?? [], flag.TargetedUserIds);
        Assert.Equal(rolloutPercentage, flag.RolloutPercentage);
    }
}
