namespace Flagbit.Cli.Api;

internal sealed record CreateFeatureFlagRequest(string Key);

internal sealed record FeatureFlagResponse(string Key, bool IsEnabled);

internal sealed record EvaluateFeatureFlagRequest(string? UserId = null, string? Environment = null, IReadOnlyDictionary<string, string>? Attributes = null);

internal sealed record FeatureFlagEvaluationResponse(string Key, bool IsEnabled);
