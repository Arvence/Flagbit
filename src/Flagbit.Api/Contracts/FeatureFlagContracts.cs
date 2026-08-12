using Flagbit.Core.Models;

namespace Flagbit.Api.Contracts;

public sealed record CreateFeatureFlagRequest(string? Key, bool IsEnabled = false);

public sealed record FeatureFlagResponse(string Key, bool IsEnabled)
{
    public static FeatureFlagResponse From(FeatureFlag flag)
    {
        return new FeatureFlagResponse(flag.Key, flag.IsEnabled);
    }
}

public sealed record FeatureFlagEvaluationResponse(string Key, bool IsEnabled);
