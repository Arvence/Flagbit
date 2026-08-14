using Flagbit.Core.Models;

namespace Flagbit.Core.Tests;

public sealed class FeatureFlagTests
{
    [Fact]
    public void ConstructorCreatesFlagWithGivenState()
    {
        var flag = new FeatureFlag("new-checkout", true);

        Assert.Equal("new-checkout", flag.Key);
        Assert.True(flag.IsEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsMissingKey(string? key)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FeatureFlag(key!, true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ConstructorRejectsInvalidRolloutPercentage(int percentage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureFlag("new-checkout", true, rolloutPercentage: percentage));
    }

    [Fact]
    public void ConfigureEvaluationReplacesSettings()
    {
        var flag = new FeatureFlag("new-checkout", true, ["user-123"], 30);
        var startsAt = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);
        var endsAt = startsAt.AddHours(2);
        var rule = new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, "enterprise");

        flag.ConfigureEvaluation(["user-456", "USER-456", ""], 50, ["production", "PRODUCTION", ""], [rule], startsAt, endsAt, ["accounts", "ACCOUNTS", ""]);

        Assert.Equal(["user-456"], flag.TargetedUserIds);
        Assert.Equal(50, flag.RolloutPercentage);
        Assert.Equal(["production"], flag.Environments);
        Assert.Equal([rule], flag.Rules);
        Assert.Equal(startsAt, flag.StartsAt);
        Assert.Equal(endsAt, flag.EndsAt);
        Assert.Equal(["accounts"], flag.DependencyKeys);
    }

    [Fact]
    public void ConfigureEvaluationRejectsInvalidSchedule()
    {
        var flag = new FeatureFlag("new-checkout", true);
        var startsAt = new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => flag.ConfigureEvaluation(null, null, startsAt: startsAt, endsAt: startsAt.AddHours(-1)));
    }

    [Fact]
    public void ConfigureEvaluationRejectsSelfDependency()
    {
        var flag = new FeatureFlag("new-checkout", true, targetedUserIds: ["user-123"]);

        Assert.Throws<ArgumentException>(() => flag.ConfigureEvaluation(["user-456"], null, dependencyKeys: ["NEW-CHECKOUT"]));
        Assert.Equal(["user-123"], flag.TargetedUserIds);
        Assert.Empty(flag.DependencyKeys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RuleRejectsMissingAttribute(string? attribute)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FeatureFlagRule(attribute!, FeatureFlagRuleOperator.Equals, "enterprise"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RuleRejectsMissingValue(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, value!));
    }

    [Fact]
    public void RuleRejectsUnknownOperator()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureFlagRule("plan", (FeatureFlagRuleOperator)999, "enterprise"));
    }

    [Fact]
    public void EnableSetsFlagToEnabled()
    {
        var flag = new FeatureFlag("new-checkout", false);

        flag.Enable();

        Assert.True(flag.IsEnabled);
    }

    [Fact]
    public void DisableSetsFlagToDisabled()
    {
        var flag = new FeatureFlag("new-checkout", true);

        flag.Disable();

        Assert.False(flag.IsEnabled);
    }

    [Fact]
    public void StateChangesAreIdempotent()
    {
        var flag = new FeatureFlag("new-checkout", false);

        flag.Enable();
        flag.Enable();
        flag.Disable();
        flag.Disable();

        Assert.False(flag.IsEnabled);
    }

    [Fact]
    public void StateChangesDoNotChangeKey()
    {
        var flag = new FeatureFlag("new-checkout", false);

        flag.Enable();
        flag.Disable();

        Assert.Equal("new-checkout", flag.Key);
    }
}
