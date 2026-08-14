namespace Flagbit.Core.Models;

public enum FeatureFlagRuleOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith
}

public sealed record FeatureFlagRule
{
    public FeatureFlagRule(string attribute, FeatureFlagRuleOperator @operator, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attribute);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Enum.IsDefined(@operator))
        {
            throw new ArgumentOutOfRangeException(nameof(@operator));
        }

        Attribute = attribute.Trim();
        Operator = @operator;
        Value = value;
    }

    public string Attribute { get; }

    public FeatureFlagRuleOperator Operator { get; }

    public string Value { get; }
}
