using System.Net;
using System.Net.Http.Json;
using Flagbit.Api.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flagbit.Api.Tests;

public sealed class ApiKeyAuthenticationTests
{
    [Theory]
    [InlineData("GET", "/api/flags")]
    [InlineData("GET", "/api/flags/protected-flag")]
    [InlineData("GET", "/api/flags/protected-flag/enabled")]
    [InlineData("POST", "/api/flags")]
    [InlineData("POST", "/api/flags/protected-flag/evaluate")]
    [InlineData("PUT", "/api/flags/protected-flag/evaluation")]
    [InlineData("PUT", "/api/flags/protected-flag/enable")]
    [InlineData("PUT", "/api/flags/protected-flag/disable")]
    [InlineData("DELETE", "/api/flags/protected-flag")]
    public async Task FlagEndpointsRejectMissingInvalidOrMultipleKeys(string method, string path)
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();
        string[]?[] credentials = [null, [""], ["invalid-key"], ["TEST-MANAGEMENT-KEY"], ["test-management-key", "test-evaluation-key"], ["test-management-key, test-evaluation-key"]];

        foreach (var keys in credentials)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Content = JsonContent.Create(new { key = "protected-flag" });

            if (keys is not null)
            {
                request.Headers.TryAddWithoutValidation("X-Api-Key", keys);
            }

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "ApiKey");
        }
    }

    [Theory]
    [InlineData("GET", "/api/flags")]
    [InlineData("GET", "/api/flags/protected-flag")]
    [InlineData("POST", "/api/flags")]
    [InlineData("PUT", "/api/flags/protected-flag/evaluation")]
    [InlineData("PUT", "/api/flags/protected-flag/enable")]
    [InlineData("PUT", "/api/flags/protected-flag/disable")]
    [InlineData("DELETE", "/api/flags/protected-flag")]
    public async Task EvaluationKeyCannotAccessManagementEndpoints(string method, string path)
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-evaluation-key");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Content = JsonContent.Create(new { key = "protected-flag" });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApiKeyInQueryStringDoesNotAuthenticate()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/api/flags?apiKey=test-management-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StatusDoesNotRequireAnApiKey()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("", "test-evaluation-key")]
    [InlineData("test-management-key", "")]
    [InlineData(" ", "test-evaluation-key")]
    [InlineData("test-management-key", " ")]
    [InlineData("same-key", "same-key")]
    public void MissingOrIdenticalKeysPreventStartup(string managementKey, string evaluationKey)
    {
        using var application = CreateApplication(managementKey, evaluationKey);

        Assert.Throws<OptionsValidationException>(() => application.CreateClient());
    }

    private static WebApplicationFactory<Program> CreateApplication(string managementKey = "test-management-key", string evaluationKey = "test-evaluation-key")
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services => services.PostConfigure<ApiKeyOptions>(options =>
            {
                options.ManagementKey = managementKey;
                options.EvaluationKey = evaluationKey;
            }));
        });
    }
}
