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

        var evaluationContext = context.CurrentTime is null
            ? context with { CurrentTime = DateTimeOffset.UtcNow }
            : context;

        return await IsEnabledAsync(key, evaluationContext, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private async ValueTask<bool> IsEnabledAsync(string key, FeatureFlagContext context, HashSet<string> evaluationPath)
    {
        if (!evaluationPath.Add(key))
        {
            return false;
        }

        try
        {
            var flag = await _store.GetByKeyAsync(key);

            if (flag?.IsEnabled != true)
            {
                return false;
            }

            return MatchesUser(flag, context)
                && MatchesPercentage(flag, context)
                && MatchesEnvironment(flag, context)
                && MatchesRule(flag, context)
                && MatchesSchedule(flag, context)
                && await MatchesDependenciesAsync(flag, context, evaluationPath);
        }
        finally
        {
            evaluationPath.Remove(key);
        }
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
        if (flag.Environments.Count == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(context.Environment)
            && flag.Environments.Contains(context.Environment, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesRule(FeatureFlag flag, FeatureFlagContext context)
    {
        if (flag.Rules.Count == 0)
        {
            return true;
        }

        return context.Attributes is not null
            && flag.Rules.All(rule => MatchesRule(rule, context.Attributes));
    }

    private static bool MatchesSchedule(FeatureFlag flag, FeatureFlagContext context)
    {
        var currentTime = context.CurrentTime ?? DateTimeOffset.UtcNow;
        return MatchesDateTimeRange(flag.StartsAt, flag.EndsAt, currentTime);
    }

    private async ValueTask<bool> MatchesDependenciesAsync(FeatureFlag flag, FeatureFlagContext context, HashSet<string> evaluationPath)
    {
        foreach (var dependencyKey in flag.DependencyKeys)
        {
            if (!await IsEnabledAsync(dependencyKey, context, evaluationPath))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesRule(FeatureFlagRule rule, IReadOnlyDictionary<string, string> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (!string.Equals(attribute.Key, rule.Attribute, StringComparison.OrdinalIgnoreCase) || attribute.Value is null)
            {
                continue;
            }

            return rule.Operator switch
            {
                FeatureFlagRuleOperator.Equals => string.Equals(attribute.Value, rule.Value, StringComparison.OrdinalIgnoreCase),
                FeatureFlagRuleOperator.NotEquals => !string.Equals(attribute.Value, rule.Value, StringComparison.OrdinalIgnoreCase),
                FeatureFlagRuleOperator.Contains => attribute.Value.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
                FeatureFlagRuleOperator.StartsWith => attribute.Value.StartsWith(rule.Value, StringComparison.OrdinalIgnoreCase),
                FeatureFlagRuleOperator.EndsWith => attribute.Value.EndsWith(rule.Value, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        return false;
    }

    private static bool MatchesDateTimeRange(DateTimeOffset? startsAt, DateTimeOffset? endsAt, DateTimeOffset currentTime)
    {
        return (startsAt is null || currentTime >= startsAt)
            && (endsAt is null || currentTime <= endsAt);
    }
}
