using Flagbit.Core.Models;

namespace Flagbit.Infrastructure.Persistence.Entities;

internal sealed class FeatureFlagRuleEntity
{
    public long Id { get; set; }

    public long FeatureFlagId { get; set; }

    public int Position { get; set; }

    public string Attribute { get; set; } = string.Empty;

    public FeatureFlagRuleOperator Operator { get; set; }

    public string Value { get; set; } = string.Empty;

    public FeatureFlagEntity FeatureFlag { get; set; } = null!;
}
