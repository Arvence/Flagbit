using Flagbit.Core.Models;

namespace Flagbit.Core.Abstractions;

public interface IFeatureFlagStore
{
    ValueTask<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default);

    ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled, CancellationToken cancellationToken = default);

    ValueTask<FeatureFlag> UpdateEvaluationAsync(FeatureFlag flag, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}
