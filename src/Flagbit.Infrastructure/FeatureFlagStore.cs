using Flagbit.Core.Abstractions;
using Flagbit.Core.Exceptions;
using Flagbit.Core.Models;
using Flagbit.Infrastructure.Persistence;
using Flagbit.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flagbit.Infrastructure;

public sealed class FeatureFlagStore : IFeatureFlagStore
{
    private readonly FlagbitDbContext _dbContext;

    public FeatureFlagStore(FlagbitDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async ValueTask<FeatureFlag?> GetByKeyAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var normalizedKey = FeatureFlagIdentifier.Normalize(key);
        var entity = await FeatureFlagsWithDetails()
            .AsNoTracking()
            .SingleOrDefaultAsync(featureFlag => featureFlag.NormalizedKey == normalizedKey);

        return entity is null ? null : FeatureFlagEntityMapper.ToDomain(entity);
    }

    public async ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync()
    {
        var entities = await FeatureFlagsWithDetails()
            .AsNoTracking()
            .OrderBy(featureFlag => featureFlag.NormalizedKey)
            .ThenBy(featureFlag => featureFlag.Key)
            .ToArrayAsync();

        return entities
            .Select(FeatureFlagEntityMapper.ToDomain)
            .ToArray();
    }

    public async ValueTask AddAsync(FeatureFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        await _dbContext.FeatureFlags.AddAsync(FeatureFlagEntityMapper.ToEntity(flag));
        await SaveChangesAsync(flag.Key);
    }

    public async ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled)
    {
        var normalizedKey = FeatureFlagIdentifier.Normalize(key);
        var affected = await _dbContext.FeatureFlags.Where(flag => flag.NormalizedKey == normalizedKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(flag => flag.IsEnabled, isEnabled));
        if (affected == 0)
        {
            throw new FeatureFlagNotFoundException(key);
        }

        return await GetByKeyAsync(key) ?? throw new FeatureFlagNotFoundException(key);
    }

    public async ValueTask<FeatureFlag> UpdateEvaluationAsync(FeatureFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        var normalizedKey = FeatureFlagIdentifier.Normalize(flag.Key);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var affected = await _dbContext.FeatureFlags.Where(entity => entity.NormalizedKey == normalizedKey)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entity => entity.RolloutPercentage, flag.RolloutPercentage)
                .SetProperty(entity => entity.StartsAt, flag.StartsAt)
                .SetProperty(entity => entity.EndsAt, flag.EndsAt));
        if (affected == 0)
        {
            throw new FeatureFlagNotFoundException(flag.Key);
        }

        var id = await _dbContext.FeatureFlags.Where(entity => entity.NormalizedKey == normalizedKey).Select(entity => entity.Id).SingleAsync();
        await _dbContext.Set<FeatureFlagTargetUserEntity>().Where(child => child.FeatureFlagId == id).ExecuteDeleteAsync();
        await _dbContext.Set<FeatureFlagEnvironmentEntity>().Where(child => child.FeatureFlagId == id).ExecuteDeleteAsync();
        await _dbContext.Set<FeatureFlagRuleEntity>().Where(child => child.FeatureFlagId == id).ExecuteDeleteAsync();
        await _dbContext.Set<FeatureFlagDependencyEntity>().Where(child => child.FeatureFlagId == id).ExecuteDeleteAsync();

        var replacement = FeatureFlagEntityMapper.ToEntity(flag);
        foreach (var child in replacement.TargetUsers)
        {
            child.FeatureFlag = null!;
            child.FeatureFlagId = id;
            _dbContext.Add(child);
        }

        foreach (var child in replacement.Environments)
        {
            child.FeatureFlag = null!;
            child.FeatureFlagId = id;
            _dbContext.Add(child);
        }

        foreach (var child in replacement.Rules)
        {
            child.FeatureFlag = null!;
            child.FeatureFlagId = id;
            _dbContext.Add(child);
        }

        foreach (var child in replacement.Dependencies)
        {
            child.FeatureFlag = null!;
            child.FeatureFlagId = id;
            _dbContext.Add(child);
        }

        await SaveChangesAsync(flag.Key);
        var updated = await GetByKeyAsync(flag.Key) ?? throw new FeatureFlagNotFoundException(flag.Key);
        await transaction.CommitAsync();
        return updated;
    }

    public async ValueTask<bool> DeleteAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var normalizedKey = FeatureFlagIdentifier.Normalize(key);
        return await _dbContext.FeatureFlags.Where(featureFlag => featureFlag.NormalizedKey == normalizedKey).ExecuteDeleteAsync() > 0;
    }

    private IQueryable<FeatureFlagEntity> FeatureFlagsWithDetails()
    {
        return _dbContext.FeatureFlags
            .Include(featureFlag => featureFlag.TargetUsers)
            .Include(featureFlag => featureFlag.Environments)
            .Include(featureFlag => featureFlag.Rules)
            .Include(featureFlag => featureFlag.Dependencies)
            .AsSplitQuery();
    }

    private async ValueTask SaveChangesAsync(string key)
    {
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsUniqueKeyViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            throw new FeatureFlagAlreadyExistsException(key);
        }
    }

    private static bool IsUniqueKeyViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_feature_flags_normalized_key"
        };
    }
}
