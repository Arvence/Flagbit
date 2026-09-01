using Flagbit.Core.Models;

namespace Flagbit.Api.Contracts;

public sealed record CreateFeatureFlagRequest(string? Key, bool IsEnabled = false, IReadOnlyCollection<string>? TargetedUserIds = null, int? RolloutPercentage = null, IReadOnlyCollection<string>? Environments = null, IReadOnlyCollection<FeatureFlagRuleRequest>? Rules = null, DateTimeOffset? StartsAt = null, DateTimeOffset? EndsAt = null, IReadOnlyCollection<string>? DependencyKeys = null);

public sealed record UpdateFeatureFlagEvaluationRequest(IReadOnlyCollection<string>? TargetedUserIds = null, int? RolloutPercentage = null, IReadOnlyCollection<string>? Environments = null, IReadOnlyCollection<FeatureFlagRuleRequest>? Rules = null, DateTimeOffset? StartsAt = null, DateTimeOffset? EndsAt = null, IReadOnlyCollection<string>? DependencyKeys = null);

public sealed record EvaluateFeatureFlagRequest(string? UserId = null, string? Environment = null, IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record FeatureFlagRuleRequest(string? Attribute, string? Operator, string? Value)
{
    public FeatureFlagRule ToDomain()
    {
        if (string.IsNullOrWhiteSpace(Operator) || !Enum.TryParse<FeatureFlagRuleOperator>(Operator, true, out var parsedOperator) || !Enum.IsDefined(parsedOperator))
        {
            throw new ArgumentException($"'{Operator}' is not a supported feature flag rule operator.", nameof(Operator));
        }

        return new FeatureFlagRule(Attribute ?? string.Empty, parsedOperator, Value ?? string.Empty);
    }
}

public sealed record FeatureFlagRuleResponse(string Attribute, string Operator, string Value)
{
    public static FeatureFlagRuleResponse From(FeatureFlagRule rule)
    {
        return new FeatureFlagRuleResponse(rule.Attribute, rule.Operator.ToString(), rule.Value);
    }
}

public sealed record FeatureFlagResponse(string Key, bool IsEnabled, IReadOnlyCollection<string> TargetedUserIds, int? RolloutPercentage, IReadOnlyCollection<string> Environments, IReadOnlyCollection<FeatureFlagRuleResponse> Rules, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, IReadOnlyCollection<string> DependencyKeys)
{
    public static FeatureFlagResponse From(FeatureFlag flag)
    {
        return new FeatureFlagResponse(flag.Key, flag.IsEnabled, flag.TargetedUserIds, flag.RolloutPercentage, flag.Environments, flag.Rules.Select(FeatureFlagRuleResponse.From).ToArray(), flag.StartsAt, flag.EndsAt, flag.DependencyKeys);
    }
}

public sealed record FeatureFlagEvaluationResponse(string Key, bool IsEnabled);
