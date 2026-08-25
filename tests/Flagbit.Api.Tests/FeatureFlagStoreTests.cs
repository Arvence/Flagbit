using Flagbit.Core.Exceptions;
using Flagbit.Core.Models;
using Flagbit.Infrastructure;

namespace Flagbit.Api.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class FeatureFlagStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSql;

    public FeatureFlagStoreTests(PostgreSqlFixture postgreSql)
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
    public async Task CrudOperationsPreserveRulesAndDependencies()
    {
        var startsAt = new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
        var endsAt = startsAt.AddDays(1);

        await using (var context = _postgreSql.CreateDbContext())
        {
            var store = new FeatureFlagStore(context);
            var flag = new FeatureFlag(
                "new-checkout",
                true,
                ["user-123"],
                50,
                ["production"],
                [new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, "enterprise")],
                startsAt,
                endsAt,
                ["accounts"]);

            await store.AddAsync(flag);
        }

        await using (var context = _postgreSql.CreateDbContext())
        {
            var store = new FeatureFlagStore(context);
            var flag = await store.GetByKeyAsync("NEW-CHECKOUT");

            Assert.NotNull(flag);
            Assert.Equal(["user-123"], flag.TargetedUserIds);
            Assert.Equal(["production"], flag.Environments);
            Assert.Equal([new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, "enterprise")], flag.Rules);
            Assert.Equal(["accounts"], flag.DependencyKeys);

            flag.Disable();
            flag.ConfigureEvaluation(
                ["user-456"],
                25,
                ["staging"],
                [new FeatureFlagRule("country", FeatureFlagRuleOperator.Equals, "TR")],
                dependencyKeys: ["recommendations"]);
            await store.UpdateAsync(flag);
        }

        await using (var context = _postgreSql.CreateDbContext())
        {
            var store = new FeatureFlagStore(context);
            var flag = await store.GetByKeyAsync("new-checkout");

            Assert.NotNull(flag);
            Assert.False(flag.IsEnabled);
            Assert.Equal(["user-456"], flag.TargetedUserIds);
            Assert.Equal(25, flag.RolloutPercentage);
            Assert.Equal(["staging"], flag.Environments);
            Assert.Equal([new FeatureFlagRule("country", FeatureFlagRuleOperator.Equals, "TR")], flag.Rules);
            Assert.Equal(["recommendations"], flag.DependencyKeys);
            Assert.True(await store.DeleteAsync("NEW-CHECKOUT"));
            Assert.Null(await store.GetByKeyAsync("new-checkout"));
        }
    }

    [Fact]
    public async Task DuplicateKeysAreRejectedCaseInsensitively()
    {
        await using var context = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(context);

        await store.AddAsync(new FeatureFlag("new-checkout", false));

        var exception = await Assert.ThrowsAsync<FeatureFlagAlreadyExistsException>(async () =>
            await store.AddAsync(new FeatureFlag("NEW-CHECKOUT", true)));

        Assert.Equal("NEW-CHECKOUT", exception.Key);
    }
}
