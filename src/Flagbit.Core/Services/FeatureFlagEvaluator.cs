using Flagbit.Core.Abstractions;
using Flagbit.Core.Models;

namespace Flagbit.Core.Services;

public sealed class FeatureFlagEvaluator
{
    private readonly IFeatureFlagStore _store;

    public FeatureFlagEvaluator(IFeatureFlagStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    public async ValueTask<bool> IsEnabledAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var flag = await _store.GetByKeyAsync(key);

        return flag?.IsEnabled ?? false;
    }

    /// <summary>
    /// Fill these functions with your own logic to evaluate feature flags based on user, percentage, environment, rules, date/time, or dependencies.
    /// </summary>

    public ValueTask<bool> IsEnabledForUserAsync(string key, string userId)
    {
        throw new NotImplementedException();
    }

    public ValueTask<bool> IsEnabledForPercentageAsync(string key, string userId, int percentage)
    {
        throw new NotImplementedException();
    }

    public ValueTask<bool> IsEnabledForEnvironmentAsync(string key, string environment)
    {
        throw new NotImplementedException();
    }

    public ValueTask<bool> IsEnabledForRuleAsync(string key, Func<FeatureFlag, bool> rule)
    {
        throw new NotImplementedException();
    }

    public ValueTask<bool> IsEnabledAtAsync(string key, DateTimeOffset dateTime)
    {
        throw new NotImplementedException();
    }

    public ValueTask<bool> IsEnabledWithDependencyAsync(string key, string dependencyKey)
    {
        throw new NotImplementedException();
    }
}
