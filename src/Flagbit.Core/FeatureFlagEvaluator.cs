namespace Flagbit.Core;

public sealed class FeatureFlagEvaluator
{
    private readonly IFeatureFlagStore _store;

    public FeatureFlagEvaluator(IFeatureFlagStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    public async ValueTask<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var flag = await _store.GetByKeyAsync(key, cancellationToken);

        return flag?.IsEnabled ?? false;
    }
}
