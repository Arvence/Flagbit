using Flagbit.Core.Abstractions;
using Flagbit.Core.Exceptions;
using Flagbit.Core.Models;

namespace Flagbit.Core.Services;

public sealed class FeatureFlagManager
{
    private readonly IFeatureFlagStore _store;

    public FeatureFlagManager(IFeatureFlagStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    public ValueTask<FeatureFlag> CreateAsync(string key)
    {
        return CreateAsync(key, isEnabled: false);
    }

    public ValueTask<FeatureFlag> CreateAsync(string key, bool isEnabled)
    {
        return CreateAsync(key, isEnabled, targetedUserIds: null, rolloutPercentage: null);
    }

    public async ValueTask<FeatureFlag> CreateAsync(string key, bool isEnabled, IEnumerable<string>? targetedUserIds, int? rolloutPercentage, IEnumerable<string>? environments = null, IEnumerable<FeatureFlagRule>? rules = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, IEnumerable<string>? dependencyKeys = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (await _store.GetByKeyAsync(key) is not null)
        {
            throw new FeatureFlagAlreadyExistsException(key);
        }

        var flag = new FeatureFlag(key, isEnabled, targetedUserIds, rolloutPercentage, environments, rules, startsAt, endsAt, dependencyKeys);
        await _store.AddAsync(flag);

        return flag;
    }

    public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync()
    {
        return _store.GetAllAsync();
    }

    public async ValueTask<FeatureFlag> GetByKeyAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return await _store.GetByKeyAsync(key)
            ?? throw new FeatureFlagNotFoundException(key);
    }

    public ValueTask<FeatureFlag> EnableAsync(string key)
    {
        return SetEnabledAsync(key, isEnabled: true);
    }

    public ValueTask<FeatureFlag> DisableAsync(string key)
    {
        return SetEnabledAsync(key, isEnabled: false);
    }

    public async ValueTask<FeatureFlag> UpdateEvaluationAsync(string key, IEnumerable<string>? targetedUserIds, int? rolloutPercentage, IEnumerable<string>? environments = null, IEnumerable<FeatureFlagRule>? rules = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, IEnumerable<string>? dependencyKeys = null)
    {
        var flag = await GetByKeyAsync(key);
        flag.ConfigureEvaluation(targetedUserIds, rolloutPercentage, environments, rules, startsAt, endsAt, dependencyKeys);
        await _store.UpdateAsync(flag);

        return flag;
    }

    public async ValueTask DeleteAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!await _store.DeleteAsync(key))
        {
            throw new FeatureFlagNotFoundException(key);
        }
    }

    private async ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var flag = await GetByKeyAsync(key);

        if (isEnabled)
        {
            flag.Enable();
        }
        else
        {
            flag.Disable();
        }

        await _store.UpdateAsync(flag);

        return flag;
    }
}
