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
        Assert.Equal(new FeatureFlagResponse("new-checkout", true), createdFlag);

        var getResponse = await client.GetAsync("/api/flags/new-checkout");
        var storedFlag = await getResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(createdFlag, storedFlag);

        var flags = await client.GetFromJsonAsync<FeatureFlagResponse[]>("/api/flags");
        var listedFlag = Assert.Single(flags!);
        Assert.Equal(createdFlag, listedFlag);

        var disableResponse = await client.PutAsync("/api/flags/new-checkout/disable", null);
        var disabledFlag = await disableResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        Assert.Equal(new FeatureFlagResponse("new-checkout", false), disabledFlag);

        var disabledEvaluation = await client.GetFromJsonAsync<FeatureFlagEvaluationResponse>("/api/flags/new-checkout/enabled");
        Assert.Equal(new FeatureFlagEvaluationResponse("new-checkout", false), disabledEvaluation);

        var enableResponse = await client.PutAsync("/api/flags/new-checkout/enable", null);
        var enabledFlag = await enableResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);
        Assert.Equal(new FeatureFlagResponse("new-checkout", true), enabledFlag);

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
}
