namespace Flagbit.Core;

public sealed class FeatureFlagManager
{
    private readonly IFeatureFlagStore _store;
    private readonly FeatureFlagEvaluator _evaluator;

    public FeatureFlagManager(IFeatureFlagStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _evaluator = new FeatureFlagEvaluator(store);
    }

    public ValueTask<FeatureFlag> CreateAsync(string key, CancellationToken cancellationToken = default)
    {
        return CreateAsync(key, isEnabled: false, cancellationToken);
    }

    public async ValueTask<FeatureFlag> CreateAsync(string key, bool isEnabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (await _store.GetByKeyAsync(key, cancellationToken) is not null)
        {
            throw new InvalidOperationException(
                $"A feature flag with the key '{key}' already exists.");
        }

        var flag = new FeatureFlag(key, isEnabled);
        await _store.AddAsync(flag, cancellationToken);

        return flag;
    }

    public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _store.GetAllAsync(cancellationToken);
    }

    public ValueTask<FeatureFlag> EnableAsync(string key, CancellationToken cancellationToken = default)
    {
        return SetEnabledAsync(key, isEnabled: true, cancellationToken);
    }

    public ValueTask<FeatureFlag> DisableAsync(string key, CancellationToken cancellationToken = default)
    {
        return SetEnabledAsync(key, isEnabled: false, cancellationToken);
    }

    public ValueTask<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
    {
        return _evaluator.IsEnabledAsync(key, cancellationToken);
    }

    private async ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var flag = await _store.GetByKeyAsync(key, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"A feature flag with the key '{key}' does not exist.");

        if (isEnabled)
        {
            flag.Enable();
        }
        else
        {
            flag.Disable();
        }

        await _store.UpdateAsync(flag, cancellationToken);

        return flag;
    }
}
