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

        var normalizedKey = FeatureFlagEntityMapper.Normalize(key);
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

    public async ValueTask UpdateAsync(FeatureFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        var normalizedKey = FeatureFlagEntityMapper.Normalize(flag.Key);
        var entity = await FeatureFlagsWithDetails()
            .SingleOrDefaultAsync(featureFlag => featureFlag.NormalizedKey == normalizedKey)
            ?? throw new FeatureFlagNotFoundException(flag.Key);

        FeatureFlagEntityMapper.ApplyToEntity(flag, entity);
        await SaveChangesAsync(flag.Key);
    }

    public async ValueTask<bool> DeleteAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var normalizedKey = FeatureFlagEntityMapper.Normalize(key);
        var entity = await _dbContext.FeatureFlags
            .SingleOrDefaultAsync(featureFlag => featureFlag.NormalizedKey == normalizedKey);

        if (entity is null)
        {
            return false;
        }

        _dbContext.FeatureFlags.Remove(entity);
        await _dbContext.SaveChangesAsync();

        return true;
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
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }
}
