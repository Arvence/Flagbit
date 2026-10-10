namespace Flagbit.Sdk;

public sealed record FeatureFlagEvaluationContext(string? UserId = null, string? Environment = null, IReadOnlyDictionary<string, string>? Attributes = null);
