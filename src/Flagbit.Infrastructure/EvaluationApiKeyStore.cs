using Flagbit.Infrastructure.Persistence;
using Flagbit.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flagbit.Infrastructure;

public sealed class EvaluationApiKeyStore
{
    private readonly FlagbitDbContext _dbContext;

    public EvaluationApiKeyStore(FlagbitDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<EvaluationApiKeyMetadata> AddAsync(string name, string keyHash, CancellationToken cancellationToken = default)
    {
        var entity = new EvaluationApiKeyEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyHash = keyHash,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.EvaluationApiKeys.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new EvaluationApiKeyMetadata(entity.Id, entity.Name, entity.CreatedAt);
    }

    public async Task<IReadOnlyCollection<EvaluationApiKeyMetadata>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.EvaluationApiKeys.AsNoTracking()
            .OrderBy(key => key.CreatedAt)
            .ThenBy(key => key.Id)
            .Select(key => new EvaluationApiKeyMetadata(key.Id, key.Name, key.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<Guid?> FindIdByHashAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EvaluationApiKeys.AsNoTracking()
            .Where(key => key.KeyHash == keyHash)
            .Select(key => (Guid?)key.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EvaluationApiKeys.Where(key => key.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }
}

public sealed record EvaluationApiKeyMetadata(Guid Id, string Name, DateTimeOffset CreatedAt);
