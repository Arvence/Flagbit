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
    private static readonly string[] ExpectedMigrations = ["20260826093617_InitialPostgreSql", "20260909112642_AddEvaluationApiKeys", "20261007161637_UseApplicationIdentifierNormalization"];
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
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
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
    public async Task MigrationsCreateLatestSchemaAndCanBeReappliedWithoutDataLoss()
    {
        await using var database = _postgreSql.CreateDbContext();
        await AssertLatestSchemaAsync(database);

        using var application = CreateApplication();
        using var managementClient = CreateClient(application, "test-management-key");
        Assert.Empty((await managementClient.GetFromJsonAsync<FeatureFlagResponse[]>("/api/flags"))!);
        Assert.Empty((await managementClient.GetFromJsonAsync<ApiKeyResponse[]>("/api/keys"))!);
        await CreateFlagAsync(managementClient);
        var key = await CreateKeyAsync(managementClient, "fresh-database-app");

        await AssertReapplyingMigrationsPreservesDataAsync(database);

        using var evaluationClient = CreateClient(application, key.Key);
        await AssertEvaluationAsync(evaluationClient, HttpStatusCode.OK);
        var keys = await managementClient.GetFromJsonAsync<ApiKeyResponse[]>("/api/keys");
        Assert.Equal(key.Id, Assert.Single(keys!).Id);

        using var revokeResponse = await managementClient.DeleteAsync($"/api/keys/{key.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        await AssertEvaluationAsync(evaluationClient, HttpStatusCode.Unauthorized);
        using var deleteResponse = await managementClient.DeleteAsync("/api/flags/protected-flag");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Empty((await managementClient.GetFromJsonAsync<FeatureFlagResponse[]>("/api/flags"))!);
        Assert.Empty((await managementClient.GetFromJsonAsync<ApiKeyResponse[]>("/api/keys"))!);
    }

    [Fact]
    public async Task MigrationPreservesExistingFlagsAndAddsKeyStorage()
    {
        await using var database = _postgreSql.CreateDbContext();
        await database.Database.EnsureDeletedAsync();
        await database.GetService<IMigrator>().MigrateAsync(ExpectedMigrations[0]);
        Assert.Equal(ExpectedMigrations[0], Assert.Single(await database.Database.GetAppliedMigrationsAsync()));
        Assert.Equal(ExpectedMigrations.Skip(1), await database.Database.GetPendingMigrationsAsync());

        await database.Database.ExecuteSqlRawAsync("""
            INSERT INTO feature_flags (key, is_enabled, rollout_percentage, starts_at, ends_at)
            VALUES ('protected-flag', true, NULL, NULL, NULL),
                   ('Legacy-Checkout', true, 63, '2026-10-07 12:00:00+00', '2026-10-08 12:00:00+00'),
                   ('disabled-flag', false, NULL, NULL, NULL);
            INSERT INTO feature_flag_target_users (feature_flag_id, user_id)
            SELECT id, u FROM feature_flags CROSS JOIN unnest(ARRAY['User-123', 'user-456']) AS u WHERE key = 'Legacy-Checkout';
            INSERT INTO feature_flag_environments (feature_flag_id, name)
            SELECT id, e FROM feature_flags CROSS JOIN unnest(ARRAY['production', 'staging']) AS e WHERE key = 'Legacy-Checkout';
            INSERT INTO feature_flag_rules (feature_flag_id, position, attribute, operator, value)
            SELECT id, 0, 'plan', 'Equals', 'enterprise' FROM feature_flags WHERE key = 'Legacy-Checkout';
            INSERT INTO feature_flag_rules (feature_flag_id, position, attribute, operator, value)
            SELECT id, 1, 'region', 'StartsWith', 'eu-' FROM feature_flags WHERE key = 'Legacy-Checkout';
            INSERT INTO feature_flag_dependencies (feature_flag_id, dependency_key)
            SELECT id, 'protected-flag' FROM feature_flags WHERE key = 'Legacy-Checkout';
            """);
        var flagsBeforeUpgrade = await ReadFlagRowsAsync(database);

        await database.Database.MigrateAsync();
        await AssertLatestSchemaAsync(database);
        Assert.Equal(flagsBeforeUpgrade, await ReadFlagRowsAsync(database));
        using var application = CreateApplication();
        using var managementClient = CreateClient(application, "test-management-key");
        Assert.Empty((await managementClient.GetFromJsonAsync<ApiKeyResponse[]>("/api/keys"))!);
        var key = await CreateKeyAsync(managementClient, "migrated-app");

        await AssertReapplyingMigrationsPreservesDataAsync(database);

        var flags = await managementClient.GetFromJsonAsync<FeatureFlagResponse[]>("/api/flags");
        Assert.Equal(3, flags!.Length);
        var migratedFlag = await managementClient.GetFromJsonAsync<FeatureFlagResponse>("/api/flags/LEGACY-CHECKOUT");
        Assert.Equal("Legacy-Checkout", migratedFlag?.Key);
        using var evaluationClient = CreateClient(application, key.Key);
        await AssertEvaluationAsync(evaluationClient, HttpStatusCode.OK);
    }

    [Fact]
    public async Task DatabaseRestartPreservesFlagsSettingsAndKeyRevocation()
    {
        CreatedApiKeyResponse active;
        CreatedApiKeyResponse revoked;
        string[] flagsBefore;
        string[] keysBefore;
        using (var application = CreateApplication())
        using (var managementClient = CreateClient(application, "test-management-key"))
        {
            await CreateFlagAsync(managementClient);
            var start = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(3));
            using var created = await managementClient.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("restart-settings", true, ["user-123"], 50,
                ["production"], [new FeatureFlagRuleRequest("plan", "Equals", "enterprise")], start, start.AddDays(1), ["protected-flag"]));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            active = await CreateKeyAsync(managementClient, "active-after-restart");
            revoked = await CreateKeyAsync(managementClient, "revoked-before-restart");
            using var revoke = await managementClient.DeleteAsync($"/api/keys/{revoked.Id}");
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            await using var database = _postgreSql.CreateDbContext();
            flagsBefore = await ReadFlagRowsAsync(database);
            keysBefore = await ReadKeyRowsAsync(database);
        }

        await _postgreSql.RestartAsync();

        await using var verification = _postgreSql.CreateDbContext();
        await AssertLatestSchemaAsync(verification);
        Assert.Equal(flagsBefore, await ReadFlagRowsAsync(verification));
        Assert.Equal(keysBefore, await ReadKeyRowsAsync(verification));
        using var restarted = CreateApplication();
        using var activeClient = CreateClient(restarted, active.Key);
        using var revokedClient = CreateClient(restarted, revoked.Key);
        await AssertEvaluationAsync(activeClient, HttpStatusCode.OK);
        await AssertEvaluationAsync(revokedClient, HttpStatusCode.Unauthorized);
        using var management = CreateClient(restarted, "test-management-key");
        Assert.Equal(active.Id, Assert.Single((await management.GetFromJsonAsync<ApiKeyResponse[]>("/api/keys"))!).Id);
    }

    private static async Task AssertLatestSchemaAsync(FlagbitDbContext database)
    {
        Assert.Equal(ExpectedMigrations, database.Database.GetMigrations());
        Assert.Equal(ExpectedMigrations, await database.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        Assert.False(database.Database.HasPendingModelChanges());
    }

    private static async Task AssertReapplyingMigrationsPreservesDataAsync(FlagbitDbContext database)
    {
        var flagsBefore = await ReadFlagRowsAsync(database);
        var keysBefore = await ReadKeyRowsAsync(database);
        Assert.NotEmpty(flagsBefore);
        Assert.NotEmpty(keysBefore);

        await database.Database.MigrateAsync();

        await AssertLatestSchemaAsync(database);
        Assert.Equal(flagsBefore, await ReadFlagRowsAsync(database));
        Assert.Equal(keysBefore, await ReadKeyRowsAsync(database));
    }

    private static Task<string[]> ReadFlagRowsAsync(FlagbitDbContext database)
    {
        return database.Database.SqlQueryRaw<string>("""
            SELECT 'feature_flags:' || row_to_json(f)::text AS "Value" FROM feature_flags AS f
            UNION ALL
            SELECT 'feature_flag_target_users:' || row_to_json(t)::text FROM feature_flag_target_users AS t
            UNION ALL
            SELECT 'feature_flag_environments:' || row_to_json(e)::text FROM feature_flag_environments AS e
            UNION ALL
            SELECT 'feature_flag_rules:' || row_to_json(r)::text FROM feature_flag_rules AS r
            UNION ALL
            SELECT 'feature_flag_dependencies:' || row_to_json(d)::text FROM feature_flag_dependencies AS d
            ORDER BY "Value"
            """).ToArrayAsync();
    }

    private static Task<string[]> ReadKeyRowsAsync(FlagbitDbContext database)
    {
        return database.Database.SqlQueryRaw<string>("""
            SELECT row_to_json(k)::text AS "Value" FROM evaluation_api_keys AS k ORDER BY id
            """).ToArrayAsync();
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
        else if (expectedStatus == HttpStatusCode.Unauthorized)
        {
            Assert.Empty(await getResponse.Content.ReadAsByteArrayAsync());
            Assert.Empty(await postResponse.Content.ReadAsByteArrayAsync());
            Assert.Contains(getResponse.Headers.WwwAuthenticate, header => header.Scheme == "ApiKey");
            Assert.Contains(postResponse.Headers.WwwAuthenticate, header => header.Scheme == "ApiKey");
        }
    }
}
