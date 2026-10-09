using System.Net;
using System.Net.Http.Json;
using System.Text;
using Flagbit.Api.Authentication;
using Flagbit.Api.Contracts;
using Flagbit.Core.Abstractions;
using Flagbit.Core.Models;
using Flagbit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
public sealed class ApiErrorTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    public Task InitializeAsync() => postgreSql.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("Development", "application/json")]
    [InlineData("Development", "text/plain")]
    [InlineData("Production", "application/json")]
    [InlineData("Production", "text/plain")]
    public async Task ErrorsAndLogsDoNotDiscloseConfiguredOrGeneratedSecrets(string environment, string accept)
    {
        using var logs = new CapturedLogs();
        var store = new FailingStore();
        using var application = CreateApplication(environment, store, logs);
        using var management = application.CreateClient();
        management.DefaultRequestHeaders.Add("X-Api-Key", "test-management-key");
        using var created = await management.PostAsJsonAsync("/api/keys", new CreateApiKeyRequest("error-test-app"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var key = (await created.Content.ReadFromJsonAsync<CreatedApiKeyResponse>())!;
        string[] secrets = ["test-management-key", "test-evaluation-key", key.Key];
        store.Failure = new InvalidOperationException("Sensitive diagnostic: " + string.Join(' ', secrets));

        foreach (var secret in secrets)
        {
            using var client = application.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", secret);
            client.DefaultRequestHeaders.Accept.ParseAdd(accept);
            if (secret != "test-management-key")
            {
                using var forbidden = await client.GetAsync("/api/keys");
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                Assert.Empty(await forbidden.Content.ReadAsByteArrayAsync());
            }

            using var failed = await client.GetAsync("/api/flags/checkout/enabled");
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.Equal("application/problem+json", failed.Content.Headers.ContentType?.MediaType);
            var body = await failed.Content.ReadAsStringAsync();
            Assert.All(secrets, value => Assert.DoesNotContain(value, body));
            var problem = await failed.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Equal("An unexpected error occurred while processing the request.", problem!.Detail);
            Assert.True(problem.Extensions.ContainsKey("traceId"));

            using var malformed = await client.PostAsync("/api/flags/checkout/evaluate", new StringContent("{\"userId\":\"" + secret + "\",", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            var error = await malformed.Content.ReadAsStringAsync();
            Assert.All(secrets, value => Assert.DoesNotContain(value, error));
        }

        var listing = await management.GetStringAsync("/api/keys");
        Assert.DoesNotContain(key.Key, listing);
        using var revoked = await management.DeleteAsync($"/api/keys/{key.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var revokedClient = application.CreateClient();
        revokedClient.DefaultRequestHeaders.Add("X-Api-Key", key.Key);
        using var denied = await revokedClient.GetAsync("/api/flags/checkout/enabled");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Empty(await denied.Content.ReadAsByteArrayAsync());
        Assert.Contains(denied.Headers.WwwAuthenticate, header => header.Scheme == "ApiKey");
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.All(logs.Entries, entry => Assert.All(secrets, secret => Assert.DoesNotContain(secret, entry.Message)));
    }

    [Theory]
    [InlineData("Development", 408)]
    [InlineData("Development", 413)]
    [InlineData("Production", 408)]
    [InlineData("Production", 413)]
    public async Task FrameworkRequestExceptionsKeepTheirStatusWithoutExposingDiagnostics(string environment, int status)
    {
        using var logs = new CapturedLogs();
        var store = new FailingStore { Failure = new BadHttpRequestException("Sensitive request diagnostic", status) };
        using var application = CreateApplication(environment, store, logs);
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-management-key");
        using var response = await client.GetAsync("/api/flags");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(status, problem!.Status);
        Assert.Equal("The request could not be processed.", problem.Detail);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    private WebApplicationFactory<Program> CreateApplication(string environment, FailingStore store, CapturedLogs logs)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs));
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<ApiKeyOptions>(options =>
                {
                    options.ManagementKey = "test-management-key";
                    options.EvaluationKey = "test-evaluation-key";
                });
                services.RemoveAll<IFeatureFlagStore>();
                services.AddSingleton<IFeatureFlagStore>(store);
                services.RemoveAll<IDbContextOptionsConfiguration<FlagbitDbContext>>();
                services.RemoveAll<DbContextOptions<FlagbitDbContext>>();
                services.RemoveAll<FlagbitDbContext>();
                services.AddDbContext<FlagbitDbContext>(options => options.UseNpgsql(postgreSql.ConnectionString));
            });
        });
    }

    private sealed class FailingStore : IFeatureFlagStore
    {
        public Exception Failure { get; set; } = new InvalidOperationException();

        public ValueTask<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) => throw Failure;

        public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default) => throw Failure;

        public ValueTask AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<FeatureFlag> UpdateEvaluationAsync(FeatureFlag flag, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
