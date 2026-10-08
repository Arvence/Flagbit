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

    public ValueTask<FeatureFlag> CreateAsync(string key, CancellationToken cancellationToken = default)
    {
        return CreateAsync(key, isEnabled: false, cancellationToken);
    }

    public ValueTask<FeatureFlag> CreateAsync(string key, bool isEnabled, CancellationToken cancellationToken = default)
    {
        return CreateAsync(key, isEnabled, targetedUserIds: null, rolloutPercentage: null, cancellationToken: cancellationToken);
    }

    public async ValueTask<FeatureFlag> CreateAsync(string key, bool isEnabled, IEnumerable<string>? targetedUserIds, int? rolloutPercentage, IEnumerable<string>? environments = null, IEnumerable<FeatureFlagRule>? rules = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, IEnumerable<string>? dependencyKeys = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (await _store.GetByKeyAsync(key, cancellationToken) is not null)
        {
            throw new FeatureFlagAlreadyExistsException(key);
        }

        var flag = new FeatureFlag(key, isEnabled, targetedUserIds, rolloutPercentage, environments, rules, startsAt, endsAt, dependencyKeys);
        await _store.AddAsync(flag, cancellationToken);

        return flag;
    }

    public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _store.GetAllAsync(cancellationToken);
    }

    public async ValueTask<FeatureFlag> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        cancellationToken.ThrowIfCancellationRequested();
        return await _store.GetByKeyAsync(key, cancellationToken)
            ?? throw new FeatureFlagNotFoundException(key);
    }

    public ValueTask<FeatureFlag> EnableAsync(string key, CancellationToken cancellationToken = default)
    {
        return SetEnabledAsync(key, isEnabled: true, cancellationToken);
    }

    public ValueTask<FeatureFlag> DisableAsync(string key, CancellationToken cancellationToken = default)
    {
        return SetEnabledAsync(key, isEnabled: false, cancellationToken);
    }

    public async ValueTask<FeatureFlag> UpdateEvaluationAsync(string key, IEnumerable<string>? targetedUserIds, int? rolloutPercentage, IEnumerable<string>? environments = null, IEnumerable<FeatureFlagRule>? rules = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, IEnumerable<string>? dependencyKeys = null, CancellationToken cancellationToken = default)
    {
        var flag = await GetByKeyAsync(key, cancellationToken);
        flag.ConfigureEvaluation(targetedUserIds, rolloutPercentage, environments, rules, startsAt, endsAt, dependencyKeys);
        return await _store.UpdateEvaluationAsync(flag, cancellationToken);
    }

    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        cancellationToken.ThrowIfCancellationRequested();
        if (!await _store.DeleteAsync(key, cancellationToken))
        {
            throw new FeatureFlagNotFoundException(key);
        }
    }

    private ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        cancellationToken.ThrowIfCancellationRequested();
        return _store.SetEnabledAsync(key, isEnabled, cancellationToken);
    }
}
