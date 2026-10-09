using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flagbit.Api.Authentication;
using Flagbit.Api.Contracts;
using Flagbit.Infrastructure.Persistence;
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
public sealed class ApiValidationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSql;

    public ApiValidationTests(PostgreSqlFixture postgreSql)
    {
        _postgreSql = postgreSql;
    }

    public Task InitializeAsync() => _postgreSql.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task NonJsonAcceptHeaderDoesNotTurnRequestErrorsIntoServerFailures(string environment)
    {
        using var application = CreateApplication(environment);
        using var client = CreateClient(application);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");
        foreach (var mediaType in new[] { "application/json", "text/plain" })
        {
            using var response = await client.PostAsync("/api/flags", new StringContent("{", Encoding.UTF8, mediaType));
            await AssertProblemAsync(response, mediaType == "application/json" ? HttpStatusCode.BadRequest : HttpStatusCode.UnsupportedMediaType, mediaType);
        }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task RequiredBodiesAndJsonTypesReturnProblemDetails(string environment)
    {
        using var application = CreateApplication(environment);
        using var client = CreateClient(application);
        await SeedAsync(client);
        var before = await client.GetStringAsync("/api/flags/checkout");
        (string Method, string Path)[] endpoints = [("POST", "/api/flags"), ("PUT", "/api/flags/checkout/evaluation"), ("POST", "/api/flags/checkout/evaluate"), ("POST", "/api/keys")];

        foreach (var (method, path) in endpoints)
        {
            foreach (var body in new string?[] { null, "", "null", "{", "[]", "true", "42", "\"text\"" })
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (body is not null)
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                using var response = await client.SendAsync(request);
                await AssertProblemAsync(response, HttpStatusCode.BadRequest, $"{environment}: {method} {path}, body={body ?? "<missing>"}");
            }

            using var unsupported = new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent("{}", Encoding.UTF8, "text/plain") };
            using var unsupportedResponse = await client.SendAsync(unsupported);
            await AssertProblemAsync(unsupportedResponse, HttpStatusCode.UnsupportedMediaType, path);
        }

        (string Path, string Body)[] invalidFields =
        [
            ("/api/flags", "{}"), ("/api/flags", "{\"key\":null}"), ("/api/flags", "{\"key\":42}"),
            ("/api/flags", "{\"key\":\"new-flag\",\"isEnabled\":\"true\"}"),
            ("/api/keys", "{}"), ("/api/keys", "{\"name\":null}"), ("/api/keys", "{\"name\":42}"),
            ("/api/flags/checkout/evaluate", "{\"userId\":42}"), ("/api/flags/checkout/evaluate", "{\"environment\":false}"),
            ("/api/flags/checkout/evaluate", "{\"attributes\":[]}"), ("/api/flags/checkout/evaluate", "{\"attributes\":{\"plan\":42}}")
        ];
        foreach (var (path, body) in invalidFields)
        {
            using var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, body);
        }

        Assert.Equal(before, await client.GetStringAsync("/api/flags/checkout"));
        Assert.Single((await client.GetFromJsonAsync<FeatureFlagResponse[]>("/api/flags"))!);
        Assert.Empty((await client.GetFromJsonAsync<ApiKeyResponse[]>("/api/keys"))!);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task BoundaryRolloutsAndContextValidationFollowEvaluationContract(string environment)
    {
        using var application = CreateApplication(environment);
        using var client = CreateClient(application);
        foreach (var percentage in new[] { 0, 100 })
        {
            var key = $"rollout-{percentage}";
            using var created = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest(key, true, RolloutPercentage: percentage,
                Rules: [new FeatureFlagRuleRequest("plan", "eQuAlS", "enterprise")]));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var flag = (await created.Content.ReadFromJsonAsync<FeatureFlagResponse>())!;
            Assert.Equal(percentage, flag.RolloutPercentage);
            Assert.Equal("Equals", Assert.Single(flag.Rules).Operator);
            using var evaluated = await client.PostAsJsonAsync($"/api/flags/{key}/evaluate", new EvaluateFeatureFlagRequest("user-123", Attributes: new Dictionary<string, string> { ["PLAN"] = "Enterprise" }));
            Assert.Equal(HttpStatusCode.OK, evaluated.StatusCode);
            Assert.Equal(percentage == 100, (await evaluated.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>())!.IsEnabled);
        }

        using var disabled = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("disabled"));
        Assert.Equal(HttpStatusCode.Created, disabled.StatusCode);
        foreach (var key in new[] { "rollout-100", "disabled", "missing" })
        {
            foreach (var attributes in new[] { "\"plan\":\"enterprise\",\"PLAN\":\"free\"", "\"PLAN\":\"free\",\"plan\":\"enterprise\"" })
            {
                using var ambiguous = await client.PostAsync($"/api/flags/{key}/evaluate", new StringContent("{\"attributes\":{" + attributes + "}}", Encoding.UTF8, "application/json"));
                await AssertProblemAsync(ambiguous, HttpStatusCode.BadRequest, $"{key}: {attributes}");
            }
        }

        using var nullAttribute = await client.PostAsync("/api/flags/rollout-100/evaluate", new StringContent("{\"userId\":\"user-123\",\"attributes\":{\"plan\":null}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, nullAttribute.StatusCode);
        Assert.False((await nullAttribute.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>())!.IsEnabled);
        using var missing = await client.PostAsJsonAsync("/api/flags/missing/evaluate", new { });
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.False((await missing.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>())!.IsEnabled);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task InvalidSettingsNeverChangeStoredFlags(string environment)
    {
        using var application = CreateApplication(environment);
        using var client = CreateClient(application);
        await SeedAsync(client);
        var before = await client.GetStringAsync("/api/flags/checkout");
        string[] settings =
        [
            "\"rolloutPercentage\":-1", "\"rolloutPercentage\":101", "\"rolloutPercentage\":2.5", "\"rolloutPercentage\":\"50\"",
            "\"rolloutPercentage\":true", "\"targetedUserIds\":{}", "\"targetedUserIds\":[42]", "\"environments\":\"production\"",
            "\"dependencyKeys\":[false]", "\"dependencyKeys\":[\"CHECKOUT\"]", "\"rules\":[null]", "\"rules\":true",
            "\"rules\":[{\"attribute\":\"plan\",\"operator\":\"Unknown\",\"value\":\"enterprise\"}]",
            "\"rules\":[{\"attribute\":\"plan\",\"operator\":\"0\",\"value\":\"enterprise\"}]",
            "\"rules\":[{\"attribute\":\"plan\",\"operator\":null,\"value\":\"enterprise\"}]",
            "\"rules\":[{\"attribute\":null,\"operator\":\"Equals\",\"value\":\"enterprise\"}]",
            "\"rules\":[{\"attribute\":\"plan\",\"operator\":\"Equals\",\"value\":null}]",
            "\"startsAt\":\"not-a-date\"", "\"endsAt\":123",
            "\"startsAt\":\"2026-10-09T12:00:00+03:00\",\"endsAt\":\"2026-10-09T08:00:00Z\""
        ];

        foreach (var setting in settings)
        {
            using var update = await client.PutAsync("/api/flags/checkout/evaluation", new StringContent("{" + setting + "}", Encoding.UTF8, "application/json"));
            await AssertProblemAsync(update, HttpStatusCode.BadRequest, setting);
            Assert.Equal(before, await client.GetStringAsync("/api/flags/checkout"));
            using var create = await client.PostAsync("/api/flags", new StringContent("{\"key\":\"new-flag\"," + setting.Replace("CHECKOUT", "NEW-FLAG") + "}", Encoding.UTF8, "application/json"));
            await AssertProblemAsync(create, HttpStatusCode.BadRequest, setting);
            Assert.Single((await client.GetFromJsonAsync<FeatureFlagResponse[]>("/api/flags"))!);
        }
    }

    private WebApplicationFactory<Program> CreateApplication(string environment)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
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
                services.AddDbContext<FlagbitDbContext>(options => options.UseNpgsql(_postgreSql.ConnectionString));
            });
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> application)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-management-key");
        return client;
    }

    private static async Task SeedAsync(HttpClient client)
    {
        using var created = await client.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("checkout", true, ["user-123"], 50,
            ["production"], [new FeatureFlagRuleRequest("plan", "Equals", "enterprise")],
            new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), DependencyKeys: ["accounts"]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string scenario)
    {
        Assert.True(response.StatusCode == expected, $"{scenario}: expected {expected}, got {response.StatusCode}.");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)expected, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("title").GetString()));
        Assert.False(json.RootElement.TryGetProperty("exception", out _));
        Assert.False(json.RootElement.TryGetProperty("stackTrace", out _));
    }
}
