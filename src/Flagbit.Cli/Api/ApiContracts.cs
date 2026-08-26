namespace Flagbit.Cli.Api;

internal sealed record CreateFeatureFlagRequest(string Key);

internal sealed record FeatureFlagResponse(string Key, bool IsEnabled);

internal sealed record FeatureFlagEvaluationResponse(string Key, bool IsEnabled);
