using System.Data.Common;
using System.Net.Http.Json;
using Flagbit.Api.Authentication;
using Flagbit.Core.Models;
using Flagbit.Infrastructure;
using Flagbit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Flagbit.Api.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ApiCancellationTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    public Task InitializeAsync() => postgreSql.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("GET", "/api/flags", "SELECT")]
    [InlineData("GET", "/api/flags/checkout", "SELECT")]
    [InlineData("GET", "/api/flags/checkout/enabled", "SELECT")]
    [InlineData("POST", "/api/flags/checkout/evaluate", "SELECT")]
    [InlineData("POST", "/api/flags", "INSERT")]
    [InlineData("PUT", "/api/flags/checkout/evaluation", "UPDATE")]
    [InlineData("PUT", "/api/flags/checkout/enable", "UPDATE")]
    [InlineData("PUT", "/api/flags/checkout/disable", "UPDATE")]
    [InlineData("DELETE", "/api/flags/checkout", "DELETE")]
    public async Task EveryFlagEndpointForwardsRequestCancellationToDatabaseCommands(string method, string path, string commandPrefix)
    {
        await using var setup = postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(setup);
        await store.AddAsync(new FeatureFlag("checkout", true, ["original"], 50));
        using var logs = new CapturedLogs();
        var observer = new CancellationObserver(commandPrefix, holdCommand: true);
        using var application = CreateApplication(observer, logs);
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-management-key");
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { key = "new-flag" }) };
        var pending = client.SendAsync(request, cancellation.Token);
        try
        {
            await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            await observer.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("An unhandled", StringComparison.Ordinal));
        }
        finally
        {
            cancellation.Cancel();
            observer.Release.TrySetResult();
        }

        var retained = Assert.Single(await store.GetAllAsync());
        Assert.True(retained.IsEnabled);
        Assert.Equal(["original"], retained.TargetedUserIds);
        Assert.Equal(50, retained.RolloutPercentage);
    }

    [Fact]
    public async Task CancellingHttpRequestInterruptsPostgreSqlLockWait()
    {
        await using var setup = postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(setup);
        await store.AddAsync(new FeatureFlag("checkout", true));
        await using var blocker = new NpgsqlConnection(postgreSql.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM feature_flags WHERE key = 'checkout' FOR UPDATE", blocker, transaction);
        await command.ExecuteScalarAsync();
        using var logs = new CapturedLogs();
        var observer = new CancellationObserver("UPDATE", holdCommand: false);
        using var application = CreateApplication(observer, logs);
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-management-key");
        using var cancellation = new CancellationTokenSource();
        var pending = client.PutAsync("/api/flags/checkout/disable", null, cancellation.Token);
        try
        {
            await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await setup.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
                    AND wait_event_type = 'Lock' AND query LIKE 'UPDATE feature_flags%') AS "Value"
                """).SingleAsync(timeout.Token))
            {
                await Task.Delay(20, timeout.Token);
            }

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            await observer.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("An unhandled", StringComparison.Ordinal));
        }
        finally
        {
            cancellation.Cancel();
            await transaction.RollbackAsync();
        }

        Assert.True((await store.GetByKeyAsync("checkout"))!.IsEnabled);
    }

    private WebApplicationFactory<Program> CreateApplication(CancellationObserver observer, CapturedLogs logs)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs));
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<ApiKeyOptions>(options =>
                {
                    options.ManagementKey = "test-management-key";
                    options.EvaluationKey = "test-evaluation-key";
                });
                services.AddHttpContextAccessor();
                services.RemoveAll<IDbContextOptionsConfiguration<FlagbitDbContext>>();
                services.RemoveAll<DbContextOptions<FlagbitDbContext>>();
                services.RemoveAll<FlagbitDbContext>();
                services.AddDbContext<FlagbitDbContext>((provider, options) =>
                {
                    observer.HttpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
                    options.UseNpgsql(postgreSql.ConnectionString, postgres => postgres.CommandTimeout(15)).AddInterceptors(observer);
                });
            });
        });
    }

    private sealed class CancellationObserver(string commandPrefix, bool holdCommand) : DbCommandInterceptor
    {
        public IHttpContextAccessor HttpContextAccessor { get; set; } = null!;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await ObserveAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await ObserveAsync(command, cancellationToken);
            return result;
        }

        public override Task CommandCanceledAsync(DbCommand command, CommandEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Cancelled.TrySetResult();
            return Task.CompletedTask;
        }

        private async Task ObserveAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!command.CommandText.StartsWith(commandPrefix, StringComparison.Ordinal))
            {
                return;
            }

            Assert.True(cancellationToken.CanBeCanceled);
            Assert.Equal(HttpContextAccessor.HttpContext!.RequestAborted, cancellationToken);
            Entered.TrySetResult();
            if (holdCommand)
            {
                try
                {
                    await Release.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Cancelled.TrySetResult();
                    throw;
                }
            }
        }
    }
}
