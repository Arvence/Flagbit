namespace Flagbit.Core;

/// <summary>
/// Provides feature flags to the core evaluation logic.
/// </summary>
public interface IFeatureFlagStore
{
    ValueTask<FeatureFlag?> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken = default);
}
