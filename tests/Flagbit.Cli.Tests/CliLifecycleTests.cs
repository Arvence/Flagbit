extern alias FlagbitCli;

using System.Net;
using System.Net.Http.Json;
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
using CliApplication = FlagbitCli::Flagbit.Cli.CliApplication;
using FlagbitApiClient = FlagbitCli::Flagbit.Cli.Api.FlagbitApiClient;

namespace Flagbit.Cli.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CliLifecycleTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSql;

    public CliLifecycleTests(PostgreSqlFixture postgreSql)
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
    public async Task CreateGetEnableEvaluateDisableDeleteWorks()
    {
        using var api = CreateApi();
        using var httpClient = api.CreateClient();
        var cli = new CliApplication(new FlagbitApiClient(httpClient, "test-management-key"));

        await AssertCommandAsync(cli, "Created new-checkout (disabled).", "create", "new-checkout");
        await AssertCommandAsync(cli, "new-checkout is disabled.", "get", "NEW-CHECKOUT");
        await AssertCommandAsync(cli, "new-checkout is enabled.", "enable", "new-checkout");
        await AssertCommandAsync(cli, "new-checkout is enabled.", "evaluate", "new-checkout");
        await AssertCommandAsync(cli, "new-checkout is disabled.", "disable", "new-checkout");
        await AssertCommandAsync(cli, "new-checkout is disabled.", "evaluate", "new-checkout");
        await AssertCommandAsync(cli, "Deleted new-checkout.", "delete", "new-checkout");

        var deletedResponse = await httpClient.GetAsync("/api/flags/new-checkout");
        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
    }

    [Fact]
    public async Task EvaluateAcceptsUserEnvironmentAndAttributes()
    {
        using var api = CreateApi();
        using var httpClient = api.CreateClient();
        var cli = new CliApplication(new FlagbitApiClient(httpClient, "test-management-key"));
        var scheduleAnchor = DateTimeOffset.UtcNow;
        scheduleAnchor = scheduleAnchor.AddTicks(-(scheduleAnchor.Ticks % TimeSpan.TicksPerSecond));
        var startsAt = scheduleAnchor.AddMinutes(-5);
        var endsAt = scheduleAnchor.AddMinutes(5);

        var dependencyResponse = await httpClient.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("accounts", true));
        Assert.Equal(HttpStatusCode.Created, dependencyResponse.StatusCode);
        var featureResponse = await httpClient.PostAsJsonAsync("/api/flags", new CreateFeatureFlagRequest("advanced-checkout", true, ["user-123"], 100, ["production"], [new FeatureFlagRuleRequest("plan", "Equals", "enterprise")], startsAt, endsAt, ["accounts"]));
        Assert.Equal(HttpStatusCode.Created, featureResponse.StatusCode);

        await AssertCommandAsync(cli, "advanced-checkout is enabled.", "evaluate", "advanced-checkout", "--user", "user-123", "--environment", "production", "--attribute", "plan=enterprise");
        await AssertCommandAsync(cli, "advanced-checkout is disabled.", "evaluate", "advanced-checkout", "--user", "user-123", "--environment", "production", "--attribute", "plan=free");
    }

    [Fact]
    public async Task EvaluationKeyCanEvaluateButCannotChangeFlags()
    {
        using var api = CreateApi();
        using var managementClient = api.CreateClient();
        var managementCli = new CliApplication(new FlagbitApiClient(managementClient, "test-management-key"));
        await AssertCommandAsync(managementCli, "Created protected-flag (disabled).", "create", "protected-flag");
        await AssertCommandAsync(managementCli, "protected-flag is enabled.", "enable", "protected-flag");

        using var evaluationClient = api.CreateClient();
        var evaluationCli = new CliApplication(new FlagbitApiClient(evaluationClient, "test-evaluation-key"));

        await AssertCommandAsync(evaluationCli, "protected-flag is enabled.", "evaluate", "protected-flag");
        await AssertFailedCommandAsync(evaluationCli, "API access denied. This command requires a management API key.", "disable", "protected-flag");
        await AssertCommandAsync(evaluationCli, "protected-flag is enabled.", "evaluate", "protected-flag");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-key")]
    public async Task MissingOrInvalidKeyProducesAnActionableError(string? apiKey)
    {
        using var api = CreateApi();
        using var httpClient = api.CreateClient();
        var cli = new CliApplication(new FlagbitApiClient(httpClient, apiKey));

        await AssertFailedCommandAsync(cli, "API authentication failed. Set FLAGBIT_API_KEY to a valid API key.", "list");
    }

    private WebApplicationFactory<Program> CreateApi()
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

    private static async Task AssertFailedCommandAsync(CliApplication cli, string expectedError, params string[] args)
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);

        try
        {
            Assert.Equal(1, await cli.RunAsync(args));
            Assert.Equal(expectedError, error.ToString().Trim());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private static async Task AssertCommandAsync(CliApplication cli, string expectedOutput, params string[] args)
    {
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);

        try
        {
            Assert.Equal(0, await cli.RunAsync(args));
            Assert.Equal(expectedOutput, output.ToString().Trim());
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }
}
