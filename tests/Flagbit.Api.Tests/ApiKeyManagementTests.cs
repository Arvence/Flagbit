using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Flagbit.Api.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ApiKeyManagementTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSql;

    public ApiKeyManagementTests(PostgreSqlFixture postgreSql)
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
    public async Task KeysCanBeCreatedListedAndRevokedIndependently()
    {
        using var application = CreateApplication();
        using var managementClient = CreateClient(application, "test-management-key");
        await CreateFlagAsync(managementClient);
        var first = await CreateKeyAsync(managementClient, " app-one ");
        var second = await CreateKeyAsync(managementClient, "app-two");

        Assert.Equal("app-one", first.Name);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Key, second.Key);

        var listJson = await managementClient.GetStringAsync("/api/keys");
        using var list = JsonDocument.Parse(listJson);
        Assert.Equal(2, list.RootElement.GetArrayLength());
        foreach (var item in list.RootElement.EnumerateArray())
        {
            Assert.Equal(new[] { "createdAt", "id", "name" }, item.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
        }

        Assert.DoesNotContain(first.Key, listJson);
        Assert.DoesNotContain(second.Key, listJson);

        await using var database = _postgreSql.CreateDbContext();
        var storedJson = await database.Database.SqlQuery<string>($"SELECT row_to_json(k)::text AS \"Value\" FROM evaluation_api_keys AS k WHERE id = {first.Id}").SingleAsync();
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first.Key)));
        Assert.Contains(expectedHash, storedJson);
        Assert.DoesNotContain(first.Key, storedJson);

        using var firstClient = CreateClient(application, first.Key);
        using var secondClient = CreateClient(application, second.Key);
        await AssertEvaluationAsync(firstClient, HttpStatusCode.OK);
        await AssertEvaluationAsync(secondClient, HttpStatusCode.OK);

        (string Method, string Path)[] managementRequests =
        [
            ("GET", "/api/flags"),
            ("GET", "/api/flags/protected-flag"),
            ("POST", "/api/flags"),
            ("PUT", "/api/flags/protected-flag/evaluation"),
            ("PUT", "/api/flags/protected-flag/enable"),
            ("PUT", "/api/flags/protected-flag/disable"),
            ("DELETE", "/api/flags/protected-flag"),
            ("GET", "/api/keys"),
            ("POST", "/api/keys"),
            ("DELETE", $"/api/keys/{second.Id}")
        ];

        foreach (var (method, path) in managementRequests)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Content = JsonContent.Create(new { name = "unauthorized", key = "unauthorized" });
            using var response = await firstClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var revokeResponse = await managementClient.DeleteAsync($"/api/keys/{first.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        await AssertEvaluationAsync(firstClient, HttpStatusCode.Unauthorized);
        await AssertEvaluationAsync(secondClient, HttpStatusCode.OK);

        using var repeatResponse = await managementClient.DeleteAsync($"/api/keys/{first.Id}");
        Assert.Equal(HttpStatusCode.NotFound, repeatResponse.StatusCode);
        var remainingKeys = await managementClient.GetFromJsonAsync<ApiKeyResponse[]>("/api/keys");
        Assert.Equal(second.Id, Assert.Single(remainingKeys!).Id);
    }

    [Fact]
    public async Task RevocationIsObservedByOtherInstancesAndSurvivesRestarts()
    {
        CreatedApiKeyResponse revoked;
        CreatedApiKeyResponse active;

        using (var firstApplication = CreateApplication())
        using (var secondApplication = CreateApplication())
        using (var managementClient = CreateClient(firstApplication, "test-management-key"))
        {
            await CreateFlagAsync(managementClient);
            revoked = await CreateKeyAsync(managementClient, "revoked-app");
            active = await CreateKeyAsync(managementClient, "active-app");
            using var otherInstanceClient = CreateClient(secondApplication, revoked.Key);
            await AssertEvaluationAsync(otherInstanceClient, HttpStatusCode.OK);

            using var response = await managementClient.DeleteAsync($"/api/keys/{revoked.Id}");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await AssertEvaluationAsync(otherInstanceClient, HttpStatusCode.Unauthorized);
        }

        using var restartedApplication = CreateApplication();
        using var revokedClient = CreateClient(restartedApplication, revoked.Key);
        using var activeClient = CreateClient(restartedApplication, active.Key);
        await AssertEvaluationAsync(revokedClient, HttpStatusCode.Unauthorized);
        await AssertEvaluationAsync(activeClient, HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvalidNamesDoNotCreateKeys()
    {
        using var application = CreateApplication();
        using var managementClient = CreateClient(application, "test-management-key");
        string?[] invalidNames = [null, "", "   ", new string('a', 101)];

        foreach (var name in invalidNames)
        {
            using var response = await managementClient.PostAsJsonAsync("/api/keys", new CreateApiKeyRequest(name));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty((await managementClient.GetFromJsonAsync<ApiKeyResponse[]>("/api/keys"))!);
        var boundaryName = new string('a', 100);
        Assert.Equal(boundaryName, (await CreateKeyAsync(managementClient, boundaryName)).Name);
    }

    [Fact]
    public async Task UnknownAndTamperedKeysCannotAuthenticate()
    {
        using var application = CreateApplication();
        using var managementClient = CreateClient(application, "test-management-key");
        var key = await CreateKeyAsync(managementClient, "app");
        var tamperedKey = key.Key[..^1] + (key.Key[^1] == 'A' ? 'B' : 'A');

        foreach (var secret in new[] { "fb_eval_" + new string('0', 64), tamperedKey })
        {
            using var client = CreateClient(application, secret);
            await AssertEvaluationAsync(client, HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task MigrationPreservesExistingFlagsAndAddsKeyStorage()
    {
        await using var database = _postgreSql.CreateDbContext();
        await database.Database.EnsureDeletedAsync();
        await database.GetService<IMigrator>().MigrateAsync("20260826093617_InitialPostgreSql");

        using var application = CreateApplication();
        using var managementClient = CreateClient(application, "test-management-key");
        await CreateFlagAsync(managementClient);

        await database.Database.MigrateAsync();
        Assert.False(database.Database.HasPendingModelChanges());
        var key = await CreateKeyAsync(managementClient, "migrated-app");
        using var evaluationClient = CreateClient(application, key.Key);
        await AssertEvaluationAsync(evaluationClient, HttpStatusCode.OK);
    }

    private WebApplicationFactory<Program> CreateApplication()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
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

    private static HttpClient CreateClient(WebApplicationFactory<Program> application, string apiKey)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }

    private static async Task CreateFlagAsync(HttpClient managementClient)
    {
        using var response = await managementClient.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("protected-flag", true));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<CreatedApiKeyResponse> CreateKeyAsync(HttpClient managementClient, string name)
    {
        using var response = await managementClient.PostAsJsonAsync("/api/keys", new CreateApiKeyRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var key = await response.Content.ReadFromJsonAsync<CreatedApiKeyResponse>();
        Assert.NotNull(key);
        Assert.NotEqual(Guid.Empty, key.Id);
        Assert.Matches("^fb_eval_[0-9A-F]{64}$", key.Key);
        return key;
    }

    private static async Task AssertEvaluationAsync(HttpClient client, HttpStatusCode expectedStatus)
    {
        using var getResponse = await client.GetAsync("/api/flags/protected-flag/enabled");
        using var postResponse = await client.PostAsJsonAsync("/api/flags/protected-flag/evaluate", new EvaluateFeatureFlagRequest());
        Assert.Equal(expectedStatus, getResponse.StatusCode);
        Assert.Equal(expectedStatus, postResponse.StatusCode);

        if (expectedStatus == HttpStatusCode.OK)
        {
            Assert.True((await getResponse.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>())?.IsEnabled);
            Assert.True((await postResponse.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>())?.IsEnabled);
        }
    }
}
