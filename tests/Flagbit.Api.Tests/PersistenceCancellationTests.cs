using Flagbit.Core.Models;
using Flagbit.Infrastructure;
using Flagbit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flagbit.Api.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PersistenceCancellationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSql;

    public PersistenceCancellationTests(PostgreSqlFixture postgreSql)
    {
        _postgreSql = postgreSql;
    }

    public Task InitializeAsync() => _postgreSql.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AlreadyCancelledOperationsDoNotChangeStoredData()
    {
        await using var database = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(database);
        await store.AddAsync(new FeatureFlag("checkout", true, ["original"], 50));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.GetByKeyAsync("checkout", token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.GetAllAsync(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.AddAsync(new FeatureFlag("cancelled", true), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.SetEnabledAsync("checkout", false, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.UpdateEvaluationAsync(new FeatureFlag("checkout", false), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.DeleteAsync("checkout", token));

        var retained = Assert.Single(await store.GetAllAsync());
        Assert.True(retained.IsEnabled);
        Assert.Equal(["original"], retained.TargetedUserIds);
        Assert.Equal(50, retained.RolloutPercentage);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("evaluation")]
    [InlineData("delete")]
    public async Task CancellationInterruptsAWriteWaitingForADatabaseLock(string operation)
    {
        await using var setup = _postgreSql.CreateDbContext();
        await new FeatureFlagStore(setup).AddAsync(new FeatureFlag("checkout", true, ["original"], 50));
        await using var blocker = new NpgsqlConnection(_postgreSql.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM feature_flags WHERE key = 'checkout' FOR UPDATE", blocker, transaction);
        await command.ExecuteScalarAsync();
        var options = new DbContextOptionsBuilder<FlagbitDbContext>().UseNpgsql(_postgreSql.ConnectionString, options => options.CommandTimeout(5)).Options;
        await using var waiting = new FlagbitDbContext(options);
        var store = new FeatureFlagStore(waiting);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (operation == "state")
            {
                await store.SetEnabledAsync("checkout", false, cancellation.Token);
            }
            else if (operation == "evaluation")
            {
                await store.UpdateEvaluationAsync(new FeatureFlag("checkout", false), cancellation.Token);
            }
            else
            {
                await store.DeleteAsync("checkout", cancellation.Token);
            }
        });

        await transaction.RollbackAsync();
        var flag = await new FeatureFlagStore(setup).GetByKeyAsync("checkout");
        Assert.True(flag?.IsEnabled);
        Assert.Equal(["original"], flag!.TargetedUserIds);
        Assert.Equal(50, flag.RolloutPercentage);
    }
}
