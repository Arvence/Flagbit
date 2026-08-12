namespace Flagbit.Core;

public interface IFeatureFlagStore
{
    ValueTask<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default);

    ValueTask UpdateAsync(FeatureFlag flag, CancellationToken cancellationToken = default);
}
