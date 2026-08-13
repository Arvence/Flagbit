using Flagbit.Core.Models;

namespace Flagbit.Api.Contracts;

public sealed record CreateFeatureFlagRequest(string? Key, bool IsEnabled = false, IReadOnlyCollection<string>? TargetedUserIds = null, int? RolloutPercentage = null);

public sealed record UpdateFeatureFlagEvaluationRequest(IReadOnlyCollection<string>? TargetedUserIds = null, int? RolloutPercentage = null);

public sealed record FeatureFlagResponse(string Key, bool IsEnabled, IReadOnlyCollection<string> TargetedUserIds, int? RolloutPercentage)
{
    public static FeatureFlagResponse From(FeatureFlag flag)
    {
        return new FeatureFlagResponse(flag.Key, flag.IsEnabled, flag.TargetedUserIds, flag.RolloutPercentage);
    }
}

public sealed record FeatureFlagEvaluationResponse(string Key, bool IsEnabled);
