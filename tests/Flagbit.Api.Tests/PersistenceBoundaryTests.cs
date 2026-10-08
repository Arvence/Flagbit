using Flagbit.Core.Abstractions;
using Flagbit.Core.Exceptions;
using Flagbit.Core.Models;
using Flagbit.Core.Services;
using Flagbit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flagbit.Api.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PersistenceBoundaryTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSql;

    public PersistenceBoundaryTests(PostgreSqlFixture postgreSql)
    {
        _postgreSql = postgreSql;
    }

    public Task InitializeAsync() => _postgreSql.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterleavedManagementOperationsPreserveUnrelatedFields(bool stateChangeIsDelayed)
    {
        await using var first = _postgreSql.CreateDbContext();
        await using var second = _postgreSql.CreateDbContext();
        var firstStore = new FeatureFlagStore(first);
        var secondManager = new FeatureFlagManager(new FeatureFlagStore(second));
        await firstStore.AddAsync(new FeatureFlag("checkout", false, ["old-user"], 25));
        first.ChangeTracker.Clear();
        var interleavingStore = new InterleavingStore(firstStore, async () =>
        {
            if (stateChangeIsDelayed)
            {
                await secondManager.UpdateEvaluationAsync("checkout", ["new-user"], 75);
            }
            else
            {
                await secondManager.EnableAsync("checkout");
            }
        });
        var firstManager = new FeatureFlagManager(interleavingStore);

        if (stateChangeIsDelayed)
        {
            await firstManager.EnableAsync("checkout");
        }
        else
        {
            await firstManager.UpdateEvaluationAsync("checkout", ["new-user"], 75);
        }

        await using var verification = _postgreSql.CreateDbContext();
        var flag = await new FeatureFlagStore(verification).GetByKeyAsync("checkout");
        Assert.NotNull(flag);
        Assert.True(flag.IsEnabled);
        Assert.Equal(["new-user"], flag.TargetedUserIds);
        Assert.Equal(75, flag.RolloutPercentage);
    }

    [Theory]
    [InlineData("checkout")]
    [InlineData("caf\u00e9")]
    [InlineData("\u0131stanbul")]
    [InlineData("\u0130stanbul")]
    [InlineData("stra\u00dfe")]
    [InlineData("\u03c2\u03c3")]
    [InlineData("\u017f")]
    [InlineData("\U00010428")]
    public async Task StoredNormalizationMatchesApplicationNormalization(string key)
    {
        await using var database = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(database);
        var dependency = "dependency-" + key;
        await store.AddAsync(new FeatureFlag(dependency, true));
        await store.AddAsync(new FeatureFlag(key, true, [key], 100, [key], dependencyKeys: [dependency]));
        var normalized = await database.Database.SqlQuery<string>($"SELECT normalized_key AS \"Value\" FROM feature_flags WHERE key = {key}").SingleAsync();

        Assert.Equal(FeatureFlagIdentifier.Normalize(key), normalized);
        Assert.NotNull(await store.GetByKeyAsync(key));
        Assert.Equal(key, (await store.GetByKeyAsync(FeatureFlagIdentifier.Normalize(key)))?.Key);
        var normalizedUser = await database.Database.SqlQuery<string>($"SELECT normalized_user_id AS \"Value\" FROM feature_flag_target_users WHERE user_id = {key}").SingleAsync();
        var normalizedEnvironment = await database.Database.SqlQuery<string>($"SELECT normalized_name AS \"Value\" FROM feature_flag_environments WHERE name = {key}").SingleAsync();
        var normalizedDependency = await database.Database.SqlQuery<string>($"SELECT normalized_dependency_key AS \"Value\" FROM feature_flag_dependencies WHERE dependency_key = {dependency}").SingleAsync();
        Assert.Equal(normalized, normalizedUser);
        Assert.Equal(normalized, normalizedEnvironment);
        Assert.Equal(FeatureFlagIdentifier.Normalize(dependency), normalizedDependency);
        Assert.True(await new FeatureFlagEvaluator(store).IsEnabledAsync(key, new FeatureFlagContext(FeatureFlagIdentifier.Normalize(key), FeatureFlagIdentifier.Normalize(key))));
        await Assert.ThrowsAsync<FeatureFlagAlreadyExistsException>(async () => await store.AddAsync(new FeatureFlag(FeatureFlagIdentifier.Normalize(key), false)));
    }

    [Fact]
    public async Task OffsetScheduleCanBePersisted()
    {
        await using var database = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(database);
        var start = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(3));
        await store.AddAsync(new FeatureFlag("scheduled", true, startsAt: start, endsAt: start.AddHours(1)));

        var flag = await store.GetByKeyAsync("scheduled");
        Assert.Equal(start.ToUniversalTime(), flag?.StartsAt);
        Assert.Equal(TimeSpan.Zero, flag?.StartsAt?.Offset);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-4)]
    public async Task OffsetScheduleUpdatesRespectPersistedMicrosecondBoundaries(int offsetHours)
    {
        await using var database = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(database);
        var start = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(offsetHours)).AddTicks(17);
        await store.AddAsync(new FeatureFlag("scheduled", true));
        var manager = new FeatureFlagManager(store);
        await manager.UpdateEvaluationAsync("scheduled", null, null, startsAt: start, endsAt: start.AddTicks(25));

        var flag = await store.GetByKeyAsync("scheduled");
        var persistedStart = start.ToUniversalTime().AddTicks(-7);
        var persistedEnd = start.AddTicks(25).ToUniversalTime().AddTicks(-2);
        Assert.Equal(persistedStart, flag?.StartsAt);
        Assert.Equal(persistedEnd, flag?.EndsAt);
        Assert.Equal(TimeSpan.Zero, flag?.StartsAt?.Offset);
        var evaluator = new FeatureFlagEvaluator(store);
        Assert.False(await evaluator.IsEnabledAsync("scheduled", new FeatureFlagContext(CurrentTime: persistedStart.AddTicks(-1))));
        Assert.True(await evaluator.IsEnabledAsync("scheduled", new FeatureFlagContext(CurrentTime: persistedStart.ToOffset(TimeSpan.FromHours(7)))));
        Assert.True(await evaluator.IsEnabledAsync("scheduled", new FeatureFlagContext(CurrentTime: persistedEnd)));
        Assert.False(await evaluator.IsEnabledAsync("scheduled", new FeatureFlagContext(CurrentTime: persistedEnd.AddTicks(1))));
    }

    [Fact]
    public async Task StateTogglesLeaveOwnedRowsUnchanged()
    {
        await using var database = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(database);
        await store.AddAsync(DetailedFlag());
        var before = await ReadChildRowsAsync(database);

        await store.SetEnabledAsync("CHECKOUT", false);
        await store.SetEnabledAsync("checkout", false);
        await store.SetEnabledAsync("checkout", true);

        Assert.Equal(before, await ReadChildRowsAsync(database));
    }

    [Fact]
    public async Task SettingsReplacementRetainsReordersAndClearsChildren()
    {
        await using var database = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(database);
        await store.AddAsync(DetailedFlag());
        var manager = new FeatureFlagManager(store);
        var rules = new[] { new FeatureFlagRule("region", FeatureFlagRuleOperator.StartsWith, "eu-"), new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, "enterprise") };

        for (var iteration = 0; iteration < 2; iteration++)
        {
            var result = await manager.UpdateEvaluationAsync("checkout", ["retained", "new-user"], 75, ["staging", "new-environment"], rules, dependencyKeys: ["accounts", "new-dependency"]);
            Assert.True(result.IsEnabled);
            Assert.Equal(["retained", "new-user"], result.TargetedUserIds);
            Assert.Equal(["staging", "new-environment"], result.Environments);
            Assert.Equal(rules, result.Rules);
            Assert.Equal(["accounts", "new-dependency"], result.DependencyKeys);
            Assert.Equal(8, (await ReadChildRowsAsync(database)).Length);
            var positions = await database.Database.SqlQueryRaw<string>("SELECT position::text || ':' || attribute AS \"Value\" FROM feature_flag_rules ORDER BY position").ToArrayAsync();
            Assert.Equal(iteration == 0 ? ["0:region", "1:plan"] : new[] { "0:plan", "1:region" }, positions);
            rules = rules.Reverse().ToArray();
        }

        var cleared = await manager.UpdateEvaluationAsync("checkout", null, null);
        Assert.True(cleared.IsEnabled);
        Assert.Empty(await ReadChildRowsAsync(database));
        Assert.Empty(cleared.TargetedUserIds);
        Assert.Empty(cleared.Environments);
        Assert.Empty(cleared.Rules);
        Assert.Empty(cleared.DependencyKeys);
        Assert.Null(cleared.RolloutPercentage);
        Assert.Null(cleared.StartsAt);
        Assert.Null(cleared.EndsAt);
    }

    [Fact]
    public async Task FailedReplacementRollsBackAndDoesNotMisreportChildUniqueness()
    {
        await using var database = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(database);
        await store.AddAsync(DetailedFlag());
        var before = await ReadChildRowsAsync(database);
        await database.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX ux_test_rule_attribute ON feature_flag_rules (feature_flag_id, attribute)");

        var exception = await Assert.ThrowsAsync<DbUpdateException>(async () => await new FeatureFlagManager(store).UpdateEvaluationAsync("checkout", ["replacement"], 10,
            rules: [new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, "enterprise"), new FeatureFlagRule("plan", FeatureFlagRuleOperator.NotEquals, "free")]));

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("ux_test_rule_attribute", postgres.ConstraintName);
        await using var verification = _postgreSql.CreateDbContext();
        Assert.Equal(before, await ReadChildRowsAsync(verification));
        var flag = await new FeatureFlagStore(verification).GetByKeyAsync("checkout");
        Assert.True(flag?.IsEnabled);
        Assert.Equal(50, flag?.RolloutPercentage);
        Assert.Equal(DetailedFlag().StartsAt, flag?.StartsAt);
    }

    [Fact]
    public async Task DeletionCascadesOwnedRowsAndKeepsIncomingDependencies()
    {
        await using var database = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(database);
        await store.AddAsync(DetailedFlag());
        await store.AddAsync(new FeatureFlag("dependent", true, dependencyKeys: ["CHECKOUT"]));

        Assert.True(await store.DeleteAsync("checkout"));

        var remaining = Assert.Single(await ReadChildRowsAsync(database));
        Assert.StartsWith("dependency:", remaining);
        Assert.Contains("CHECKOUT", remaining);
        Assert.False(await new FeatureFlagEvaluator(store).IsEnabledAsync("dependent"));
        Assert.Equal(["CHECKOUT"], (await store.GetByKeyAsync("dependent"))!.DependencyKeys);
    }

    [Fact]
    public async Task ConcurrentDuplicateCreationHasOneWinner()
    {
        await using var first = _postgreSql.CreateDbContext();
        await using var second = _postgreSql.CreateDbContext();
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readers = 0;
        async Task AfterRead()
        {
            if (Interlocked.Increment(ref readers) == 2)
            {
                bothRead.SetResult();
            }

            await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var firstManager = new FeatureFlagManager(new InterleavingStore(new FeatureFlagStore(first), AfterRead));
        var secondManager = new FeatureFlagManager(new InterleavingStore(new FeatureFlagStore(second), AfterRead));
        var results = await Task.WhenAll(Record.ExceptionAsync(async () => await firstManager.CreateAsync("checkout")), Record.ExceptionAsync(async () => await secondManager.CreateAsync("CHECKOUT")));

        Assert.Single(results, exception => exception is null);
        Assert.IsType<FeatureFlagAlreadyExistsException>(Assert.Single(results, exception => exception is not null));
        Assert.Single(await new FeatureFlagStore(first).GetAllAsync());
    }

    [Fact]
    public async Task DotlessAndAsciiIdentifiersRemainDistinct()
    {
        await using var database = _postgreSql.CreateDbContext();
        var store = new FeatureFlagStore(database);
        await store.AddAsync(new FeatureFlag("i", false));
        await store.AddAsync(new FeatureFlag("\u0131", true));
        await store.AddAsync(new FeatureFlag("checkout", true, ["i", "\u0131"], environments: ["i", "\u0131"], dependencyKeys: ["i", "\u0131"]));

        var flag = await store.GetByKeyAsync("checkout");
        Assert.Equal(2, flag!.TargetedUserIds.Count);
        Assert.Equal(2, flag.Environments.Count);
        Assert.Equal(2, flag.DependencyKeys.Count);
        Assert.False((await store.GetByKeyAsync("I"))!.IsEnabled);
        Assert.True((await store.GetByKeyAsync("\u0131"))!.IsEnabled);
        Assert.False(await new FeatureFlagEvaluator(store).IsEnabledAsync("checkout", new FeatureFlagContext("i", "i")));
    }

    private static FeatureFlag DetailedFlag()
    {
        return new FeatureFlag("checkout", true, ["old-user", "retained"], 50, ["production", "staging"],
            [new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, "enterprise"), new FeatureFlagRule("region", FeatureFlagRuleOperator.StartsWith, "eu-")],
            new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero), dependencyKeys: ["accounts", "old-dependency"]);
    }

    private static Task<string[]> ReadChildRowsAsync(Flagbit.Infrastructure.Persistence.FlagbitDbContext database)
    {
        return database.Database.SqlQueryRaw<string>("""
            SELECT 'user:' || row_to_json(t)::text AS "Value" FROM feature_flag_target_users AS t
            UNION ALL SELECT 'environment:' || row_to_json(e)::text FROM feature_flag_environments AS e
            UNION ALL SELECT 'rule:' || row_to_json(r)::text FROM feature_flag_rules AS r
            UNION ALL SELECT 'dependency:' || row_to_json(d)::text FROM feature_flag_dependencies AS d
            ORDER BY "Value"
            """).ToArrayAsync();
    }

    private sealed class InterleavingStore : IFeatureFlagStore
    {
        private readonly IFeatureFlagStore _inner;
        private readonly Func<Task> _afterRead;

        public InterleavingStore(IFeatureFlagStore inner, Func<Task> afterRead)
        {
            _inner = inner;
            _afterRead = afterRead;
        }

        public async ValueTask<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            var flag = await _inner.GetByKeyAsync(key, cancellationToken);
            await _afterRead();
            return flag;
        }

        public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default) => _inner.GetAllAsync(cancellationToken);

        public ValueTask AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default) => _inner.AddAsync(flag, cancellationToken);

        public async ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled, CancellationToken cancellationToken = default)
        {
            await _afterRead();
            return await _inner.SetEnabledAsync(key, isEnabled, cancellationToken);
        }

        public ValueTask<FeatureFlag> UpdateEvaluationAsync(FeatureFlag flag, CancellationToken cancellationToken = default) => _inner.UpdateEvaluationAsync(flag, cancellationToken);

        public ValueTask<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => _inner.DeleteAsync(key, cancellationToken);
    }
}
