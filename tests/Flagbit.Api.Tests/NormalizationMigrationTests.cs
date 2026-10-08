using Flagbit.Core.Models;
using Flagbit.Core.Services;
using Flagbit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Flagbit.Api.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class NormalizationMigrationTests
{
    private readonly PostgreSqlFixture _postgreSql;

    public NormalizationMigrationTests(PostgreSqlFixture postgreSql)
    {
        _postgreSql = postgreSql;
    }

    [Theory]
    [InlineData("\u0131stanbul", "ISTANBUL")]
    [InlineData("\u017f", "S")]
    public async Task UpgradeBackfillsUnicodeIdentitiesWithoutReplacingExistingRows(string identifier, string distinctIdentifier)
    {
        await using var database = _postgreSql.CreateDbContext();
        await database.Database.EnsureDeletedAsync();
        await database.GetService<IMigrator>().MigrateAsync("20260909112642_AddEvaluationApiKeys");
        await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO feature_flags (key, is_enabled) VALUES ({identifier}, true), ('dependent', true)");
        await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO feature_flag_target_users (feature_flag_id, user_id) SELECT id, {identifier} FROM feature_flags WHERE key = 'dependent'");
        await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO feature_flag_environments (feature_flag_id, name) SELECT id, {identifier} FROM feature_flags WHERE key = 'dependent'");
        await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO feature_flag_dependencies (feature_flag_id, dependency_key) SELECT id, {identifier} FROM feature_flags WHERE key = 'dependent'");
        var before = await ReadOriginalRowsAsync(database);
        var previousNormalized = await database.Database.SqlQuery<string>($"SELECT normalized_key AS \"Value\" FROM feature_flags WHERE key = {identifier}").SingleAsync();
        Assert.NotEqual(FeatureFlagIdentifier.Normalize(identifier), previousNormalized);

        await database.Database.MigrateAsync();

        Assert.Equal(before, await ReadOriginalRowsAsync(database));
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        Assert.False(database.Database.HasPendingModelChanges());
        var normalized = await database.Database.SqlQueryRaw<string>("""
            SELECT normalized_key AS "Value" FROM feature_flags WHERE key <> 'dependent'
            UNION ALL SELECT normalized_user_id FROM feature_flag_target_users
            UNION ALL SELECT normalized_name FROM feature_flag_environments
            UNION ALL SELECT normalized_dependency_key FROM feature_flag_dependencies
            """).ToArrayAsync();
        Assert.Equal(4, normalized.Length);
        Assert.All(normalized, value => Assert.Equal(FeatureFlagIdentifier.Normalize(identifier), value));
        var store = new FeatureFlagStore(database);
        Assert.NotNull(await store.GetByKeyAsync(identifier));
        Assert.True(await new FeatureFlagEvaluator(store).IsEnabledAsync("dependent", new FeatureFlagContext(identifier, identifier)));
        await store.AddAsync(new FeatureFlag(distinctIdentifier, false));
        Assert.False((await store.GetByKeyAsync(distinctIdentifier))!.IsEnabled);
        Assert.True((await store.GetByKeyAsync(identifier))!.IsEnabled);
        await database.Database.MigrateAsync();
        Assert.True(await new FeatureFlagEvaluator(store).IsEnabledAsync("dependent", new FeatureFlagContext(identifier, identifier)));
    }

    private static Task<string[]> ReadOriginalRowsAsync(Flagbit.Infrastructure.Persistence.FlagbitDbContext database)
    {
        return database.Database.SqlQueryRaw<string>("""
            SELECT 'flag:' || (to_jsonb(f) - 'normalized_key')::text AS "Value" FROM feature_flags AS f
            UNION ALL SELECT 'user:' || (to_jsonb(t) - 'normalized_user_id')::text FROM feature_flag_target_users AS t
            UNION ALL SELECT 'environment:' || (to_jsonb(e) - 'normalized_name')::text FROM feature_flag_environments AS e
            UNION ALL SELECT 'dependency:' || (to_jsonb(d) - 'normalized_dependency_key')::text FROM feature_flag_dependencies AS d
            ORDER BY "Value"
            """).ToArrayAsync();
    }
}
