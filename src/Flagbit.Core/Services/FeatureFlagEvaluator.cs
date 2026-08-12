using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
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
        return await IsEnabledAsync(key, FeatureFlagContext.Empty);
    }

    public async ValueTask<bool> IsEnabledAsync(string key, FeatureFlagContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(context);

        var flag = await _store.GetByKeyAsync(key);

        if (flag?.IsEnabled != true)
        {
            return false;
        }

        return MatchesUser(flag, context) && MatchesPercentage(flag, context);
    }

    private static bool MatchesUser(FeatureFlag flag, FeatureFlagContext context)
    {
        if (flag.TargetedUserIds.Count == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(context.UserId) && flag.TargetedUserIds.Contains(context.UserId, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesPercentage(FeatureFlag flag, FeatureFlagContext context)
    {
        if (flag.RolloutPercentage is null)
        {
            return true;
        }

        if (flag.RolloutPercentage == 0 || string.IsNullOrWhiteSpace(context.UserId))
        {
            return false;
        }

        if (flag.RolloutPercentage == 100)
        {
            return true;
        }

        var input = Encoding.UTF8.GetBytes($"{flag.Key}:{context.UserId}");
        var hash = SHA256.HashData(input);
        var bucket = BinaryPrimitives.ReadUInt32BigEndian(hash) % 100;

        return bucket < flag.RolloutPercentage;
    }

    private static bool MatchesEnvironment(FeatureFlag flag, FeatureFlagContext context)
    {
        throw new NotImplementedException();
    }

    private static bool MatchesRule(FeatureFlag flag, FeatureFlagContext context)
    {
        throw new NotImplementedException();
    }

    private static bool MatchesSchedule(FeatureFlag flag, FeatureFlagContext context)
    {
        throw new NotImplementedException();
    }

    private ValueTask<bool> MatchesDependenciesAsync(FeatureFlag flag)
    {
        throw new NotImplementedException();
    }
}
