extern alias FlagbitCli;

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
    public async Task CreateEnableEvaluateDisableEvaluateWorks()
    {
        using var api = CreateApi();
        using var httpClient = api.CreateClient();
        var cli = new CliApplication(new FlagbitApiClient(httpClient));

        await AssertCommandAsync(cli, "Created new-checkout (disabled).", "create", "new-checkout");
        await AssertCommandAsync(cli, "new-checkout is enabled.", "enable", "new-checkout");
        await AssertCommandAsync(cli, "new-checkout is enabled.", "evaluate", "new-checkout");
        await AssertCommandAsync(cli, "new-checkout is disabled.", "disable", "new-checkout");
        await AssertCommandAsync(cli, "new-checkout is disabled.", "evaluate", "new-checkout");
    }

    private WebApplicationFactory<Program> CreateApi()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<FlagbitDbContext>>();
                services.RemoveAll<DbContextOptions<FlagbitDbContext>>();
                services.RemoveAll<FlagbitDbContext>();
                services.AddDbContext<FlagbitDbContext>(options => options.UseNpgsql(_postgreSql.ConnectionString));
            });
        });
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
