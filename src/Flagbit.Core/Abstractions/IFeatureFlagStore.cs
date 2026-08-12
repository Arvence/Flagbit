using Flagbit.Core.Models;

namespace Flagbit.Core.Abstractions;

public interface IFeatureFlagStore
{
    ValueTask<FeatureFlag?> GetByKeyAsync(string key);

    ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync();

    ValueTask AddAsync(FeatureFlag flag);

    ValueTask UpdateAsync(FeatureFlag flag);

    ValueTask<bool> DeleteAsync(string key);
}
